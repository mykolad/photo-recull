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
    public void SuggestedName_IsNeverOneAnotherTargetHas()
    {
        Assert.Equal("iPhone 3", SyncTargets.SuggestedName(@"D:\Backups\iPhone", ["iphone", "iPhone 2"]));
    }
}
