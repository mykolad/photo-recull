using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using PhotoRecull.Core.Library;
using PhotoRecull.Core.Settings;

namespace PhotoRecull.Core.Storage;

// The Windows app's state (settings, cleanups, picks, Google sign-in) in one
// SQLite file. Writes that belong together share a transaction, so the app never
// sees, say, a new pick next to the old cleanup details after an interruption.
public sealed class AppStateDatabase
{
    private const int CurrentSchemaVersion = 1;
    private const string DayFormat = "yyyy-MM-dd";

    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };

    private readonly string _connectionString;

    public AppStateDatabase(string databasePath)
    {
        DatabasePath = databasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
        OpenOrRecreateIfDamaged();
    }

    public string DatabasePath { get; }

    // ------------------------------------------------------------------ settings

    public AppSettings LoadSettings()
    {
        using var connection = Open();
        var values = new Dictionary<string, string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT key, value FROM settings";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                values[reader.GetString(0)] = reader.GetString(1);
        }

        return new AppSettings
        {
            SignInBrowser = values.GetValueOrDefault(nameof(AppSettings.SignInBrowser)),
            Targets = values.TryGetValue(nameof(AppSettings.Targets), out var targets)
                ? JsonSerializer.Deserialize<List<TargetSettings>>(targets, JsonOptions) ?? []
                : [],
            ShowCaptureTimesInUtc = values.GetValueOrDefault(nameof(AppSettings.ShowCaptureTimesInUtc)) == "true",
            DeveloperCredentialsPath = values.GetValueOrDefault(nameof(AppSettings.DeveloperCredentialsPath)),
            SetupCompletedAt = values.TryGetValue(nameof(AppSettings.SetupCompletedAt), out var completed)
                ? DateTimeOffset.Parse(completed, CultureInfo.InvariantCulture)
                : null,
        };
    }

    public void SaveSettings(AppSettings settings)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        SetOrRemoveSetting(transaction, nameof(AppSettings.SignInBrowser), settings.SignInBrowser);
        SetOrRemoveSetting(transaction, nameof(AppSettings.Targets), JsonSerializer.Serialize(settings.Targets, JsonOptions));
        SetOrRemoveSetting(transaction, nameof(AppSettings.ShowCaptureTimesInUtc), settings.ShowCaptureTimesInUtc ? "true" : "false");
        SetOrRemoveSetting(transaction, nameof(AppSettings.DeveloperCredentialsPath), settings.DeveloperCredentialsPath);
        SetOrRemoveSetting(transaction, nameof(AppSettings.SetupCompletedAt),
            settings.SetupCompletedAt?.ToString("O", CultureInfo.InvariantCulture));
        transaction.Commit();
    }

    // ------------------------------------------------------------------ cleanups

    public List<Cleanup> LoadCleanups()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, from_day, to_day, state, picked_count, picked_at, last_activity_at, targets,
                   earliest_picked_day, latest_picked_day, to_remove_count, kept_by_user_count,
                   staged_count, removed_count, freed_bytes
            FROM cleanups
            ORDER BY from_day, to_day
            """;
        using var reader = command.ExecuteReader();
        var cleanups = new List<Cleanup>();
        while (reader.Read())
        {
            cleanups.Add(new Cleanup(
                Guid.Parse(reader.GetString(0)),
                ParseDay(reader.GetString(1)),
                ParseDay(reader.GetString(2)),
                Enum.Parse<CleanupState>(reader.GetString(3)),
                reader.GetInt32(4),
                DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture))
            {
                Targets = reader.IsDBNull(7) ? null : JsonSerializer.Deserialize<List<TargetSettings>>(reader.GetString(7), JsonOptions),
                EarliestPickedDay = reader.IsDBNull(8) ? null : ParseDay(reader.GetString(8)),
                LatestPickedDay = reader.IsDBNull(9) ? null : ParseDay(reader.GetString(9)),
                ToRemoveCount = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                KeptByUserCount = reader.GetInt32(11),
                StagedCount = reader.GetInt32(12),
                RemovedCount = reader.GetInt32(13),
                FreedBytes = reader.GetInt64(14),
            });
        }
        return cleanups;
    }

    public void SaveCleanup(Cleanup cleanup)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        UpsertCleanup(transaction, cleanup);
        transaction.Commit();
    }

    // ------------------------------------------------------------------ picks

    public PickCacheData? LoadPick(Guid cleanupId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.captured_at_utc_ticks, c.picked_at, c.earliest_picked_day, c.latest_picked_day
            FROM picked_photos p JOIN cleanups c ON c.id = p.cleanup_id
            WHERE p.cleanup_id = $cleanupId
            """;
        command.Parameters.AddWithValue("$cleanupId", cleanupId.ToString());
        using var reader = command.ExecuteReader();

        var timestamps = new List<DateTimeOffset>();
        DateTimeOffset pickedAt = default;
        DateOnly? earliest = null, latest = null;
        while (reader.Read())
        {
            timestamps.Add(new DateTimeOffset(reader.GetInt64(0), TimeSpan.Zero));
            pickedAt = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
            earliest = reader.IsDBNull(2) ? null : ParseDay(reader.GetString(2));
            latest = reader.IsDBNull(3) ? null : ParseDay(reader.GetString(3));
        }
        return timestamps.Count == 0 ? null : new PickCacheData(pickedAt.ToUniversalTime(), earliest, latest, timestamps);
    }

    // The pick and the cleanup that describes it are replaced together.
    public void SavePickWithCleanup(Cleanup cleanup, IReadOnlyCollection<DateTimeOffset> pickedTimestamps)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        UpsertCleanup(transaction, cleanup);

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM picked_photos WHERE cleanup_id = $cleanupId";
            delete.Parameters.AddWithValue("$cleanupId", cleanup.Id.ToString());
            delete.ExecuteNonQuery();
        }

        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO picked_photos (cleanup_id, captured_at_utc_ticks) VALUES ($cleanupId, $ticks)";
            insert.Parameters.AddWithValue("$cleanupId", cleanup.Id.ToString());
            var ticks = insert.Parameters.Add("$ticks", SqliteType.Integer);
            foreach (var timestamp in pickedTimestamps)
            {
                ticks.Value = timestamp.UtcTicks;
                insert.ExecuteNonQuery();
            }
        }
        transaction.Commit();
    }

    // ------------------------------------------------------------------ key/value used by the Google token store

    public string? GetGoogleTokenValue(string key) =>
        ScalarOrNull("SELECT value FROM google_token WHERE key = $key", ("$key", key));

    public void SetGoogleTokenValue(string key, string value) =>
        Execute("INSERT INTO google_token (key, value) VALUES ($key, $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            ("$key", key), ("$value", value));

    public void DeleteGoogleTokenValue(string key) =>
        Execute("DELETE FROM google_token WHERE key = $key", ("$key", key));

    public void ClearGoogleToken() => Execute("DELETE FROM google_token");

    public bool HasGoogleToken() => ScalarOrNull("SELECT 1 FROM google_token LIMIT 1") is not null;

    // ------------------------------------------------------------------ plumbing

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragmas = connection.CreateCommand();
        pragmas.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        pragmas.ExecuteNonQuery();
        return connection;
    }

    // A file that isn't a usable database (damaged, or not SQLite at all) is moved
    // aside and a fresh one created, so the app can still start; the old file is
    // kept for recovery.
    private void OpenOrRecreateIfDamaged()
    {
        try
        {
            CreateOrMigrateSchema();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 11 or 26) // SQLITE_CORRUPT, SQLITE_NOTADB
        {
            SqliteConnection.ClearAllPools();
            File.Move(DatabasePath, $"{DatabasePath}.damaged-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
            CreateOrMigrateSchema();
        }
    }

    private void CreateOrMigrateSchema()
    {
        using var connection = Open();
        using (var walMode = connection.CreateCommand())
        {
            walMode.CommandText = "PRAGMA journal_mode = WAL";
            walMode.ExecuteNonQuery();
        }

        int version = Convert.ToInt32(ScalarOrNull(connection, "PRAGMA user_version"), CultureInfo.InvariantCulture);
        if (version >= CurrentSchemaVersion)
            return;

        using var transaction = connection.BeginTransaction();
        using var create = connection.CreateCommand();
        create.Transaction = transaction;
        create.CommandText = $"""
            CREATE TABLE settings (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL);

            CREATE TABLE cleanups (
                id                  TEXT PRIMARY KEY,
                from_day            TEXT NOT NULL,
                to_day              TEXT NOT NULL,
                state               TEXT NOT NULL,
                picked_count        INTEGER NOT NULL,
                picked_at           TEXT NOT NULL,
                last_activity_at    TEXT NOT NULL,
                targets             TEXT,
                earliest_picked_day TEXT,
                latest_picked_day   TEXT,
                to_remove_count     INTEGER,
                kept_by_user_count  INTEGER NOT NULL DEFAULT 0,
                staged_count        INTEGER NOT NULL DEFAULT 0,
                removed_count       INTEGER NOT NULL DEFAULT 0,
                freed_bytes         INTEGER NOT NULL DEFAULT 0);

            CREATE TABLE picked_photos (
                cleanup_id            TEXT NOT NULL REFERENCES cleanups(id) ON DELETE CASCADE,
                captured_at_utc_ticks INTEGER NOT NULL);
            CREATE INDEX picked_photos_by_cleanup ON picked_photos (cleanup_id);

            CREATE TABLE google_token (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL);

            PRAGMA user_version = {CurrentSchemaVersion};
            """;
        create.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void UpsertCleanup(SqliteTransaction transaction, Cleanup cleanup)
    {
        using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cleanups (id, from_day, to_day, state, picked_count, picked_at, last_activity_at, targets,
                                  earliest_picked_day, latest_picked_day, to_remove_count, kept_by_user_count,
                                  staged_count, removed_count, freed_bytes)
            VALUES ($id, $from, $to, $state, $pickedCount, $pickedAt, $lastActivityAt, $targets,
                    $earliest, $latest, $toRemove, $keptByUser, $staged, $removed, $freed)
            ON CONFLICT(id) DO UPDATE SET
                from_day = excluded.from_day, to_day = excluded.to_day, state = excluded.state,
                picked_count = excluded.picked_count, picked_at = excluded.picked_at,
                last_activity_at = excluded.last_activity_at, targets = excluded.targets,
                earliest_picked_day = excluded.earliest_picked_day, latest_picked_day = excluded.latest_picked_day,
                to_remove_count = excluded.to_remove_count, kept_by_user_count = excluded.kept_by_user_count,
                staged_count = excluded.staged_count, removed_count = excluded.removed_count,
                freed_bytes = excluded.freed_bytes
            """;
        command.Parameters.AddWithValue("$id", cleanup.Id.ToString());
        command.Parameters.AddWithValue("$from", FormatDay(cleanup.From));
        command.Parameters.AddWithValue("$to", FormatDay(cleanup.To));
        command.Parameters.AddWithValue("$state", cleanup.State.ToString());
        command.Parameters.AddWithValue("$pickedCount", cleanup.PickedCount);
        command.Parameters.AddWithValue("$pickedAt", cleanup.PickedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$lastActivityAt", cleanup.LastActivityAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$targets",
            cleanup.Targets is null ? DBNull.Value : JsonSerializer.Serialize(cleanup.Targets, JsonOptions));
        command.Parameters.AddWithValue("$earliest", cleanup.EarliestPickedDay is { } earliest ? FormatDay(earliest) : DBNull.Value);
        command.Parameters.AddWithValue("$latest", cleanup.LatestPickedDay is { } latest ? FormatDay(latest) : DBNull.Value);
        command.Parameters.AddWithValue("$toRemove", cleanup.ToRemoveCount is { } toRemove ? toRemove : DBNull.Value);
        command.Parameters.AddWithValue("$keptByUser", cleanup.KeptByUserCount);
        command.Parameters.AddWithValue("$staged", cleanup.StagedCount);
        command.Parameters.AddWithValue("$removed", cleanup.RemovedCount);
        command.Parameters.AddWithValue("$freed", cleanup.FreedBytes);
        command.ExecuteNonQuery();
    }

    private static void SetOrRemoveSetting(SqliteTransaction transaction, string key, string? value)
    {
        using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = value is null
            ? "DELETE FROM settings WHERE key = $key"
            : "INSERT INTO settings (key, value) VALUES ($key, $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value";
        command.Parameters.AddWithValue("$key", key);
        if (value is not null)
            command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    private string? ScalarOrNull(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = Open();
        return ScalarOrNull(connection, sql, parameters);
    }

    private static string? ScalarOrNull(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return command.ExecuteScalar() is { } result and not DBNull
            ? Convert.ToString(result, CultureInfo.InvariantCulture)
            : null;
    }

    private static string FormatDay(DateOnly day) => day.ToString(DayFormat, CultureInfo.InvariantCulture);

    private static DateOnly ParseDay(string day) => DateOnly.ParseExact(day, DayFormat, CultureInfo.InvariantCulture);
}
