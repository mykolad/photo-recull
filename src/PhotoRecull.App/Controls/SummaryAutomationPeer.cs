using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;

namespace PhotoRecull.App.Controls;

// A UserControl has no automation peer of its own, so the name set with
// AutomationProperties.SetName never reaches Narrator. Controls that draw their
// content in code use this peer to expose that name as a one-line summary.
internal sealed class SummaryAutomationPeer(FrameworkElement owner, AutomationControlType controlType, string className)
    : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => controlType;

    protected override string GetClassNameCore() => className;

    protected override bool IsControlElementCore() => true;

    protected override bool IsContentElementCore() => true;
}
