using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoRecull.App.Controls;
using PhotoRecull.App.Services;
using PhotoRecull.App.ViewModels;
using PhotoRecull.Core;
using PhotoRecull.Core.Settings;

namespace PhotoRecull.App.Views;

internal static class ConfirmDialogContent
{
    public static ContentDialog Create(XamlRoot xamlRoot, string title)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };
        // The default dialog is too narrow for a folder path per target.
        dialog.Resources["ContentDialogMaxWidth"] = 640.0;
        return dialog;
    }

    public static Grid TargetRow(TargetSettings target, int count, long bytes, UIElement? trailing)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new FontIcon { Glyph = ShellPage.TargetGlyph(target.Kind), FontSize = 20 });
        var name = new StackPanel();
        name.Children.Add(new TextBlock { Text = SyncTargets.DisplayName(target.Kind) });
        name.Children.Add(new TextBlock
        {
            Text = $"{count:N0} file{(count == 1 ? "" : "s")} · {FileSizeText.Format(bytes)}",
            Style = (Style)Application.Current.Resources["SecondaryCaptionTextBlockStyle"],
        });
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        if (trailing is FrameworkElement element)
        {
            element.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(element, 2);
            row.Children.Add(element);
        }
        return row;
    }
}

internal sealed class StageDialog
{
    private readonly ContentDialog _dialog;
    private readonly Dictionary<TargetSettings, TextBox> _folderBoxes = [];
    private readonly Dictionary<TargetSettings, TextBlock> _folderProblems = [];

    public StageDialog(XamlRoot xamlRoot, CleanupFlowViewModel flow)
    {
        var perTarget = flow.CandidatesPerTarget();
        int total = perTarget.Sum(t => t.Count);
        _dialog = ConfirmDialogContent.Create(xamlRoot, $"Stage {total:N0} photo{(total == 1 ? "" : "s")}?");
        _dialog.PrimaryButtonText = $"Stage {total:N0} file{(total == 1 ? "" : "s")}";
        _dialog.CloseButtonText = "Cancel";
        _dialog.DefaultButton = ContentDialogButton.Primary;

        var content = new StackPanel { Spacing = 16, Width = 560 };
        content.Children.Add(new TextBlock
        {
            Text = "Candidates move to a review folder outside each target's photos, so they aren't scanned or shown again. Nothing is deleted. Check the folder, move back anything you need, then delete it.",
            TextWrapping = TextWrapping.Wrap,
        });
        foreach (var (target, count, bytes) in perTarget)
        {
            var folder = new TextBox
            {
                Text = flow.DefaultStageFolderFor(target) ?? "",
                Header = "Stage folder",
                PlaceholderText = $"A folder outside {SyncTargets.DisplayName(target.Kind)}, so staged photos don't show up there again",
            };
            folder.TextChanged += (_, _) => UpdatePrimaryButton();
            _folderProblems[target] = new TextBlock
            {
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = XamlFunctions.ThemeBrush("SystemFillColorCriticalBrush"),
                TextWrapping = TextWrapping.Wrap,
            };
            var browse = new Button { Content = "Browse…", VerticalAlignment = VerticalAlignment.Bottom };
            browse.Click += async (_, _) =>
            {
                if (await Pickers.PickFolderAsync() is { } picked)
                    folder.Text = picked;
            };
            _folderBoxes[target] = folder;

            var folderRow = new Grid { ColumnSpacing = 8 };
            folderRow.ColumnDefinitions.Add(new ColumnDefinition());
            folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            folderRow.Children.Add(folder);
            Grid.SetColumn(browse, 1);
            folderRow.Children.Add(browse);

            var block = new StackPanel { Spacing = 8 };
            block.Children.Add(ConfirmDialogContent.TargetRow(target, count, bytes, trailing: null));
            block.Children.Add(folderRow);
            block.Children.Add(_folderProblems[target]);
            content.Children.Add(block);
        }
        _dialog.Content = content;
        UpdatePrimaryButton();
    }

    public Dictionary<TargetSettings, string> StageFolders =>
        _folderBoxes.ToDictionary(pair => pair.Key, pair => pair.Value.Text.Trim());

    public async Task<bool> ConfirmAsync() => await _dialog.ShowAsync() == ContentDialogResult.Primary;

    private void UpdatePrimaryButton()
    {
        bool allUsable = true;
        foreach (var (target, box) in _folderBoxes)
        {
            var folder = box.Text.Trim();
            var problem = !Path.IsPathFullyQualified(folder) ? null
                : IsSameOrInside(folder, target.FolderPath) ? "This is inside the target's folder, so the next scan would find these photos again."
                : null;
            _folderProblems[target].Text = problem ?? "";
            _folderProblems[target].Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
            allUsable &= Path.IsPathFullyQualified(folder) && problem is null;
        }
        _dialog.IsPrimaryButtonEnabled = allUsable;
    }

