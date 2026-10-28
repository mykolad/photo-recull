using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PhotoRecull.Core;

public record FileFailure(PhotoFile Photo, string Reason);

public record FileActionResult(int SucceededCount, List<FileFailure> Failures, long SucceededBytes = 0);

public static class FileActions
{
    public static FileActionResult MoveToStageFolder(IEnumerable<PhotoFile> photos, string stageFolder)
    {
        Directory.CreateDirectory(stageFolder);
        return ApplyToEachCollectingFailures(photos,
            p => File.Move(p.Path, Path.Combine(stageFolder, p.FileName), overwrite: false));
    }

    public static FileActionResult DeletePermanently(IEnumerable<PhotoFile> photos) =>
        ApplyToEachCollectingFailures(photos, p => File.Delete(p.Path));

    [SupportedOSPlatform("windows")]
    public static FileActionResult MoveToRecycleBin(IEnumerable<PhotoFile> photos) =>
        ApplyToEachCollectingFailures(photos, p => SendToRecycleBin(p.Path));

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
                long size = new FileInfo(photo.Path).Length;
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

    // The shell's delete (SHFileOperation with "allow undo") is what puts a file in
    // the Recycle Bin; .NET's File.Delete always deletes permanently.
    [SupportedOSPlatform("windows")]
    private static void SendToRecycleBin(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(null, path);

        var operation = new ShellFileOperation
        {
            Function = FileOperationDelete,
            From = Path.GetFullPath(path) + "\0\0",
            Flags = AllowUndo | NoConfirmation | NoErrorUi | Silent,
        };
        int result = SHFileOperationW(ref operation);
        if (result != 0 || operation.AnyOperationsAborted)
            throw new IOException($"Couldn't move to the Recycle Bin: {new Win32Exception(result).Message}", result);
    }

    private const uint FileOperationDelete = 0x0003;
    private const ushort Silent = 0x0004;
    private const ushort NoConfirmation = 0x0010;
    private const ushort AllowUndo = 0x0040;
    private const ushort NoErrorUi = 0x0400;

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
