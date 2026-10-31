using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PhotoRecull.Core.Library;

namespace PhotoRecull.Core;

public record FileFailure(PhotoFile Photo, string Reason);

public record FileActionResult(int SucceededCount, List<FileFailure> Failures, long SucceededBytes = 0);

// What the shell said about deleting one file: its error code, and whether it
// gave up (the file couldn't be recycled, or the user said no to deleting it
// permanently).
internal readonly record struct ShellDeleteOutcome(int ErrorCode, bool Aborted);

public static class FileActions
{
    // A stage folder that can't be created (the path names a file, a drive that
    // isn't there, no permission) fails every photo for that target instead of
    // throwing, so the result can still be shown. Photos keep their folders below
    // the target's (2026\07\IMG_0001.HEIC), so two photos named alike in different
    // months don't collide, and each can be moved back where it came from.
    // onPhotoDone hears about each photo as soon as it's done, before the next one
    // starts, so the outcome can be saved as the run goes.
    public static FileActionResult MoveToStageFolder(
        IEnumerable<PhotoFile> photos, string stageFolder, string targetFolder, Action<RunFile>? onPhotoDone = null)
    {
        if (IsSameOrInside(stageFolder, targetFolder))
            return FailEvery(photos, StageFolderInsideTarget, onPhotoDone);
        try
        {
            Directory.CreateDirectory(stageFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return FailEvery(photos, $"Can't use the stage folder: {ex.Message}", onPhotoDone);
        }
        string StagedPath(string path) => Path.Combine(stageFolder, FolderBelow(targetFolder, path), Path.GetFileName(path));
        return ApplyToEachCollectingFailures(photos,
            path =>
            {
                var stagedPath = StagedPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
                File.Move(path, stagedPath, overwrite: false);
                return stagedPath;
            },
            onPhotoDone,
            // Both parts of a Live Photo are checked before either moves, so a name
            // already taken never leaves the video staged without its photo.
            checkBeforeActing: photo =>
            {
                if (FilesOf(photo).Any(path => File.Exists(StagedPath(path))))
                    throw new IOException("A file with this name is already in the stage folder", FileExists);
            });
    }

    public const string StageFolderInsideTarget =
        "The stage folder is inside the target's folder, so the next scan would find these photos again";

    // A stage folder inside a target is read by its next scan (Synology's is
    // recursive), and photos waiting there for review come up for removal again.
    public static bool IsSameOrInside(string folder, string parent)
    {
        try
        {
            return WithEndingSeparator(folder).StartsWith(WithEndingSeparator(parent), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    // A drive root (Z:\) already ends in one; other folders get one, so Camera
    // Roll isn't taken to contain "Camera Roll staged".
    private static string WithEndingSeparator(string folder)
    {
        var fullPath = NetworkPathOf(Path.GetFullPath(folder));
        return Path.EndsInDirectorySeparator(fullPath) ? fullPath : fullPath + Path.DirectorySeparatorChar;
    }

    // A mapped drive and its share are one folder by two names: Z:\Photos and
    // \\nas\home\Photos. A mapping that's disconnected still has its share.
    private static string NetworkPathOf(string fullPath)
    {
        if (!OperatingSystem.IsWindows() || fullPath is not [_, ':', ..])
            return fullPath;
        var share = new char[1024];
        int length = share.Length;
        int result = WNetGetConnectionW(fullPath[..2], share, ref length);
        if (result is not (NoError or ConnectionUnavailable))
            return fullPath;
        return new string(share, 0, Array.IndexOf(share, '\0')) + fullPath[2..];
    }

    private const int NoError = 0;
    private const int ConnectionUnavailable = 1201;

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetGetConnectionW(string localName, char[] remoteName, ref int length);

    private static string FolderBelow(string targetFolder, string path)
    {
        var relative = Path.GetRelativePath(targetFolder, Path.GetDirectoryName(path)!);
        return relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? ""
            : relative;
    }

    public static FileActionResult DeletePermanently(IEnumerable<PhotoFile> photos, Action<RunFile>? onPhotoDone = null) =>
        ApplyToEachCollectingFailures(photos, path => { File.Delete(path); return null; }, onPhotoDone);

    // ownerWindow is the window Windows' own "delete permanently?" prompt belongs
    // to, so it opens over the app instead of behind it.
    [SupportedOSPlatform("windows")]
    public static FileActionResult MoveToRecycleBin(
        IEnumerable<PhotoFile> photos, Action<RunFile>? onPhotoDone = null, IntPtr ownerWindow = default) =>
        MoveToRecycleBin(photos, onPhotoDone, path => ShellDelete(path, ownerWindow));

    // Tests answer for the shell: a real decline or failure can't be set up.
    [SupportedOSPlatform("windows")]
    internal static FileActionResult MoveToRecycleBin(
        IEnumerable<PhotoFile> photos, Action<RunFile>? onPhotoDone, Func<string, ShellDeleteOutcome> shellDelete) =>
        ApplyToEachCollectingFailures(photos, path => { SendToRecycleBin(path, shellDelete); return null; }, onPhotoDone);

    private static FileActionResult FailEvery(IEnumerable<PhotoFile> photos, string reason, Action<RunFile>? onPhotoDone)
    {
        var failures = photos.Select(p => new FileFailure(p, reason)).ToList();
        foreach (var failure in failures)
            onPhotoDone?.Invoke(new RunFile(failure.Photo.Path, null, failure.Photo.LivePhotoVideoPath, null, false, reason, 0));
        return new FileActionResult(0, failures);
    }

    // Short reasons the user can act on, as shown in the Result screen.
    public static string DescribeFailure(Exception exception) => exception switch
    {
        FileNotFoundException or DirectoryNotFoundException => "Missing: moved or deleted since the scan",
        UnauthorizedAccessException => "Access denied",
        IOException { HResult: SharingViolation or LockViolation } => "Locked: another app or OneDrive sync is using it",
        // PhotoRecull's own name checks say where the name is taken.
        IOException { HResult: FileExists } io when io.Message.StartsWith("A file with this name", StringComparison.Ordinal) => io.Message,
        IOException { HResult: FileExists or AlreadyExists } => "A file with this name is already in the stage folder",
        _ => exception.Message,
    };

    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);
    private const int FileExists = unchecked((int)0x80070050);
    private const int AlreadyExists = unchecked((int)0x800700B7);

    // A Live Photo's video goes first: if the photo then fails, a retry finds the
    // video already gone and acts on the photo alone. The bytes count every file
    // acted on, so a video deleted before its photo failed still counts as freed.
    // actOnFile returns where the file went (a stage path), or null once deleted.
    // undoFile, when given, puts back each file of a photo that then failed (from
    // where it went to where it was), so the photo's parts stay together.
    private static FileActionResult ApplyToEachCollectingFailures(
        IEnumerable<PhotoFile> photos, Func<string, string?> actOnFile, Action<RunFile>? onPhotoDone,
        Action<PhotoFile>? checkBeforeActing = null, Action<string, string>? undoFile = null)
    {
        int succeededCount = 0;
        long succeededBytes = 0;
        var failures = new List<FileFailure>();
        foreach (var photo in photos)
        {
            long photoBytes = 0;
            var wentTo = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            string? failureReason = null;
            try
            {
                if (!File.Exists(photo.Path))
                    throw new FileNotFoundException(null, photo.Path);
                checkBeforeActing?.Invoke(photo);
                foreach (var path in FilesOf(photo).ToList())
                {
                    long size = new FileInfo(path).Length;
                    wentTo[path] = actOnFile(path);
                    photoBytes += size;
                }
                succeededCount++;
            }
            // A path the file system rejects (ArgumentException, NotSupportedException)
            // fails that photo, too, rather than ending the run after earlier files
            // moved, with nothing recorded for them.
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                failureReason = DescribeFailure(ex);
                failures.Add(new FileFailure(photo, failureReason));
                if (undoFile is not null)
                    photoBytes -= UndoWhatMoved(wentTo, undoFile);
            }
            succeededBytes += photoBytes;
            onPhotoDone?.Invoke(new RunFile(
                photo.Path, wentTo.GetValueOrDefault(photo.Path),
                photo.LivePhotoVideoPath, photo.LivePhotoVideoPath is { } video ? wentTo.GetValueOrDefault(video) : null,
                failureReason is null, failureReason, photoBytes));
        }
        return new FileActionResult(succeededCount, failures, succeededBytes);
    }

    // Returns the bytes of the files put back. A file that can't be put back stays
    // where it went, and is still recorded there.
    private static long UndoWhatMoved(Dictionary<string, string?> wentTo, Action<string, string> undoFile)
    {
        long undoneBytes = 0;
        foreach (var (from, to) in wentTo.ToList())
        {
            if (to is null)
                continue;
            try
            {
                long size = new FileInfo(to).Length;
                undoFile(from, to);
                wentTo.Remove(from);
                undoneBytes += size;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        return undoneBytes;
    }

    // A Live Photo's video that's gone is skipped (moved or deleted on its own).
    // One that can't be checked (no access, a NAS that dropped out) fails the
    // photo: acting on the photo alone would split the pair.
    private static IEnumerable<string> FilesOf(PhotoFile photo)
    {
        if (photo.LivePhotoVideoPath is { } video && IsThere(video))
            yield return video;
        yield return photo.Path;
    }

    private static bool IsThere(string path)
    {
        try
        {
            File.GetAttributes(path);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Can't reach its Live Photo video: {ex.Message}", ex);
        }
    }

    // Staged photos go back to where they were on their target. A file already
    // there under that name is never overwritten: the photo stays staged.
    public static FileActionResult MoveBack(IEnumerable<StagedPhoto> photos, Action<RunFile>? onPhotoDone = null)
    {
        var originalOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var stagedFiles = photos.Select(p =>
        {
            originalOf[p.StagedPath] = p.OriginalPath;
            if (p.StagedVideoPath is { } video && p.OriginalVideoPath is { } originalVideo)
                originalOf[video] = originalVideo;
            return new PhotoFile(p.StagedPath, p.FileName, null, IsIphone: true, TimestampSource: null)
            {
                LivePhotoVideoPath = p.StagedVideoPath,
            };
        }).ToList();
        return ApplyToEachCollectingFailures(stagedFiles,
            path =>
            {
                var original = originalOf[path];
                Directory.CreateDirectory(Path.GetDirectoryName(original)!);
                File.Move(path, original, overwrite: false);
                return original;
            },
            onPhotoDone,
            checkBeforeActing: photo =>
            {
                if (FilesOf(photo).Any(path => File.Exists(originalOf[path])))
                    throw new IOException("A file with this name is already back in its place", FileExists);
            },
            // A video that went back before its photo failed returns to the stage
            // folder: back on the target alone, nothing would keep it.
            undoFile: (staged, original) => File.Move(original, staged, overwrite: false));
    }

    // Network and mapped drives have no Recycle Bin: the shell would delete from
    // them permanently, which the app promises not to do.
    [SupportedOSPlatform("windows")]
    public static bool HasRecycleBin(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (new Uri(fullPath).IsUnc || Path.GetPathRoot(fullPath) is not { Length: > 0 } root)
            return false;
        return new DriveInfo(root).DriveType is DriveType.Fixed or DriveType.Removable;
    }

    // The shell's delete (SHFileOperation with "allow undo") is what puts a file in
    // the Recycle Bin; .NET's File.Delete always deletes permanently. "Allow undo"
    // alone still deletes permanently when recycling isn't possible (e.g. a file
    // larger than the Recycle Bin), so the shell is also asked to warn first; if
    // the user declines, the file is left alone and reported as not deleted.
    [SupportedOSPlatform("windows")]
    private static void SendToRecycleBin(string path, Func<string, ShellDeleteOutcome> shellDelete)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(null, path);
        if (!HasRecycleBin(path))
            throw new IOException("Not deleted: this drive has no Recycle Bin");

        var (errorCode, aborted) = shellDelete(Path.GetFullPath(path));
        if (aborted)
            throw new IOException("Not deleted: it couldn't go to the Recycle Bin");
        if (errorCode != 0)
            throw new IOException($"Couldn't move to the Recycle Bin: {new Win32Exception(errorCode).Message}", errorCode);
    }

    private static ShellDeleteOutcome ShellDelete(string fullPath, IntPtr ownerWindow)
    {
        var operation = new ShellFileOperation
        {
            Window = ownerWindow,
            Function = FileOperationDelete,
            From = fullPath + "\0\0",
            Flags = AllowUndo | NoConfirmation | WantNukeWarning | NoErrorUi | Silent,
        };
        int errorCode = SHFileOperationW(ref operation);
        return new ShellDeleteOutcome(errorCode, operation.AnyOperationsAborted);
    }

    private const uint FileOperationDelete = 0x0003;
    private const ushort Silent = 0x0004;
    private const ushort NoConfirmation = 0x0010;
    private const ushort AllowUndo = 0x0040;
    private const ushort NoErrorUi = 0x0400;
    private const ushort WantNukeWarning = 0x4000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileOperation
    {
        public IntPtr Window;
        public uint Function;
        public string From;
        public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool AnyOperationsAborted;
        public IntPtr NameMappings;
        public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref ShellFileOperation operation);
}
