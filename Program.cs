using System.Globalization;
using PhotoRecull;

// ---------------------------------------------------------------------------
// CLI argument parsing
// ---------------------------------------------------------------------------

string? oneDriveFolder  = null;
string? credentialsPath = "credentials.json";
string? stageFolder     = null;
string? outputCsv       = null;
string? outputHtml      = null;
DateOnly? fromDate      = null;
DateOnly? toDate        = null;
string? browser         = null;
string? cachePath       = null;
bool repick             = false;
bool noCache            = false;
bool noReport           = false;
bool dryRun             = false;
bool deleteMode         = false;
bool assumeYes          = false;
bool wantStage          = false;   // --stage given (folder may be defaulted)
bool wantHtml           = false;   // --report given (file may be defaulted)

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--onedrive":    oneDriveFolder  = args[++i]; break;
        case "--credentials": credentialsPath = args[++i]; break;
        case "--stage":
            wantStage = true;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) stageFolder = args[++i];
            break;
        case "--output":      outputCsv       = args[++i]; break;
        case "--report":
            wantHtml = true;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) outputHtml = args[++i];
            break;
        case "--browser":     browser         = args[++i]; break;
        case "--cache":       cachePath       = args[++i]; break;
        case "--repick":      repick          = true;      break;
        case "--no-cache":    noCache         = true;      break;
        case "--no-report":   noReport        = true;      break;
        case "--dry-run":     dryRun          = true;      break;
        case "--delete":      deleteMode      = true;      break;
        case "--yes":
        case "--force":       assumeYes       = true;      break;
        case "--from":        fromDate        = ParseDateArg(args[++i], "--from", startOfMonth: true);  break;
        case "--to":          toDate          = ParseDateArg(args[++i], "--to",   startOfMonth: false); break;
        case "--help":
        case "-h":
            PrintHelp();
            return 0;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            PrintHelp();
            return 1;
    }
}

if (oneDriveFolder is null)
{
    Console.Error.WriteLine("ERROR: --onedrive <folder> is required.");
    PrintHelp();
    return 1;
}

// Dry run is the default. An action mode (--stage / --delete) overrides it.
dryRun = !wantStage && !deleteMode;

if (fromDate.HasValue && toDate.HasValue && fromDate > toDate)
{
    Console.Error.WriteLine("ERROR: --from date must be earlier than --to date.");
    return 1;
}

// Caching is on by default: the pick is saved to (and reused from) a file named
// after the date range, unless --no-cache. --cache overrides the path; --repick
// forces a fresh pick.
string? effectiveCache = noCache ? null : (cachePath ?? $"{DefaultLabel(fromDate, toDate)}.json");

// When reusing a cache we never touch Google, so credentials aren't required.
bool useCache = effectiveCache is not null && File.Exists(effectiveCache) && !repick;

if (!useCache && !File.Exists(credentialsPath))
{
    Console.Error.WriteLine($"""
        ERROR: Google OAuth credentials file not found: {credentialsPath}

        To set up:
          1. Go to https://console.cloud.google.com
          2. Create a project and enable "Google Photos Library API"
          3. Create OAuth 2.0 credentials (Desktop app) and download as credentials.json
          4. Place credentials.json in the same folder as this app, or pass --credentials <path>
        """);
    return 1;
}

// ---------------------------------------------------------------------------
// Step 1: Authenticate and fetch Google Photos timestamps
// ---------------------------------------------------------------------------

var rangeLabel = DescribeRange(fromDate, toDate);
Console.WriteLine($"Date range  : {rangeLabel}");

List<DateTimeOffset> googleTimestamps;

