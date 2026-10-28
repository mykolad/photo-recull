using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PhotoRecull.App.Views;
using VirtualKey = Windows.System.VirtualKey;

namespace PhotoRecull.App.Controls;

// A destructive action that has to be held, not clicked: press and hold (or hold
// Space/Enter) until the fill reaches the end. Letting go early cancels.
public sealed class HoldToConfirmButton : UserControl
{
    private readonly Border _fill = new() { HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    private readonly TextBlock _text = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = XamlFunctions.ThemeBrush("HoldToConfirmTextBrush"),
    };
    private readonly DispatcherQueueTimer _timer;
    private readonly Stopwatch _held = new();
    private string _label = "";
    private bool _isKeyHolding;

    public HoldToConfirmButton()
    {
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        Height = 32;
        var surface = new Grid
        {
            CornerRadius = new CornerRadius(4),
            Background = XamlFunctions.ThemeBrush("HoldToConfirmBackgroundBrush"),
            BorderBrush = XamlFunctions.ThemeBrush("HoldToConfirmBorderBrush"),
            BorderThickness = new Thickness(1),
        };
        _fill.Background = XamlFunctions.ThemeBrush("HoldToConfirmFillBrush");
        surface.Children.Add(_fill);
        surface.Children.Add(_text);
        Content = surface;

        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += (_, _) => UpdateProgress();

        PointerPressed += (_, e) =>
        {
            CapturePointer(e.Pointer);
            StartHolding();
            e.Handled = true;
        };
        PointerReleased += (_, _) => CancelHolding();
        PointerCanceled += (_, _) => CancelHolding();
        PointerCaptureLost += (_, _) => CancelHolding();
        KeyDown += OnKeyDown;
        KeyUp += (_, e) =>
        {
            if (e.Key is VirtualKey.Space or VirtualKey.Enter)
            {
                _isKeyHolding = false;
                CancelHolding();
            }
        };
        LostFocus += (_, _) => CancelHolding();
        SizeChanged += (_, _) => UpdateProgress();
    }

    public TimeSpan HoldDuration { get; set; } = TimeSpan.FromSeconds(3);

    public string Label
    {
        get => _label;
        set
        {
            _label = value;
            _text.Text = value;
            AutomationProperties.SetName(this, $"{value}. Press and hold Space to confirm.");
        }
    }

    public event EventHandler? Confirmed;

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.Space or VirtualKey.Enter))
            return;
        e.Handled = true;
        if (_isKeyHolding)
            return; // key repeat while held
        _isKeyHolding = true;
        StartHolding();
    }

    private void StartHolding()
    {
        _held.Restart();
        _timer.Start();
    }

    private void CancelHolding()
    {
        _timer.Stop();
        _held.Reset();
        _fill.Width = 0;
        _text.Text = _label;
    }

    private void UpdateProgress()
    {
        if (!_held.IsRunning)
            return;
        double progress = Math.Min(1, _held.Elapsed / HoldDuration);
        _fill.Width = ActualWidth * progress;
        _text.Text = $"Keep holding… {(HoldDuration - _held.Elapsed).TotalSeconds:0.0} s";
        if (progress < 1)
            return;
        _timer.Stop();
        _held.Reset();
        _text.Text = _label;
        Confirmed?.Invoke(this, EventArgs.Empty);
    }
}
