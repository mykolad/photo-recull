using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PhotoRecull.App.Views;

namespace PhotoRecull.App.Controls;

public sealed class FlowStepper : UserControl
{
    private const double CircleSize = 22;
    private static readonly string[] StepLabels = ["Pick", "Range", "Scan", "Review", "Confirm", "Result"];

    private readonly StackPanel _steps = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private int _currentStepIndex;

    public FlowStepper()
    {
        Content = _steps;
        ActualThemeChanged += (_, _) => Redraw();
        CurrentStepIndex = 0;
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new SummaryAutomationPeer(this, AutomationControlType.Group, nameof(FlowStepper));

    public int CurrentStepIndex
    {
        get => _currentStepIndex;
        set
        {
            _currentStepIndex = value;
            AutomationProperties.SetName(this, $"Step {value + 1} of {StepLabels.Length}: {StepLabels[value]}");
            Redraw();
        }
    }

    private void Redraw()
    {
        _steps.Children.Clear();
        var accent = XamlFunctions.ThemeBrush("AccentFillColorDefaultBrush");
        var onAccent = XamlFunctions.ThemeBrush("TextOnAccentFillColorPrimaryBrush");
        var upcoming = XamlFunctions.ThemeBrush("ControlStrongStrokeColorDefaultBrush");
        var secondaryText = XamlFunctions.ThemeBrush("TextFillColorSecondaryBrush");

        for (int i = 0; i < StepLabels.Length; i++)
        {
            bool isDone = i < _currentStepIndex;
            bool isCurrent = i == _currentStepIndex;

            if (i > 0)
                _steps.Children.Add(new Rectangle { Width = 20, Height = 1, Fill = upcoming, VerticalAlignment = VerticalAlignment.Center });

            var circle = new Border
            {
                Width = CircleSize,
                Height = CircleSize,
                CornerRadius = new CornerRadius(CircleSize / 2),
                BorderThickness = new Thickness(isCurrent ? 2 : 1),
                BorderBrush = isDone || isCurrent ? accent : upcoming,
                Background = isDone ? accent : null,
                Child = isDone
                    ? new FontIcon { Glyph = "", FontSize = 11, Foreground = onAccent }
                    : new TextBlock
                    {
                        Text = (i + 1).ToString(),
                        FontSize = 11,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = isCurrent ? accent : secondaryText,
                    },
            };

            var step = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            step.Children.Add(circle);
            step.Children.Add(new TextBlock
            {
                Text = StepLabels[i],
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = isCurrent ? XamlFunctions.ThemeBrush("TextFillColorPrimaryBrush") : secondaryText,
            });
            _steps.Children.Add(step);
        }
    }
}