if (useCache)
{
    // Reuse a previous pick — no picker, no Google auth.
    try
    {
        var cache = PickCache.Load(effectiveCache!);
        googleTimestamps = cache.Timestamps;
        Console.WriteLine($"Loaded {googleTimestamps.Count} Google timestamps from cache: {effectiveCache}");
        Console.WriteLine($"  captured {cache.CapturedAtUtc:yyyy-MM-dd HH:mm} UTC for range {DescribeRange(cache.From, cache.To)}");
        Console.WriteLine("  (use --repick to select photos again)");
        if (cache.From != fromDate || cache.To != toDate)
            Console.WriteLine($"  WARNING: cache range differs from current --from/--to ({rangeLabel}). Re-run with --repick to refresh.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"ERROR: Failed to read cache {effectiveCache}: {ex.Message}");
        return 1;
    }
}
else
{
    Console.WriteLine("Authenticating with Google Photos (Picker API)...");

    // Picker uses a different scope than the retired Library API, so it needs its
    // own token store — reusing the old one would load a token without the picker scope.
    var tokenStore = Path.Combine(AppContext.BaseDirectory, "token_store_picker");
    var pickerClient = new GooglePhotosPickerClient(credentialsPath, tokenStore, browser);

    HttpClient http;
    try
    {
        http = await pickerClient.AuthorizeAsync();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"ERROR: Google Photos authentication failed: {ex.Message}");
        return 1;
    }

    try
    {
        int lastReported = 0;
        var progress = new Progress<int>(count =>
        {
            if (count - lastReported >= 50)
            {
                Console.Write($"\rReading picked photos... {count} items");
                lastReported = count;
            }
        });

        googleTimestamps = await pickerClient.PickTimestampsAsync(http, progress);
        Console.WriteLine($"\rReading picked photos... done ({googleTimestamps.Count} items).    ");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"\nERROR: Failed to read picked Google Photos: {ex.Message}");
        return 1;
    }

    // Persist for subsequent review/stage/delete runs (on by default).
    if (effectiveCache is not null)
    {
        try
        {
            PickCache.Save(effectiveCache, new PickCacheData(DateTimeOffset.UtcNow, fromDate, toDate, googleTimestamps));
            Console.WriteLine($"Cached {googleTimestamps.Count} Google timestamps to: {effectiveCache} (reused on the next run; --repick to refresh)");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"(Warning: could not write cache {effectiveCache}: {ex.Message})");
        }
    }
}

// ---------------------------------------------------------------------------
// Step 2: Scan OneDrive folder, then apply date range filter
// ---------------------------------------------------------------------------

Console.Write("Scanning OneDrive folder... ");
List<OneDrivePhoto> oneDrivePhotos;
try
{
    var all = OneDriveScanner.ScanFolder(oneDriveFolder);

    // Apply date range: iPhone files outside the range are excluded entirely
    // (the user is only interested in that window).
    if (fromDate.HasValue || toDate.HasValue)
    {
        var fromUtc = fromDate.HasValue
            ? new DateTimeOffset(fromDate.Value, TimeOnly.MinValue, TimeSpan.Zero)
            : DateTimeOffset.MinValue;
        var toUtc = toDate.HasValue
            ? new DateTimeOffset(toDate.Value, TimeOnly.MaxValue, TimeSpan.Zero)
            : DateTimeOffset.MaxValue;

        oneDrivePhotos = all
            .Where(p => !p.IsIphone                                              // non-iPhone always included as skipped
                     || !p.Timestamp.HasValue                                    // no timestamp → included as skipped
                     || (p.Timestamp.Value >= fromUtc && p.Timestamp.Value <= toUtc))
            .ToList();

        var excluded = all.Count - oneDrivePhotos.Count;
        Console.WriteLine($"{all.Count} files found, {excluded} outside date range filtered out.");
    }
    else
    {
        oneDrivePhotos = all;
        Console.WriteLine($"{oneDrivePhotos.Count} files found.");
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"\nERROR: {ex.Message}");
    return 1;
}

// ---------------------------------------------------------------------------
// Step 3: Match
// ---------------------------------------------------------------------------

Console.WriteLine("Matching photos...");
var result = PhotoMatcher.Match(googleTimestamps, oneDrivePhotos);

// ---------------------------------------------------------------------------
// Step 4: Report
// ---------------------------------------------------------------------------

