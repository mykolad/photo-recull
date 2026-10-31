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
    // throwing, so the result can still be shown.
    public static FileActionResult MoveToStageFolder(IEnumerable<PhotoFile> photos, string stageFolder)
    {
        try
        {
            Directory.CreateDirectory(stageFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            var reason = $"Can't use the stage folder: {ex.Message}";
            return new FileActionResult(0, photos.Select(p => new FileFailure(p, reason)).ToList());
        }
        return ApplyToEachCollectingFailures(photos,
            p => ApplyToPhotoAndItsVideo(p, path => File.Move(path, Path.Combine(stageFolder, Path.GetFileName(path)), overwrite: false)));
    }

    public static FileActionResult DeletePermanently(IEnumerable<PhotoFile> photos) =>
        ApplyToEachCollectingFailures(photos, p => ApplyToPhotoAndItsVideo(p, File.Delete));

    [SupportedOSPlatform("windows")]
    public static FileActionResult MoveToRecycleBin(IEnumerable<PhotoFile> photos) =>
        ApplyToEachCollectingFailures(photos, p => ApplyToPhotoAndItsVideo(p, SendToRecycleBin));

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

    private static FileActionResult ApplyToEachCollectingFailures(
        IEnumerable<PhotoFile> photos, Action<PhotoFile> action)
    {
        int succeededCount = 0;
        long succeededBytes = 0;
        var failures = new List<FileFailure>();
        foreach (var photo in photos)
        {
            try
            {
                long size = CurrentSizeOfPhotoAndItsVideo(photo);
                action(photo);
                succeededCount++;
                succeededBytes += size;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(new FileFailure(photo, DescribeFailure(ex)));
            }
        }
        return new FileActionResult(succeededCount, failures, succeededBytes);
    }

    // A Live Photo's video goes first: if the photo then fails, a retry finds the
    // video already gone and acts on the photo alone.
    private static void ApplyToPhotoAndItsVideo(PhotoFile photo, Action<string> action)
    {
        if (photo.LivePhotoVideoPath is { } video && File.Exists(video))
            action(video);
        action(photo.Path);
    }

    private static long CurrentSizeOfPhotoAndItsVideo(PhotoFile photo) =>
        new FileInfo(photo.Path).Length
        + (photo.LivePhotoVideoPath is { } video && File.Exists(video) ? new FileInfo(video).Length : 0);

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
