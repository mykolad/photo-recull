using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PhotoRecull.App.ViewModels;
using PhotoRecull.App.Views;
using PhotoRecull.Core.Library;

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
    private IReadOnlyList<NotCleanedGap> _notCleanedGaps = [];
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

    // A UserControl has no automation peer of its own, so without this Narrator
    // never sees the strip or the spoken summary set in Show().
    protected override AutomationPeer OnCreateAutomationPeer() => new CoverageStripAutomationPeer(this);

    private sealed class CoverageStripAutomationPeer(CoverageStrip owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

        protected override string GetClassNameCore() => nameof(CoverageStrip);

        protected override bool IsControlElementCore() => true;

        protected override bool IsContentElementCore() => true;
    }

    public void Show(int year, IReadOnlyList<CoverageSegment> segments, IReadOnlyList<NotCleanedGap> notCleanedGaps, DateOnly today)
    {
        _year = year;
        _segments = segments;
        _notCleanedGaps = notCleanedGaps;
        _today = today;
        AutomationProperties.SetName(this, SpokenSummary());
        Redraw();
    }

    // The segments are only a pointer shortcut: every cleanup also has a card in
    // the Library with a focusable action, so screen readers get this summary
    // instead of one element per segment.
    private string SpokenSummary()
    {
        var segmentsThisYear = _segments.Where(s => s.From.Year <= _year && s.To.Year >= _year).ToList();
        return segmentsThisYear.Count == 0
            ? $"Coverage {_year}: no cleanups"
            : $"Coverage {_year}: " + string.Join("; ", segmentsThisYear.Select(s => s.Tooltip));
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
        DrawPastBackground(pastWidth);
        foreach (var gap in _notCleanedGaps.Where(g => g.To >= firstDay && g.From <= lastDay))
        {
            double gapStart = XOf(gap.From < firstDay ? firstDay : gap.From);
            double gapEnd = XOf(gap.To > lastDay ? lastDay : gap.To) + dayWidth;
            DrawNotCleanedHatching(gapStart, gapEnd);
        }
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

    private void DrawPastBackground(double pastWidth)
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
    }

    private void DrawNotCleanedHatching(double start, double end)
    {
        var hatchBrush = XamlFunctions.ThemeBrush("NotCleanedHatchBrush");
        var hatching = new Canvas
        {
            Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(start, 0, Math.Max(0, end - start), TrackHeight) },
        };
        // Lines start one track-height early so the diagonal reaches the left edge.
        for (double x = start - TrackHeight; x < end; x += HatchSpacing)
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
            AutomationProperties.SetAccessibilityView(bar, AccessibilityView.Raw);
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
