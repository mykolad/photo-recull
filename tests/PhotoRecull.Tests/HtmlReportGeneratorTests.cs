using System.Text.RegularExpressions;
using PhotoRecull.Cli;
using PhotoRecull.Core;

namespace PhotoRecull.Tests;

public class HtmlReportGeneratorTests : IDisposable
{
    private readonly string _reportPath = Path.Combine(Path.GetTempPath(), $"photorecull-report-{Guid.NewGuid():N}.html");

    public void Dispose() => File.Delete(_reportPath);

    private static PhotoFile PhotoAt(string name, int hour, int minute, int second) =>
        new($@"C:\fake\{name}", name, new DateTimeOffset(2026, 7, 14, hour, minute, second, TimeSpan.Zero), IsIphone: true, TimestampSource: "filename");

    private string Render(MatchResult result)
    {
        HtmlReportGenerator.Write(_reportPath, result, "OneDrive", @"C:\fake", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
        return File.ReadAllText(_reportPath);
    }

    [Fact]
    public void PhotosToRemoveSitInTheSameTimelineAsThoseThatStay()
    {
        var stays = PhotoAt("stays.jpg", 9, 0, 0);
        var goes = PhotoAt("goes.jpg", 12, 0, 0);

        var html = Render(new MatchResult([stays], [goes], []));

        Assert.Single(Regex.Matches(html, """<section class="day has-stay has-remove">"""));
        Assert.Contains("Tue 14 Jul 2026 <span>· 1 stays · 1 to remove</span>", html);
        Assert.True(html.IndexOf("goes.jpg", StringComparison.Ordinal) < html.IndexOf("stays.jpg", StringComparison.Ordinal),
            "newest first");
        Assert.Matches("""<div class="card remove">\s*<a class="thumb" href="file:///C:/fake/goes.jpg""", html);
        Assert.Matches("""<div class="card stays">\s*<a class="thumb" href="file:///C:/fake/stays.jpg""", html);
    }

    [Fact]
    public void ABurstIsOneStack_WithTheFrameThatStaysAsItsCover()
    {
        var newest = PhotoAt("c.jpg", 10, 0, 2);
        var kept = PhotoAt("b.jpg", 10, 0, 1);
        var oldest = PhotoAt("a.jpg", 10, 0, 0);

        var html = Render(new MatchResult([kept], [newest, oldest], []));

        var stack = Regex.Match(html, """<details class="stack has-stay has-remove">.*?</details>""", RegexOptions.Singleline);
        Assert.True(stack.Success);
        Assert.Contains("Burst · 3 frames within 2 s", stack.Value);
        Assert.Contains("1 stays · 2 go", stack.Value);
        Assert.Matches("""<div class="cover stays">\s*<img src="file:///C:/fake/b.jpg""", stack.Value);
        Assert.Matches("""<div class="cover remove">\s*<img src="file:///C:/fake/c.jpg""", stack.Value);
        Assert.Equal(3, Regex.Matches(stack.Value, """<div class="card """).Count);
    }

    [Fact]
    public void APhotoOnTheNetwork_LinksToItsServer_WithItsPathEscaped()
    {
        var onNas = PhotoAt("IMG_0001.JPG", 9, 0, 0) with { Path = @"\\nas\home\Photos\2026 #1\IMG_0001.JPG" };

        var html = Render(new MatchResult([onNas], [], []));

        Assert.Contains("""href="file://nas/home/Photos/2026%20%231/IMG_0001.JPG" """, html);
    }

    [Fact]
    public void TheFilterIsThreeRadioButtonsAheadOfTheTimeline()
    {
        var html = Render(new MatchResult([PhotoAt("a.jpg", 9, 0, 0)], [], []));

        Assert.Matches("""id="show-all" checked>\s*<input [^>]*id="show-hide-remove">\s*<input [^>]*id="show-only-remove">\s*<nav class="filter">""", html);
        Assert.True(html.IndexOf("""<nav class="filter">""", StringComparison.Ordinal) < html.IndexOf("""<main class="timeline">""", StringComparison.Ordinal));
    }
}
