using PhotoRecull.Core;
using PhotoRecull.Core.Settings;

namespace PhotoRecull.Tests;

public class SyncTargetsTests
{
    [Fact]
    public void Create_OneDrive_UsesTheConfiguredFolder()
    {
        var target = SyncTargets.Create(new TargetSettings(TargetKind.OneDrive, @"D:\Camera Roll"));

        var oneDrive = Assert.IsType<OneDriveTarget>(target);
        Assert.Equal(@"D:\Camera Roll", oneDrive.FolderPath);
    }

    [Fact]
    public void Create_SynologyPhotos_UsesTheConfiguredFolder()
    {
        var target = SyncTargets.Create(new TargetSettings(TargetKind.SynologyPhotos, @"\\nas\home\Photos\MobileBackup\iPhone"));

        var synology = Assert.IsType<SynologyPhotosTarget>(target);
        Assert.Equal(@"\\nas\home\Photos\MobileBackup\iPhone", synology.FolderPath);
    }
}