Console.WriteLine();
Console.WriteLine("=== RESULTS ===");
Console.WriteLine($"  Date range            : {rangeLabel}");
Console.WriteLine($"  iPhone photos scanned : {result.Matched.Count + result.Unmatched.Count}");
Console.WriteLine($"  Non-iPhone (skipped)  : {result.Skipped.Count}");
Console.WriteLine($"  Matched (keep)        : {result.Matched.Count}");
Console.WriteLine($"  Unmatched (candidates): {result.Unmatched.Count}");
Console.WriteLine();

if (result.Unmatched.Count > 0)
{
    Console.WriteLine("Files NOT found in Google Photos (candidates for removal):");
    Console.WriteLine(new string('-', 80));
    Console.WriteLine($"{"#",-5} {"Timestamp (UTC)",-22} {"Source",-10} {"File Name"}");
    Console.WriteLine(new string('-', 80));

    int idx = 1;
    foreach (var photo in result.Unmatched.OrderByDescending(p => p.Timestamp))
    {
        var ts = photo.Timestamp.HasValue
            ? photo.Timestamp.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : "(unknown)";
        Console.WriteLine($"{idx,-5} {ts,-22} {photo.TimestampSource ?? "?",-10} {photo.FileName}");
        idx++;
    }
    Console.WriteLine(new string('-', 80));
}
else
{
    Console.WriteLine("No unmatched files — nothing to delete.");
}

// Optional CSV export
if (outputCsv is not null)
{
    WriteCsv(outputCsv, result.Unmatched);
    Console.WriteLine($"\nCSV report saved to: {outputCsv}");
}

// HTML report: explicit via --report, or automatic on a dry run (unless --no-report).
if (wantHtml || (dryRun && !noReport))
{
    var htmlPath = outputHtml ?? $"{DefaultLabel(fromDate, toDate)}.html";
    Console.Write("Generating HTML report (decoding HEIC thumbnails)... ");
    HtmlReportGenerator.Write(htmlPath, result, oneDriveFolder, fromDate, toDate);
    Console.WriteLine("done.");
    Console.WriteLine($"HTML report saved to: {Path.GetFullPath(htmlPath)}");
    // The report is a local file with no Google login — always use the default browser.
    if (!BrowserLauncher.Open(Path.GetFullPath(htmlPath)))
        Console.Error.WriteLine("(Could not auto-open the report; open it manually.)");
}

if (result.Unmatched.Count == 0)
    return 0;

// ---------------------------------------------------------------------------
// Step 5: Act (stage or delete)
// ---------------------------------------------------------------------------

if (dryRun)
{
    Console.WriteLine("\nDry run complete. No files were moved or deleted.");
    if (result.Unmatched.Count > 0)
        PrintNextSteps(oneDriveFolder, fromDate, toDate, effectiveCache, stageFolder);
    return 0;
}

if (wantStage)
{
    var stageDir = Path.GetFullPath(stageFolder ?? $"_staged_{DefaultLabel(fromDate, toDate)}");
    Console.WriteLine($"\nAbout to MOVE {result.Unmatched.Count} file(s) to: {stageDir}");
    if (!assumeYes)
    {
        Console.Write("Continue? (yes/no): ");
        if (!ConfirmYes()) { Console.WriteLine("Aborted."); return 0; }
    }

    System.IO.Directory.CreateDirectory(stageDir);
    int moved = 0, failed = 0;
    foreach (var photo in result.Unmatched)
    {
        var dest = System.IO.Path.Combine(stageDir, photo.FileName);
        try
        {
            File.Move(photo.Path, dest, overwrite: false);
            moved++;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  FAILED to move {photo.FileName}: {ex.Message}");
            failed++;
        }
    }
    Console.WriteLine($"Moved {moved} file(s) to: {stageDir} {(failed > 0 ? $"({failed} failed)" : "")}");
    Console.WriteLine("Review that folder, then delete it manually when satisfied.");
    return 0;
}

if (deleteMode)
{
    Console.WriteLine($"\nAbout to PERMANENTLY DELETE {result.Unmatched.Count} file(s).");
    Console.WriteLine("This cannot be undone! Review the report first (run without --delete), or use --stage to move them instead.");
    if (assumeYes)
    {
        Console.WriteLine("--yes given: skipping confirmation.");
    }
    else
    {
        Console.Write("Type \"DELETE\" to confirm: ");
        var input = Console.ReadLine()?.Trim();
        if (input != "DELETE") { Console.WriteLine("Aborted."); return 0; }
    }

    int deleted = 0, failed = 0;
    foreach (var photo in result.Unmatched)
    {
        try
        {
            File.Delete(photo.Path);
            deleted++;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  FAILED to delete {photo.FileName}: {ex.Message}");
            failed++;
        }
    }
    Console.WriteLine($"Deleted {deleted} file(s). {(failed > 0 ? $"{failed} failed." : "")}");
    return 0;
}

return 0;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

static DateOnly ParseDateArg(string value, string argName, bool startOfMonth)
{
    // Accept "YYYY-MM" (expand to first/last day) or "YYYY-MM-DD"
    if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var full))
        return full;

    if (DateOnly.TryParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var month))
    {
        return startOfMonth
            ? new DateOnly(month.Year, month.Month, 1)
            : new DateOnly(month.Year, month.Month,
                DateTime.DaysInMonth(month.Year, month.Month));
    }

    Console.Error.WriteLine($"ERROR: {argName} value \"{value}\" is not a valid date. Use YYYY-MM or YYYY-MM-DD.");
    Environment.Exit(1);
    return default;
}

