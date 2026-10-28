using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoRecull.Core.Library;

namespace PhotoRecull.App.ViewModels;

public sealed partial class RangeSuggestionChipViewModel(RangeSuggestion suggestion, Action<RangeSuggestion> choose)
    : ObservableObject
{
    public RangeSuggestion Suggestion { get; } = suggestion;

    public string Text { get; } = $"{suggestion.Label} · {DateRangeLabel.Format(suggestion.From, suggestion.To)}";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Choose() => choose(Suggestion);
}
