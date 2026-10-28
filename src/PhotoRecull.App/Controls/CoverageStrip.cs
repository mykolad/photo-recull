using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PhotoRecull.App.ViewModels;
using PhotoRecull.App.Views;

namespace PhotoRecull.App.Controls;

public sealed class CoverageStrip : UserControl
{
    private const double MonthLabelsHeight = 18;
    private const double TrackHeight = 24;
    private const double HatchSpacing = 6;
    private const double GapAfterSegment = 2;

    private readonly Canvas _monthLabels = new() { Height = MonthLabelsHeight };
    private readonly Canvas _track = new() { Height = TrackHeight };
    private int _year = DateTime.Today.Year;
    private IReadOnlyList<CoverageSegment> _segments = [];
    private DateOnly _today = DateOnly.FromDateTime(DateTime.Today);

    public CoverageStrip()
    {
        var layout = new StackPanel { Spacing = 4 };
        layout.Children.Add(_monthLabels);
        layout.Children.Add(_track);
        Content = layout;
        SizeChanged += (_, _) => Redraw();
        ActualThemeChanged += (_, _) => Redraw();
    }

    public event EventHandler<CoverageSegment>? SegmentClicked;

    public void Show(int year, IReadOnlyList<CoverageSegment> segments, DateOnly today)
    {
        _year = year;
        _segments = segments;
        _today = today;
        Redraw();
    }

    private void Redraw()
    {
        double width = ActualWidth;
        if (width <= 0)
            return;

        _monthLabels.Children.Clear();
        _track.Children.Clear();
        _track.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, width, TrackHeight) };

        var firstDay = new DateOnly(_year, 1, 1);
        var lastDay = new DateOnly(_year, 12, 31);
        int daysInYear = lastDay.DayNumber - firstDay.DayNumber + 1;
        double dayWidth = width / daysInYear;
        double XOf(DateOnly day) => (day.DayNumber - firstDay.DayNumber) * dayWidth;

        double pastWidth = _today >= lastDay ? width
            : _today < firstDay ? 0
            : XOf(_today) + dayWidth;

        DrawMonthLabels(XOf);
        DrawNotCleanedHatching(pastWidth);
        DrawSegments(firstDay, lastDay, XOf, dayWidth);
        DrawTodayLine(firstDay, lastDay, XOf, dayWidth);
    }

    private void DrawMonthLabels(Func<DateOnly, double> xOf)
    {
        var secondaryText = XamlFunctions.ThemeBrush("TextFillColorSecondaryBrush");
        var tickBrush = XamlFunctions.ThemeBrush("DividerStrokeColorDefaultBrush");
        for (int month = 1; month <= 12; month++)
        {
            double x = xOf(new DateOnly(_year, month, 1));
            var tick = new Rectangle { Width = 1, Height = MonthLabelsHeight, Fill = tickBrush };
            Canvas.SetLeft(tick, x);
            _monthLabels.Children.Add(tick);

            var label = new TextBlock
            {
                Text = new DateTime(_year, month, 1).ToString("MMM"),
                FontSize = 11,
                Foreground = secondaryText,
            };
            Canvas.SetLeft(label, x + 4);
            _monthLabels.Children.Add(label);
        }
    }

    private void DrawNotCleanedHatching(double pastWidth)
    {
        if (pastWidth <= 0)
            return;
        _track.Children.Add(new Rectangle
        {
            Width = pastWidth,
            Height = TrackHeight,
            RadiusX = 4,
            RadiusY = 4,
            Fill = XamlFunctions.ThemeBrush("SubtleFillColorSecondaryBrush"),
        });

        var hatchBrush = XamlFunctions.ThemeBrush("NotCleanedHatchBrush");
        var hatching = new Canvas
        {
            Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, pastWidth, TrackHeight) },
        };
        for (double x = -TrackHeight; x < pastWidth; x += HatchSpacing)
            hatching.Children.Add(new Line { X1 = x, Y1 = TrackHeight, X2 = x + TrackHeight, Y2 = 0, Stroke = hatchBrush, StrokeThickness = 1 });
        _track.Children.Add(hatching);
    }

    private void DrawSegments(DateOnly firstDay, DateOnly lastDay, Func<DateOnly, double> xOf, double dayWidth)
    {
        foreach (var segment in _segments.Where(s => s.To >= firstDay && s.From <= lastDay))
        {
            var visibleFrom = segment.From < firstDay ? firstDay : segment.From;
            var visibleTo = segment.To > lastDay ? lastDay : segment.To;
            double x = xOf(visibleFrom);
            var bar = new Rectangle
            {
                Width = Math.Max(1, xOf(visibleTo) + dayWidth - x - GapAfterSegment),
                Height = TrackHeight,
                Fill = XamlFunctions.ThemeBrush(StateBrushKey(segment.Status)),
            };
            ToolTipService.SetToolTip(bar, segment.Tooltip);
            bar.PointerEntered += (_, _) => bar.Opacity = 0.75;
            bar.PointerExited += (_, _) => bar.Opacity = 1;
            bar.Tapped += (_, _) => SegmentClicked?.Invoke(this, segment);
            Canvas.SetLeft(bar, x);
            _track.Children.Add(bar);
        }
    }

    private void DrawTodayLine(DateOnly firstDay, DateOnly lastDay, Func<DateOnly, double> xOf, double dayWidth)
    {
        if (_today < firstDay || _today > lastDay)
            return;
        var line = new Rectangle { Width = 2, Height = TrackHeight, Fill = XamlFunctions.ThemeBrush("TodayLineBrush") };
        Canvas.SetLeft(line, xOf(_today) + dayWidth - 1);
        _track.Children.Add(line);
    }

    public static string StateBrushKey(LibraryCardStatus status) => status switch
    {
        LibraryCardStatus.Done => "CleanupDoneBrush",
        LibraryCardStatus.Staged => "CleanupStagedBrush",
        LibraryCardStatus.ReadyToAct => "CleanupReadyToActBrush",
        LibraryCardStatus.Picked => "CleanupPickedBrush",
        _ => "NotCleanedHatchBrush",
    };
}