static string DescribeRange(DateOnly? from, DateOnly? to) => (from, to) switch
{
    (not null, not null) => $"{from:yyyy-MM-dd} – {to:yyyy-MM-dd}",
    (not null, null)     => $"from {from:yyyy-MM-dd}",
    (null, not null)     => $"up to {to:yyyy-MM-dd}",
    _                    => "entire library"
};

static void PrintHelp()
{
    Console.WriteLine("""
        PhotoRecull — Remove OneDrive photos already deleted from Google Photos

        USAGE:
          dotnet run -- --onedrive <folder> [options] <mode>

        REQUIRED:
          --onedrive <folder>     Path to your local OneDrive Camera Roll folder

        DATE RANGE (optional — process one month at a time):
          --from <date>           Start date: YYYY-MM or YYYY-MM-DD
          --to   <date>           End date:   YYYY-MM or YYYY-MM-DD
                                  When using YYYY-MM, --from expands to the 1st
                                  and --to expands to the last day of the month.
                                  NOTE: this filters the OneDrive side only. The
                                  Google Photos Picker has no date filter, so in
                                  the browser you must select every photo that
                                  still exists in Google Photos for this range.
          Examples:
            --from 2024-01 --to 2024-01        all of January 2024
            --from 2023-06-01 --to 2023-06-30  same, explicit days
            --from 2022-01 --to 2022-12        all of 2022

        AUTHENTICATION:
          --credentials <file>   Path to Google OAuth credentials JSON
                                 (default: credentials.json in current directory)
          --browser <name|path>  Browser used for Google sign-in and the picker:
                                 chrome, edge, firefox, an executable name, or a
                                 full path. Default: system default. Use this to
                                 sign in to Google via a browser other than your
                                 default (e.g. --browser chrome). The HTML report
                                 always opens in your default browser.

        MODE (default: dry run):
          (no mode flag)         Dry run — show candidates and write the report;
                                 change nothing. This is the default.
          --dry-run              Explicit dry run (same as the default).
          --stage [dir]          Move candidates to a folder for manual review.
                                 Folder is optional; defaults to _staged_<range>
                                 (e.g. _staged_2026-08).
          --delete               Permanently delete candidates (requires confirmation)
          --yes, --force         Skip the confirmation prompt for --stage/--delete

        PICKER CACHE (on by default — pick once, then review/stage/delete offline):
          By default the pick is saved to <range>.json (e.g. 2026-08.json) and
          reused automatically on later runs, so the picker and Google sign-in
          are skipped. This means the very next command (e.g. --stage) reuses
          your selection with no re-picking.
          --cache <file.json>    Override the cache file path.
          --repick               Ignore the cache and open the picker again,
                                 overwriting it (use when Google Photos changed).
          --no-cache             Don't save or reuse a cache (pick every run).

        OUTPUT:
          --report [file.html]   Generate a visual HTML review (thumbnails).
                                 Generated automatically on --dry-run; file is
                                 optional and defaults to <range>.html
                                 (e.g. 2026-08.html). Opens in your default browser.
          --no-report            Don't generate the automatic dry-run report.
          --output <file.csv>    Write candidates list to CSV
          --help                 Show this help
        """);
}

