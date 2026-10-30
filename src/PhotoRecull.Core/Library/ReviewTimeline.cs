namespace PhotoRecull.Core.Library;

public enum ReviewMark
{
    Stays,
    KeptByYou,
    ToRemove,
}

// Like a reject filter in a photo editor: photos to remove are dimmed in the
// timeline but stay visible unless they're explicitly hidden.
public enum ReviewFilter
{
    All,
    HideToRemove,
    OnlyToRemove,
}

// One press of the shutter: a single photo, or a burst shown as one stack.
// Frames are newest first; Shown are the frames the filter lets through.
public sealed record TimelineStack(List<PhotoFile> Frames, List<PhotoFile> Shown, int SpanSeconds)
{
    public bool IsBurst => Frames.Count > 1;
}

// Day is null for photos without a capture time (only ever in Skipped). Photos
// are all of the day's photos, including those the filter hides, so the day's
// counts don't change with the filter.
public sealed record TimelineDay(DateOnly? Day, List<PhotoFile> Photos, List<TimelineStack> Stacks);

public static class ReviewTimeline
{
    // Photos you kept by hand are both staying and candidates, so they show
    // under either filter and can be undone from there.
    public static bool IsShown(ReviewMark mark, ReviewFilter filter) => filter switch
    {
        ReviewFilter.HideToRemove => mark != ReviewMark.ToRemove,
        ReviewFilter.OnlyToRemove => mark != ReviewMark.Stays,
        _ => true,
    };

    // Bursts are found among all photos, so a filtered stack is still the same
    // burst; stacks and days the filter empties are left out.
    public static List<TimelineDay> NewestFirst(
        IEnumerable<PhotoFile> photos, Func<PhotoFile, ReviewMark> markOf, ReviewFilter filter, TimeZoneInfo dayTimeZone) =>
        ReviewGrouping.NewestFirstByDayWithBursts(photos, dayTimeZone)
            .Select(day => new TimelineDay(day.Day, day.Groups.SelectMany(g => g.Photos).ToList(), day.Groups
                .Select(group => new TimelineStack(group.Photos, group.Photos.Where(p => IsShown(markOf(p), filter)).ToList(), group.SpanSeconds))
                .Where(stack => stack.Shown.Count > 0)
                .ToList()))
            .Where(day => day.Stacks.Count > 0)
            .ToList();

    // The frame that stays stands for the burst, like the top pick of a stack in
    // Google Photos; a burst that goes entirely shows its newest frame.
    public static T Cover<T>(IReadOnlyList<T> newestFirst, Func<T, ReviewMark> markOf) =>
        newestFirst.FirstOrDefault(frame => markOf(frame) != ReviewMark.ToRemove) ?? newestFirst[0];

    public static string StackCaption(int staying, int toRemove, int shown)
    {
        int total = staying + toRemove;
        var caption = (staying, toRemove) switch
        {
            (_, 0) => $"All {total} stay",
            (0, _) => $"All {total} go",
            _ => $"{staying} {(staying == 1 ? "stays" : "stay")} · {toRemove} {(toRemove == 1 ? "goes" : "go")}",
        };
        return shown < total ? $"{caption} · {shown} of {total} shown" : caption;
    }

    public static string DayCaption(int staying, int toRemove) => (staying, toRemove) switch
    {
        (_, 0) => $"{staying} {(staying == 1 ? "stays" : "stay")}",
        (0, _) => $"{toRemove} to remove",
        _ => $"{staying} {(staying == 1 ? "stays" : "stay")} · {toRemove} to remove",
    };
}