    private static bool IsSameOrInside(string folder, string parent)
    {
        var folderPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        var parentPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) + Path.DirectorySeparatorChar;
        return folderPath.StartsWith(parentPath, StringComparison.OrdinalIgnoreCase);
    }
}

internal enum DeleteChoice
{
    Cancel,
    Delete,
    StageInstead,
}

internal sealed class DeleteDialog
{
    private readonly ContentDialog _dialog;
    private DeleteChoice _choice = DeleteChoice.Cancel;

    public DeleteDialog(XamlRoot xamlRoot, CleanupFlowViewModel flow, int keptByUserCount)
    {
        var perTarget = flow.CandidatesPerTarget();
        int total = perTarget.Sum(t => t.Count);
        _dialog = ConfirmDialogContent.Create(xamlRoot, $"Delete {total:N0} photo{(total == 1 ? "" : "s")}?");
        _dialog.SecondaryButtonText = "Stage instead";
        _dialog.CloseButtonText = "Cancel";

        var dsmRecycleBins = perTarget.ToDictionary(t => t.Target, t => CleanupFlowViewModel.DsmRecycleBinFolder(t.Target));
        var permanentTargets = perTarget
            .Where(t => !CleanupFlowViewModel.DeletesGoToRecycleBin(t.Target) && dsmRecycleBins[t.Target] is null)
            .ToList();
        var content = new StackPanel { Spacing = 12, Width = 560 };
        content.Children.Add(new InfoBar
        {
            IsOpen = true,
            IsClosable = false,
            Severity = permanentTargets.Count > 0 ? InfoBarSeverity.Error : InfoBarSeverity.Warning,
            Message = permanentTargets.Count > 0
                ? $"Deletion on {string.Join(" and ", permanentTargets.Select(t => SyncTargets.DisplayName(t.Target.Kind)))} is permanent and can't be undone. Make sure every photo you kept was picked."
                    + (permanentTargets.Any(t => t.Target.Kind == TargetKind.SynologyPhotos)
                        ? " To be able to undo it, first turn on the Recycle Bin for the shared folder in DSM (Control Panel › Shared Folder › Edit)."
                        : "")
                : dsmRecycleBins.Values.Any(folder => folder is not null)
                    ? "Deleted photos go to the Recycle Bin; on Synology Photos, to #recycle on the NAS while DSM's Recycle Bin is on. Make sure every photo you kept was picked."
                    : "Deleted photos go to the Recycle Bin. Make sure every photo you kept was picked.",
        });
        foreach (var (target, count, bytes) in perTarget)
        {
            bool isPermanent = permanentTargets.Any(t => t.Target == target);
            var where = new TextBlock
            {
                Text = isPermanent ? "Permanent"
                    : dsmRecycleBins[target] is not null ? "Goes to #recycle on the NAS"
                    : "Goes to the Recycle Bin",
                FontWeight = isPermanent ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = XamlFunctions.ThemeBrush(isPermanent ? "SystemFillColorCriticalBrush" : "TextFillColorPrimaryBrush"),
            };
            content.Children.Add(ConfirmDialogContent.TargetRow(target, count, bytes, where));
        }
        content.Children.Add(new TextBlock
        {
            Text = (keptByUserCount > 0
                ? $"{keptByUserCount:N0} photo{(keptByUserCount == 1 ? "" : "s")} you marked Keep stay untouched. "
                : "") + "Google Photos is not changed.",
            Style = (Style)Application.Current.Resources["SecondaryCaptionTextBlockStyle"],
            TextWrapping = TextWrapping.Wrap,
        });

        var hold = new HoldToConfirmButton { Label = $"Hold to delete {total:N0} file{(total == 1 ? "" : "s")}" };
        hold.Confirmed += (_, _) =>
        {
            _choice = DeleteChoice.Delete;
            _dialog.Hide();
        };
        content.Children.Add(hold);
        content.Children.Add(new TextBlock
        {
            Text = "Press and hold, or hold Space. Releasing early cancels.",
            Style = (Style)Application.Current.Resources["SecondaryCaptionTextBlockStyle"],
        });
        _dialog.Content = content;
    }

    public async Task<DeleteChoice> ChooseAsync()
    {
        var result = await _dialog.ShowAsync();
        return _choice == DeleteChoice.Delete ? DeleteChoice.Delete
            : result == ContentDialogResult.Secondary ? DeleteChoice.StageInstead
            : DeleteChoice.Cancel;
    }
}
