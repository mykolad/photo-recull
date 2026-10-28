using Windows.Storage.Pickers;

namespace PhotoRecull.App.Services;

internal static class Pickers
{
    public static async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeFilter.Add("*");
        AttachToMainWindow(picker);
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    public static async Task<string?> PickFileAsync(string extension)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add(extension);
        AttachToMainWindow(picker);
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public static async Task<string?> PickSaveFileAsync(string suggestedName, string extension, string fileTypeLabel)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedName,
        };
        picker.FileTypeChoices.Add(fileTypeLabel, [extension]);
        AttachToMainWindow(picker);
        return (await picker.PickSaveFileAsync())?.Path;
    }

    // An unpackaged desktop app has no CoreWindow, so WinRT pickers must be told
    // which window owns them or they fail to open.
    private static void AttachToMainWindow(object picker) =>
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
}
