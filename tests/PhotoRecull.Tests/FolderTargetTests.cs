using PhotoRecull.Core;

namespace PhotoRecull.Tests;

public class FolderTargetTests : IDisposable
{
    private readonly string _backupFolder = Directory.CreateTempSubdirectory("photorecull-folder-").FullName;

    public void Dispose() => Directory.Delete(_backupFolder, recursive: true);

    private string CreateFile(string relativePath)
    {
        var path = Path.Combine(_backupFolder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, relativePath);
        return path;
    }

    private List<string> ListedRelativePaths(DateOnly? firstDay, DateOnly? lastDay) =>
        FolderTarget.ListPhotoFiles(_backupFolder, firstDay, lastDay)
            .Select(p => Path.GetRelativePath(_backupFolder, p))
            .ToList();

    [Fact]
    public void ListPhotoFiles_GoesThroughEveryYearAndMonthFolder_SkippingSynologysAndPhotoRecullsOwnFolders()
    {
        CreateFile(@"2025\12\IMG_0001.HEIC");
        CreateFile(@"2026\01\IMG_0002.JPEG");
        CreateFile(@"2026\01\IMG_0003.mov");
        CreateFile(@"2026\01\Thumbs.db");
        CreateFile(@"2026\01\@eaDir\IMG_0002.JPEG\SYNOPHOTO_THUMB_XL.jpg");
        CreateFile(@"#recycle\IMG_0004.HEIC");
        CreateFile(@"PhotoRecull staged\2026-01-01_2026-01-31\2026\01\IMG_0005.HEIC");
        CreateFile(@"_staged_2026-01\IMG_0006.HEIC");

        Assert.Equal([@"2025\12\IMG_0001.HEIC", @"2026\01\IMG_0002.JPEG", @"2026\01\IMG_0003.mov"],
            ListedRelativePaths(firstDay: null, lastDay: null));
    }

    [Fact]
    public void ListPhotoFiles_SkipsDateFoldersMoreThanTwoDaysOutsideTheRange()
    {
        CreateFile(@"2026\01\IMG_0001.HEIC");
        CreateFile(@"2026\02\IMG_0002.HEIC");
        CreateFile(@"2026\02\14\IMG_0003.HEIC");
        CreateFile(@"2026\02\20\IMG_0004.HEIC");
        CreateFile(@"2026\03\IMG_0005.HEIC");
        CreateFile(@"2026-02-15\IMG_0006.HEIC");
        CreateFile(@"2026-03\IMG_0007.HEIC");
        CreateFile(@"Shared album\IMG_0008.HEIC");
        CreateFile(@"2026\02\11\IMG_0009.HEIC");
        CreateFile(@"2026\02\10\IMG_0010.HEIC");

        Assert.Equal(
            [@"2026-02-15\IMG_0006.HEIC", @"2026\02\11\IMG_0009.HEIC", @"2026\02\14\IMG_0003.HEIC", @"2026\02\IMG_0002.HEIC", @"Shared album\IMG_0008.HEIC"],
            ListedRelativePaths(new DateOnly(2026, 2, 13), new DateOnly(2026, 2, 16)));
    }

