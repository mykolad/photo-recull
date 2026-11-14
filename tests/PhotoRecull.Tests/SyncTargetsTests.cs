using PhotoRecull.Core;
using PhotoRecull.Core.Settings;

namespace PhotoRecull.Tests;

public class SyncTargetsTests
{
    [Fact]
    public void Create_IsAFolderTargetWithTheConfiguredNameAndFolder()
    {
        var target = SyncTargets.Create(new TargetSettings("Synology Photos", @"\\nas\home\Photos\MobileBackup\iPhone"));

        var folder = Assert.IsType<FolderTarget>(target);
        Assert.Equal("Synology Photos", folder.DisplayName);
        Assert.Equal(@"\\nas\home\Photos\MobileBackup\iPhone", folder.FolderPath);
    }

    [Theory]
    [InlineData(@"\\nas\home\Photos\MobileBackup\iPhone", "Synology Photos")]
    [InlineData(@"\\nas\photo\Phones", "nas")]
    [InlineData(@"D:\Backups\iPhone\", "iPhone")]
    [InlineData(@"E:\", "Drive E")]
    [InlineData("", "Folder")]
    public void SuggestedName_IsTheServiceTheNasOrTheFolder(string folder, string expected)
    {
        Assert.Equal(expected, SyncTargets.SuggestedName(folder, []));
    }

    [Fact]
    public void DefaultStageFolders_OfTargetsSideBySide_AreEachInAFolderOfTheirOwn()
    {
        var phone1 = new TargetSettings("Phone 1", @"D:\Backups\Phone1");
        var phone2 = new TargetSettings("Phone: 2", @"D:\Backups\Phone2");
        var sameName = new TargetSettings("Phone 1", @"D:\Backups\Old");
        var elsewhere = new TargetSettings("OneDrive", @"C:\Users\me\OneDrive\Pictures\Camera Roll");

        var folders = SyncTargets.DefaultStageFolders([phone1, phone2, sameName, elsewhere], new(2026, 1, 1), new(2026, 1, 31));

        Assert.Equal(@"D:\Backups\_staged_2026-01-01_2026-01-31\Phone 1", folders[phone1]);
        Assert.Equal(@"D:\Backups\_staged_2026-01-01_2026-01-31\Phone_ 2", folders[phone2]);
        Assert.Equal(@"D:\Backups\_staged_2026-01-01_2026-01-31\Phone 1 2", folders[sameName]);
        Assert.Equal(@"C:\Users\me\OneDrive\Pictures\_staged_2026-01-01_2026-01-31", folders[elsewhere]);
    }

    [Fact]
    public void SuggestedName_IsNeverOneAnotherTargetHas()
    {
        Assert.Equal("iPhone 3", SyncTargets.SuggestedName(@"D:\Backups\iPhone", ["iphone", "iPhone 2"]));
    }
}
