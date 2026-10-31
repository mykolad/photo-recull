using PhotoRecull.Core;

namespace PhotoRecull.Tests;

public class SynologyPhotosTargetTests : IDisposable
{
    private readonly string _backupFolder = Directory.CreateTempSubdirectory("photorecull-synology-").FullName;

    public void Dispose() => Directory.Delete(_backupFolder, recursive: true);

    private string CreateFile(string relativePath)
    {
        var path = Path.Combine(_backupFolder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, relativePath);
        return path;
    }

    private List<string> ListedRelativePaths(DateOnly? firstDay, DateOnly? lastDay) =>
        SynologyPhotosTarget.ListPhotoFiles(_backupFolder, firstDay, lastDay)
            .Select(p => Path.GetRelativePath(_backupFolder, p))
            .ToList();

    [Fact]
    public void ListPhotoFiles_GoesThroughEveryYearAndMonthFolder_SkippingSynologysOwnFolders()
    {
        CreateFile(@"2025\12\IMG_0001.HEIC");
        CreateFile(@"2026\01\IMG_0002.JPEG");
        CreateFile(@"2026\01\IMG_0003.mov");
        CreateFile(@"2026\01\Thumbs.db");
        CreateFile(@"2026\01\@eaDir\IMG_0002.JPEG\SYNOPHOTO_THUMB_XL.jpg");
        CreateFile(@"#recycle\IMG_0004.HEIC");

        Assert.Equal([@"2025\12\IMG_0001.HEIC", @"2026\01\IMG_0002.JPEG", @"2026\01\IMG_0003.mov"],
            ListedRelativePaths(firstDay: null, lastDay: null));
    }

    [Fact]
    public void ListPhotoFiles_SkipsDateFoldersAtLeastADayOutsideTheRange()
    {
        CreateFile(@"2026\01\IMG_0001.HEIC");
        CreateFile(@"2026\02\IMG_0002.HEIC");
        CreateFile(@"2026\02\14\IMG_0003.HEIC");
        CreateFile(@"2026\02\20\IMG_0004.HEIC");
        CreateFile(@"2026\03\IMG_0005.HEIC");
        CreateFile(@"2026-02-15\IMG_0006.HEIC");
        CreateFile(@"2026-03\IMG_0007.HEIC");
        CreateFile(@"Shared album\IMG_0008.HEIC");

        Assert.Equal(
            [@"2026-02-15\IMG_0006.HEIC", @"2026\02\14\IMG_0003.HEIC", @"2026\02\IMG_0002.HEIC", @"Shared album\IMG_0008.HEIC"],
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

        var photos = new SynologyPhotosTarget(_backupFolder).Scan(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));

        var photo = Assert.Single(photos);
        Assert.Equal("IMG_5587.HEIC", photo.FileName);
        Assert.True(photo.IsIphone);
        Assert.Equal(new DateTimeOffset(2026, 4, 11, 7, 33, 33, 208, TimeSpan.Zero), photo.Timestamp);
    }

    [Fact]
    public void Scan_AFolderThatCantBeReached_SaysToCheckTheNas()
    {
        var target = new SynologyPhotosTarget(Path.Combine(_backupFolder, "missing"));

        var error = Assert.Throws<DirectoryNotFoundException>(() => target.Scan(null, null));
        Assert.Contains("Check that the NAS is on", error.Message);
    }

    [Theory]
    [InlineData(@"\\nas\home\Photos\MobileBackup\iPhone", @"\\nas\home\#recycle")]
    [InlineData(@"Z:\Photos\MobileBackup\iPhone", @"Z:\#recycle")]
    public void RecycleBinFolder_IsDsmsRecycleBinAtTheRootOfTheSharedFolder(string backupFolder, string expected)
    {
        Assert.Equal(expected, new SynologyPhotosTarget(backupFolder).RecycleBinFolder);
    }

    [Theory]
    [InlineData(@"\\nas\home\Photos\MobileBackup\iPhone", @"\\nas\home\PhotoRecull staged\2026-01-01_2026-01-31")]
    [InlineData(@"Z:\Photos\MobileBackup\iPhone\", @"Z:\PhotoRecull staged\2026-01-01_2026-01-31")]
    public void DefaultStageFolder_IsOutsideThePhotosFolderThatSynologyPhotosShows(string backupFolder, string expected)
    {
        var target = new SynologyPhotosTarget(backupFolder);

        Assert.Equal(expected, target.DefaultStageFolder(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));
    }

    [Theory]
    [InlineData(@"\\nas\photo\MobileBackup\iPhone")]
    [InlineData(@"Y:\MobileBackup\iPhone")]
    public void DefaultStageFolder_IsLeftToTheUser_WhenNoFolderOutsideSynologyPhotosCanBeTold(string backupFolder)
    {
        Assert.Null(new SynologyPhotosTarget(backupFolder).DefaultStageFolder(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));
    }
}
