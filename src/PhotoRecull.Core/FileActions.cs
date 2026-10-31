using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PhotoRecull.Core;

public record FileFailure(PhotoFile Photo, string Reason);

public record FileActionResult(int SucceededCount, List<FileFailure> Failures, long SucceededBytes = 0);

public static class FileActions
{
    // A stage folder that can't be created (the path names a file, a drive that
    // isn't there, no permission) fails every photo for that target instead of
    // throwing, so the result can still be shown. Photos keep their folders below
    // the target's (2026\07\IMG_0001.HEIC), so two photos named alike in different
    // months don't collide, and each can be moved back where it came from.
    public static FileActionResult MoveToStageFolder(IEnumerable<PhotoFile> photos, string stageFolder, string targetFolder)
    {
        if (IsSameOrInside(stageFolder, targetFolder))
            return new FileActionResult(0, photos.Select(p => new FileFailure(p, StageFolderInsideTarget)).ToList());
        try
        {
            Directory.CreateDirectory(stageFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            var reason = $"Can't use the stage folder: {ex.Message}";
            return new FileActionResult(0, photos.Select(p => new FileFailure(p, reason)).ToList());
        }
        string StagedPath(string path) => Path.Combine(stageFolder, FolderBelow(targetFolder, path), Path.GetFileName(path));
        return ApplyToEachCollectingFailures(photos,
            path =>
            {
                var stagedPath = StagedPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
                File.Move(path, stagedPath, overwrite: false);
            },
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
        var fullPath = Path.GetFullPath(folder);
        return Path.EndsInDirectorySeparator(fullPath) ? fullPath : fullPath + Path.DirectorySeparatorChar;
    }

    private static string FolderBelow(string targetFolder, string path)
    {
        var relative = Path.GetRelativePath(targetFolder, Path.GetDirectoryName(path)!);
        return relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? ""
            : relative;
    }

    public static FileActionResult DeletePermanently(IEnumerable<PhotoFile> photos) =>
        ApplyToEachCollectingFailures(photos, File.Delete);

    [SupportedOSPlatform("windows")]
    public static FileActionResult MoveToRecycleBin(IEnumerable<PhotoFile> photos) =>
        ApplyToEachCollectingFailures(photos, SendToRecycleBin);

    // Short reasons the user can act on, as shown in the Result screen.
    public static string DescribeFailure(Exception exception) => exception switch
    {
        FileNotFoundException or DirectoryNotFoundException => "Missing: moved or deleted since the scan",
        UnauthorizedAccessException => "Access denied",
        IOException { HResult: SharingViolation or LockViolation } => "Locked: another app or OneDrive sync is using it",
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
    private static FileActionResult ApplyToEachCollectingFailures(
        IEnumerable<PhotoFile> photos, Action<string> actOnFile, Action<PhotoFile>? checkBeforeActing = null)
    {
        int succeededCount = 0;
        long succeededBytes = 0;
        var failures = new List<FileFailure>();
        foreach (var photo in photos)
        {
            try
            {
                if (!File.Exists(photo.Path))
                    throw new FileNotFoundException(null, photo.Path);
                checkBeforeActing?.Invoke(photo);
                foreach (var path in FilesOf(photo).ToList())
                {
                    long size = new FileInfo(path).Length;
                    actOnFile(path);
                    succeededBytes += size;
                }
                succeededCount++;
            }
            // A path the file system rejects (ArgumentException, NotSupportedException)
            // fails that photo, too, rather than ending the run after earlier files
            // moved, with nothing recorded for them.
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                failures.Add(new FileFailure(photo, DescribeFailure(ex)));
            }
        }
        return new FileActionResult(succeededCount, failures, succeededBytes);
    }

    private static IEnumerable<string> FilesOf(PhotoFile photo)
    {
        if (photo.LivePhotoVideoPath is { } video && File.Exists(video))
            yield return video;
        yield return photo.Path;
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
    private static void SendToRecycleBin(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(null, path);
        if (!HasRecycleBin(path))
            throw new IOException("Not deleted: this drive has no Recycle Bin");

        var operation = new ShellFileOperation
        {
            Function = FileOperationDelete,
            From = Path.GetFullPath(path) + "\0\0",
            Flags = AllowUndo | NoConfirmation | WantNukeWarning | NoErrorUi | Silent,
        };
        int result = SHFileOperationW(ref operation);
        if (operation.AnyOperationsAborted)
            throw new IOException("Not deleted: it couldn't go to the Recycle Bin");
        if (result != 0)
            throw new IOException($"Couldn't move to the Recycle Bin: {new Win32Exception(result).Message}", result);
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
