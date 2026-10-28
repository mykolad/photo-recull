using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using PhotoRecull.App.Views;
using PhotoRecull.Core.Library;

namespace PhotoRecull.App.Controls;

// "Your pick" and "Clean up" drawn on one shared timeline, so it's visible at a
// glance which days the range covers that the pick doesn't (and vice versa).
public sealed class RangeComparisonBars : UserControl
{
    private const double LabelColumnWidth = 150;
    private const double TrackHeight = 20;

    private readonly Canvas _monthTicks = new() { Height = 16 };
    private readonly Canvas _pickTrack = new() { Height = TrackHeight };
    private readonly Canvas _rangeTrack = new() { Height = TrackHeight };
    private readonly TextBlock _pickLabel = Label();
    private readonly TextBlock _rangeLabel = Label();
    private (DateOnly From, DateOnly To)? _pick;
    private (DateOnly From, DateOnly To)? _range;

    public RangeComparisonBars()
    {
        var grid = new Grid { RowSpacing = 6, ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelColumnWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int row = 0; row < 3; row++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Place(grid, _monthTicks, row: 0, column: 1);
        Place(grid, _pickLabel, row: 1, column: 0);
        Place(grid, _pickTrack, row: 1, column: 1);
        Place(grid, _rangeLabel, row: 2, column: 0);
        Place(grid, _rangeTrack, row: 2, column: 1);
        Content = grid;

        _pickTrack.SizeChanged += (_, _) => Redraw();
        ActualThemeChanged += (_, _) => Redraw();
    }

    public void Show(DateOnly pickFrom, DateOnly pickTo, (DateOnly From, DateOnly To)? range)
    {
        _pick = (pickFrom, pickTo);
        _range = range;
        _pickLabel.Text = $"Your pick\n{DateRangeLabel.Format(pickFrom, pickTo)}";
        _rangeLabel.Text = range is { } r
            ? $"Clean up\n{DateRangeLabel.Format(r.From, r.To)} · {r.To.DayNumber - r.From.DayNumber + 1} days"
            : "Clean up\nNo valid range";
        Redraw();
    }

    private void Redraw()
    {
        // Set on every redraw (including theme changes) rather than once, so the
        // labels stay readable after switching between light and dark.
        _pickLabel.Foreground = _rangeLabel.Foreground = XamlFunctions.ThemeBrush("TextFillColorSecondaryBrush");
        double width = _pickTrack.ActualWidth;
        if (width <= 0 || _pick is not { } pick)
            return;

        var domainStart = _range is { } r && r.From < pick.From ? r.From : pick.From;
        var domainEnd = _range is { } r2 && r2.To > pick.To ? r2.To : pick.To;
        double daySpan = domainEnd.DayNumber - domainStart.DayNumber + 1;
        double XOf(DateOnly day) => (day.DayNumber - domainStart.DayNumber) / daySpan * width;

        _monthTicks.Children.Clear();
        var month = new DateOnly(domainStart.Year, domainStart.Month, 1);
        var tickBrush = XamlFunctions.ThemeBrush("DividerStrokeColorDefaultBrush");
        var tickText = XamlFunctions.ThemeBrush("TextFillColorSecondaryBrush");
        while (month <= domainEnd)
        {
            double x = Math.Max(0, XOf(month));
            if (x < width * 0.96)
            {
                var tick = new Rectangle { Width = 1, Height = 16, Fill = tickBrush };
                Canvas.SetLeft(tick, x);
                var label = new TextBlock { Text = month.ToString("MMM"), FontSize = 11, Foreground = tickText };
                Canvas.SetLeft(label, x + 4);
                _monthTicks.Children.Add(tick);
                _monthTicks.Children.Add(label);
            }
            month = month.AddMonths(1);
        }

        DrawBar(_pickTrack, XOf(pick.From), XOf(pick.To.AddDays(1)), "CleanupPickedBrush", width);
        if (_range is { } range)
            DrawBar(_rangeTrack, XOf(range.From), XOf(range.To.AddDays(1)), "CleanupReadyToActBrush", width);
        else
            DrawBar(_rangeTrack, 0, 0, "CleanupReadyToActBrush", width);
    }

    private static void DrawBar(Canvas track, double start, double end, string brushKey, double trackWidth)
    {
        track.Children.Clear();
        track.Children.Add(new Rectangle
        {
            Width = trackWidth,
            Height = TrackHeight,
            RadiusX = 4,
            RadiusY = 4,
            Fill = XamlFunctions.ThemeBrush("SubtleFillColorSecondaryBrush"),
        });
        if (end <= start)
            return;
        var bar = new Rectangle
        {
            Width = end - start,
            Height = TrackHeight,
            RadiusX = 4,
            RadiusY = 4,
            Fill = XamlFunctions.ThemeBrush(brushKey),
        };
        Canvas.SetLeft(bar, start);
        track.Children.Add(bar);
    }

    private static TextBlock Label() => new()
    {
        FontSize = 12,
        LineHeight = 16,
        TextWrapping = TextWrapping.Wrap,
    };

    private static void Place(Grid grid, FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }
}