    [Fact]
    public void ListPhotoFiles_KeepsTheNeighbouringMonth_WhenTheRangeStartsOnItsFirstDay()
    {
        // Taken in the evening of 31 Jan where it was shot, which is already
        // 1 Feb in the user's time zone.
        CreateFile(@"2026\01\IMG_0001.HEIC");
        CreateFile(@"2025\12\IMG_0002.HEIC");

        Assert.Equal([@"2026\01\IMG_0001.HEIC"], ListedRelativePaths(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)));
    }

    [Fact]
    public void ListPhotoFiles_ReadsYearFoldersBelowAFolderPickedHigherUp()
    {
        CreateFile(@"iPhone\2026\01\IMG_0001.HEIC");
        CreateFile(@"iPhone\2025\01\IMG_0002.HEIC");

        Assert.Equal([@"iPhone\2026\01\IMG_0001.HEIC"], ListedRelativePaths(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));
    }

    [Fact]
    public void Scan_ReadsTheBackedUpPhotosOfTheRange()
    {
        var testData = Path.Combine(AppContext.BaseDirectory, "TestData", "IMG_5587.HEIC");
        File.Copy(testData, CreateFile(@"2026\04\IMG_5587.HEIC"), overwrite: true);
        CreateFile(@"2026\06\IMG_5600.HEIC");

        var photos = new FolderTarget("Synology Photos", _backupFolder).Scan(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));

        var photo = Assert.Single(photos);
        Assert.Equal("IMG_5587.HEIC", photo.FileName);
        Assert.True(photo.IsIphone);
        Assert.Equal(new DateTimeOffset(2026, 4, 11, 7, 33, 33, 208, TimeSpan.Zero), photo.Timestamp);
    }

    [Fact]
    public void Scan_ReadsPhotosInEveryFolderBelow_LikeOneDrivesYearAndMonthFolders()
    {
        var testData = Path.Combine(AppContext.BaseDirectory, "TestData", "IMG_5587.HEIC");
        File.Copy(testData, CreateFile(@"2026\04\IMG_5587.HEIC"), overwrite: true);
        File.Copy(testData, CreateFile(@"Trip\IMG_5588.HEIC"), overwrite: true);

        var photos = new FolderTarget("OneDrive", _backupFolder).Scan(firstDay: null, lastDay: null);

        Assert.Equal(["IMG_5587.HEIC", "IMG_5588.HEIC"], photos.Select(p => p.FileName).Order());
    }

    [Fact]
    public void ListPhotoFiles_SkipsAFolderThatIsALinkToAnother()
    {
        CreateFile(@"Photos\IMG_0001.HEIC");
        var alias = Path.Combine(_backupFolder, "Alias");
        // A junction, which unlike a symbolic link needs no administrator rights.
        using (var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "cmd.exe", $"/c mklink /J \"{alias}\" \"{Path.Combine(_backupFolder, "Photos")}\"")
            { CreateNoWindow = true, UseShellExecute = false })!)
            mklink.WaitForExit();
        try
        {
            Assert.True(Directory.Exists(alias));

            Assert.Equal([@"Photos\IMG_0001.HEIC"], ListedRelativePaths(firstDay: null, lastDay: null));
        }
        finally
        {
            // Removes the junction itself, not the folder it points to.
            Directory.Delete(alias);
        }
    }

    [Fact]
    public void Scan_ALocalFolderThatIsMissing_SaysToCheckItsDrive()
    {
        var target = new FolderTarget("Camera Roll", Path.Combine(_backupFolder, "missing"));

        var error = Assert.Throws<DirectoryNotFoundException>(() => target.Scan(null, null));
        Assert.Contains("that its drive is connected", error.Message);
    }

    [Fact]
    public void NasRecycleBinFolder_IsNeverLookedForOnALocalFolder()
    {
        Directory.CreateDirectory(Path.Combine(_backupFolder, "#recycle"));

        Assert.Null(new FolderTarget("Camera Roll", _backupFolder).NasRecycleBinFolder());
    }

    [Theory]
    [InlineData(@"\\nas\home\Photos\MobileBackup\iPhone", @"\\nas\home\PhotoRecull staged\2026-01-01_2026-01-31")]
    [InlineData(@"Z:\Photos\MobileBackup\iPhone\", @"Z:\PhotoRecull staged\2026-01-01_2026-01-31")]
    public void DefaultStageFolder_IsOutsideThePhotosFolderThatSynologyPhotosShows(string backupFolder, string expected)
    {
        var target = new FolderTarget("Synology Photos", backupFolder);

        Assert.Equal(expected, target.DefaultStageFolder(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));
    }

    [Theory]
    [InlineData(@"\\nas\photo\MobileBackup\iPhone")]
    [InlineData(@"Y:\MobileBackup\iPhone")]
    public void DefaultStageFolder_IsLeftToTheUser_WhenNoFolderOutsideSynologyPhotosCanBeTold(string backupFolder)
    {
        Assert.Null(new FolderTarget("Synology Photos", backupFolder).DefaultStageFolder(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));
    }

    [Theory]
    [InlineData(@"C:\Users\me\OneDrive\Pictures\Camera Roll", @"C:\Users\me\OneDrive\Pictures\_staged_2026-01-01_2026-01-31")]
    [InlineData(@"D:\Backups\iPhone\", @"D:\Backups\_staged_2026-01-01_2026-01-31")]
    public void DefaultStageFolder_OfAnyOtherFolder_IsNextToIt(string folder, string expected)
    {
        var target = new FolderTarget("Folder", folder);

        Assert.Equal(expected, target.DefaultStageFolder(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));
    }

    [Fact]
    public void DefaultStageFolder_OfAWholeDrive_IsLeftToTheUser()
    {
        Assert.Null(new FolderTarget("Drive E", @"E:\").DefaultStageFolder(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));
    }
}