static void WriteCsv(string path, List<OneDrivePhoto> photos)
{
    using var sw = new StreamWriter(path);
    sw.WriteLine("TimestampUTC,TimestampSource,FileName,FullPath");
    foreach (var p in photos.OrderByDescending(x => x.Timestamp))
    {
        var ts = p.Timestamp.HasValue
            ? p.Timestamp.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : "";
        sw.WriteLine($"{ts},{p.TimestampSource ?? ""},{EscapeCsv(p.FileName)},{EscapeCsv(p.Path)}");
    }
}

static string EscapeCsv(string s) =>
    s.Contains(',') || s.Contains('"') || s.Contains('\n')
        ? $"\"{s.Replace("\"", "\"\"")}\""
        : s;

static bool ConfirmYes()
{
    var input = Console.ReadLine()?.Trim().ToLowerInvariant();
    return input is "yes" or "y";
}

// A filesystem-safe label derived from the date range, used to name the
// default staging folder and HTML report (e.g. "2026-08", "2026-08-03_2026-08-09").
static string DefaultLabel(DateOnly? from, DateOnly? to) => (from, to) switch
{
    (not null, not null) when IsWholeMonth(from.Value, to.Value) => $"{from:yyyy-MM}",
    (not null, not null) => $"{from:yyyy-MM-dd}_{to:yyyy-MM-dd}",
    (not null, null)     => $"from-{from:yyyy-MM-dd}",
    (null, not null)     => $"upto-{to:yyyy-MM-dd}",
    _                    => "all",
};

static bool IsWholeMonth(DateOnly from, DateOnly to) =>
    from.Day == 1 && from.Year == to.Year && from.Month == to.Month
    && to.Day == DateTime.DaysInMonth(to.Year, to.Month);

// After a dry run, print the exact command to move the candidates to staging.
static void PrintNextSteps(string oneDriveFolder, DateOnly? from, DateOnly? to,
    string? effectiveCache, string? stageFolder)
{
    var stageDir = stageFolder ?? $"_staged_{DefaultLabel(from, to)}";

    string BaseCmd()
    {
        var c = $"dotnet run -- --onedrive \"{oneDriveFolder}\"";
        if (from.HasValue) c += $" --from {from:yyyy-MM-dd}";
        if (to.HasValue)   c += $" --to {to:yyyy-MM-dd}";
        if (effectiveCache is not null) c += $" --cache \"{effectiveCache}\"";
        return c;
    }

    Console.WriteLine();
    Console.WriteLine("Next steps:");
    Console.WriteLine("  Move the candidates to a staging folder for review:");
    Console.WriteLine($"    {BaseCmd()} --stage \"{stageDir}\"");
    Console.WriteLine("  Or delete them permanently (irreversible; add --yes to skip the prompt):");
    Console.WriteLine($"    {BaseCmd()} --delete");
    Console.WriteLine(effectiveCache is not null
        ? $"  Both reuse the cached pick in {effectiveCache} — no re-picking (use --repick to refresh)."
        : "  (you used --no-cache, so these re-open the picker; drop --no-cache to reuse the pick.)");
}
