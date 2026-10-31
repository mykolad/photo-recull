using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using ImageMagick;
using PhotoRecull.Core;
using PhotoRecull.Core.Library;

namespace PhotoRecull.Cli;

/// <summary>
/// Generates a self-contained HTML review page.
/// Browser-native formats (jpg/png) are referenced via file:// URIs so no
/// files are copied. HEIC/HEIF — which browsers cannot decode — are converted
/// to small embedded JPEG thumbnails (base64 data URIs) via Magick.NET.
/// </summary>
public static class HtmlReportGenerator
{
    private const int ThumbnailMaxPixels = 400;
    public static void Write(
        string outputPath,
        MatchResult result,
        string targetName,
        string targetFolder,
        DateOnly? fromDate,
        DateOnly? toDate)
    {
        var sb = new StringBuilder(512 * 1024);

        // Pre-compute the <img> source for every displayed photo. HEIC files are
        // decoded to embedded JPEG thumbnails; everything else uses a file:// URI.
        // Done in parallel because HEIC decode is CPU-bound.
        var displayed = result.Unmatched.Concat(result.Matched).ToList();
        var srcMap = new ConcurrentDictionary<string, string>();
        Parallel.ForEach(displayed, p => srcMap[p.Path] = BuildImageSrc(p.Path));

        var rangeLabel = (fromDate, toDate) switch
        {
            (not null, not null) => $"{fromDate:yyyy-MM-dd} – {toDate:yyyy-MM-dd}",
            (not null, null)     => $"from {fromDate:yyyy-MM-dd}",
            (null, not null)     => $"up to {toDate:yyyy-MM-dd}",
            _                    => "entire library"
        };

        sb.AppendLine($$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="UTF-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>PhotoRecull Review — {{rangeLabel}}</title>
              <style>
                *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
                body {
                  font-family: system-ui, -apple-system, sans-serif;
                  background: #f0f0f0;
                  color: #222;
                  padding: 24px;
                }
                h1 { font-size: 1.5rem; margin-bottom: 4px; }
                .meta { color: #555; font-size: 0.9rem; margin-bottom: 24px; }
                .stats {
                  display: flex; gap: 16px; flex-wrap: wrap;
                  margin-bottom: 28px;
                }
                .stat {
                  background: #fff; border-radius: 8px; padding: 12px 20px;
                  text-align: center; min-width: 120px; box-shadow: 0 1px 4px #0001;
                }
                .stat .num { font-size: 2rem; font-weight: 700; }
                .stat .lbl { font-size: 0.8rem; color: #666; margin-top: 2px; }
                .stat.remove .num { color: #c0392b; }
                .stat.keep   .num { color: #27ae60; }
                .stat.skip   .num { color: #7f8c8d; }
                section { margin-bottom: 32px; }
                section h2 {
                  font-size: 1.05rem; margin-bottom: 12px;
                  padding-bottom: 6px; border-bottom: 2px solid #ddd;
                }
                section h2 span { font-weight: 400; color: #666; }
                section.skip   h2 { border-color: #bdc3c7; }

                /* Filter: All / Hide photos to remove / Only photos to remove */
                .filter-choice { position: absolute; opacity: 0; pointer-events: none; }
                .filter { display: inline-flex; margin-bottom: 24px; border-radius: 8px; box-shadow: 0 1px 4px #0001; }
                .filter label {
                  background: #fff; padding: 8px 14px; font-size: 0.85rem; cursor: pointer; user-select: none;
                  border-right: 1px solid #e4e4e4;
                }
                .filter label:first-child { border-radius: 8px 0 0 8px; }
                .filter label:last-child { border-radius: 0 8px 8px 0; border-right: none; }
                #show-all:checked ~ .filter label[for=show-all],
                #show-hide-remove:checked ~ .filter label[for=show-hide-remove],
                #show-only-remove:checked ~ .filter label[for=show-only-remove] { background: #2c3e50; color: #fff; }
                .filter-choice:focus-visible ~ .filter { outline: 2px solid #2c3e50; outline-offset: 2px; }
                #show-hide-remove:checked ~ .timeline .remove,
                #show-hide-remove:checked ~ .timeline .day:not(.has-stay),
                #show-hide-remove:checked ~ .timeline .stack:not(.has-stay),
                #show-only-remove:checked ~ .timeline .stays,
                #show-only-remove:checked ~ .timeline .day:not(.has-remove),
                #show-only-remove:checked ~ .timeline .stack:not(.has-remove) { display: none; }

                .grid {
                  display: grid;
                  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
                  gap: 14px;
                }
                .card, .stack {
                  background: #fff; border-radius: 8px;
                  box-shadow: 0 1px 4px #0001; position: relative;
                  transition: box-shadow .15s ease, transform .15s ease;
                }
                .card { overflow: hidden; }
                .card:hover { box-shadow: 0 4px 14px #0003; transform: translateY(-2px); }
                .card a.thumb { display: block; cursor: pointer; text-decoration: none; position: relative; }
                .card img, .cover img {
                  width: 100%; aspect-ratio: 4/3; object-fit: cover;
                  display: block; background: #e8e8e8;
                }
                .card .no-thumb {
                  width: 100%; aspect-ratio: 4/3;
                  display: flex; align-items: center; justify-content: center;
                  font-size: 2.5rem; background: #e8e8e8; color: #aaa;
                }
                .card .info, .stack .info {
                  padding: 8px 10px; font-size: 0.75rem;
                  word-break: break-all; color: #444;
                }
                .info .name { font-weight: 600; color: #222; margin-bottom: 2px; }
                .info .ts   { color: #888; }

                /* Photos to remove: dimmed and outlined, but still in place */
                .card.remove { outline: 2px solid #c0392b; outline-offset: -2px; }
                .card.remove img, .cover.remove img { opacity: .4; }
                .remove-badge {
                  position: absolute; top: 6px; right: 6px;
                  width: 24px; height: 24px; border-radius: 12px; background: #c0392b;
                  display: none; align-items: center; justify-content: center;
                }
                .remove > .remove-badge, .remove .thumb > .remove-badge { display: flex; }

                /* Bursts: one stacked cell that opens across the row */
                .stack summary { list-style: none; cursor: pointer; }
                .stack summary::-webkit-details-marker { display: none; }
                .cover { position: relative; border-radius: 8px 8px 0 0; overflow: hidden; background: #fff; }
                .cover.remove { outline: 2px solid #c0392b; outline-offset: -2px; }
                .stack:not([open]) { box-shadow: 5px -5px 0 -1px #fff, 5px -5px 0 0 #bbb, 0 1px 4px #0001; margin: 5px 5px 0 0; }
                .stack.has-stay .cover.remove { display: none; }
                #show-only-remove:checked ~ .timeline .stack.has-stay:not([open]) .cover.remove { display: block; }
                .cover .count {
                  position: absolute; right: 6px; bottom: 6px; padding: 1px 6px; border-radius: 4px;
                  background: #0009; color: #fff; font-size: 0.75rem;
                }
                .cover .count.split { padding: 0; overflow: hidden; display: flex; }
                .cover .count.split span { padding: 1px 6px; display: flex; align-items: center; gap: 3px; }
                .cover .count.split .goes { background: #c0392b; }
                .stack .stack-header { display: none; font-size: 0.8rem; color: #444; padding: 10px 12px; }
                .stack .stack-header span { float: right; color: #2c3e50; }
                .stack[open] { grid-column: 1 / -1; background: #e6e6e6; box-shadow: none; padding-bottom: 12px; }
                .stack[open] summary > .cover, .stack[open] summary > .info { display: none; }
                .stack[open] .stack-header { display: block; }
                .stack[open] > .grid { padding: 0 12px; }
                .skipped-list { list-style: none; }
                .skipped-list li {
                  padding: 6px 0; border-bottom: 1px solid #eee;
                  font-size: 0.85rem; color: #555;
                }
                .skipped-list li span { color: #999; margin-left: 8px; font-size: 0.8rem; }
                summary { cursor: pointer; user-select: none; }
              </style>
            </head>
            <body>
              <h1>PhotoRecull Review</h1>
              <p class="meta">
                Date range: <strong>{{rangeLabel}}</strong> &nbsp;|&nbsp;
                {{HtmlEncode(targetName)}} folder: <code>{{HtmlEncode(targetFolder)}}</code> &nbsp;|&nbsp; Days and times in UTC
              </p>

              <div class="stats">
                <div class="stat remove">
                  <div class="num">{{result.Unmatched.Count}}</div>
                  <div class="lbl">To Remove</div>
                </div>
                <div class="stat keep">
                  <div class="num">{{result.Matched.Count}}</div>
                  <div class="lbl">To Keep</div>
                </div>
                <div class="stat skip">
                  <div class="num">{{result.Skipped.Count}}</div>
                  <div class="lbl">Skipped</div>
                </div>
              </div>
            """);

        // --- TIMELINE --------------------------------------------------------
        // The filter is radio buttons ahead of the timeline, so CSS alone can
        // hide photos (no script needed for a file:// page).
        sb.AppendLine("""
              <input class="filter-choice" type="radio" name="show" id="show-all" checked>
              <input class="filter-choice" type="radio" name="show" id="show-hide-remove">
              <input class="filter-choice" type="radio" name="show" id="show-only-remove">
              <nav class="filter">
                <label for="show-all">All photos</label>
                <label for="show-hide-remove">Hide photos to remove</label>
                <label for="show-only-remove">Only photos to remove</label>
              </nav>
            """);
        sb.AppendLine("""  <main class="timeline">""");
        if (displayed.Count == 0)
            sb.AppendLine("    <p>No iPhone photos in this range.</p>");
        var toRemove = result.Unmatched.ToHashSet();
        ReviewMark MarkOf(PhotoFile p) => toRemove.Contains(p) ? ReviewMark.ToRemove : ReviewMark.Stays;
        foreach (var day in ReviewTimeline.NewestFirst(displayed, MarkOf, ReviewFilter.All, TimeZoneInfo.Utc))
        {
            int dayToRemove = day.Photos.Count(toRemove.Contains);
            var dayLabel = day.Day?.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture) ?? "No capture time";
            sb.AppendLine($"""  <section class="day{MarkClasses(day.Photos.Count - dayToRemove, dayToRemove)}">""");
            sb.AppendLine($"""    <h2>{dayLabel} <span>· {ReviewTimeline.DayCaption(day.Photos.Count - dayToRemove, dayToRemove)}</span></h2>""");
            sb.AppendLine("""    <div class="grid">""");
            foreach (var stack in day.Stacks)
            {
                if (!stack.IsBurst)
                    sb.AppendLine(PhotoCard(stack.Frames[0], srcMap[stack.Frames[0].Path], MarkOf(stack.Frames[0])));
                else
                    sb.AppendLine(BurstStack(stack, srcMap, MarkOf));
            }
            sb.AppendLine("    </div>");
            sb.AppendLine("  </section>");
        }
        sb.AppendLine("  </main>");

        // --- SKIPPED section (collapsible) -----------------------------------
        if (result.Skipped.Count > 0)
        {
            sb.AppendLine("""  <section class="skip">""");
            sb.AppendLine($"""    <details><summary><h2 style="display:inline">&#x2296; Skipped — not iPhone ({result.Skipped.Count})</h2></summary>""");
            sb.AppendLine("""    <ul class="skipped-list" style="margin-top:12px">""");
            foreach (var p in result.Skipped.OrderBy(x => x.FileName))
            {
                var reason = !p.IsIphone ? "not iPhone" : "no timestamp";
                sb.AppendLine($"""      <li>{HtmlEncode(p.FileName)}<span>{reason}</span></li>""");
            }
            sb.AppendLine("    </ul>");
            sb.AppendLine("    </details>");
            sb.AppendLine("  </section>");
        }

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
    }

    private static readonly HashSet<string> VideoExtensions = [".mp4", ".mov"];

    // Days and bursts say whether anything in them stays or goes, so the filter
    // can hide the ones it empties.
    private static string MarkClasses(int staying, int toRemove) =>
        (staying > 0 ? " has-stay" : "") + (toRemove > 0 ? " has-remove" : "");

    // A burst is one cell, like a stack in Google Photos: its cover is the frame
    // that stays (and, for the "only to remove" filter, the newest one that goes).
    // Opening it lays the frames out across the row.
    private static string BurstStack(TimelineStack stack, IReadOnlyDictionary<string, string> srcMap, Func<PhotoFile, ReviewMark> markOf)
    {
        int toRemove = stack.Frames.Count(f => markOf(f) == ReviewMark.ToRemove);
        int staying = stack.Frames.Count - toRemove;
        var title = $"Burst · {stack.Frames.Count} frames within {stack.SpanSeconds} s";
        var caption = ReviewTimeline.StackCaption(staying, toRemove, stack.Frames.Count);

        var covers = new StringBuilder();
        if (stack.Frames.FirstOrDefault(f => markOf(f) == ReviewMark.Stays) is { } stayingCover)
            covers.Append(StackCover(stayingCover, srcMap[stayingCover.Path], ReviewMark.Stays, staying, toRemove));
        if (stack.Frames.FirstOrDefault(f => markOf(f) == ReviewMark.ToRemove) is { } removedCover)
            covers.Append(StackCover(removedCover, srcMap[removedCover.Path], ReviewMark.ToRemove, staying, toRemove));

        var frames = string.Concat(stack.Frames.Select(f => PhotoCard(f, srcMap[f.Path], markOf(f))));
        return $"""
              <details class="stack{MarkClasses(staying, toRemove)}">
                <summary title="Show the frames">
                  {covers}
                  <div class="info">
                    <div class="name">{title}</div>
                    <div class="ts">{caption}</div>
                  </div>
                  <div class="stack-header">&#x2750; {title} · {caption} <span>&#x25B4; Stack</span></div>
                </summary>
                <div class="grid">{frames}</div>
              </details>
            """;
    }

    // A burst with frames that stay and frames that go shows both counts, so a
    // cover that stays doesn't read as the whole burst.
    private static string StackCover(PhotoFile p, string src, ReviewMark mark, int staying, int toRemove)
    {
        var name = HtmlEncode(p.FileName);
        var count = staying > 0 && toRemove > 0
            ? $"""<span class="count split" title="{staying} stay, {toRemove} to remove"><span>&#x2713; {staying}</span><span class="goes">{TrashIcon} {toRemove}</span></span>"""
            : $"""<span class="count">&#x2750; {staying + toRemove}</span>""";
        return $"""
              <div class="cover {MarkClass(mark)}">
                <img src="{src}" alt="{name}" loading="lazy" onerror="this.style.visibility='hidden'">
                <span class="remove-badge" title="To remove">{TrashIcon}</span>
                {count}
              </div>
            """;
    }

    private const string TrashIcon =
        """<svg viewBox="0 0 16 16" width="12" height="12" fill="none" stroke="#fff" stroke-width="1.5" stroke-linecap="round"><path d="M2.5 4h11M6 4V2.5h4V4M4 4l.7 9.5h6.6L12 4M6.7 6.5v4.5M9.3 6.5v4.5"/></svg>""";

    private static string MarkClass(ReviewMark mark) => mark == ReviewMark.ToRemove ? "remove" : "stays";

    private static string PhotoCard(PhotoFile p, string src, ReviewMark mark)
    {
        var fileUri = FileUri(p.Path);
        var ts = p.Timestamp.HasValue
            ? p.Timestamp.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC"
            : "(unknown time)";
        var name = HtmlEncode(p.FileName);
        var ext = Path.GetExtension(p.FileName).ToLowerInvariant();
        // Videos have no thumbnail; show a play glyph instead of the camera one.
        var fallbackIcon = VideoExtensions.Contains(ext) ? "&#x25B6;&#xFE0F;" : "&#x1F4F7;";

        // The whole thumbnail links to the original file (opens in a new browser
        // tab — plays mp4 inline, shows full-size images). img has an onerror
        // fallback to an icon for formats the <img> can't render (e.g. video).
        return $"""
              <div class="card {MarkClass(mark)}">
                <a class="thumb" href="{fileUri}" target="_blank" rel="noopener" title="Open {name}">
                  <img src="{src}" alt="{name}" loading="lazy"
                       onerror="this.style.display='none';this.nextElementSibling.style.display='flex'">
                  <div class="no-thumb" style="display:none">{fallbackIcon}</div>
                  <span class="remove-badge" title="To remove">{TrashIcon}</span>
                </a>
                <div class="info">
                  <div class="name">{name}</div>
                  <div class="ts">{ts}</div>
                </div>
              </div>
            """;
    }

    // file:///C:/... for a local file, file://nas/home/... for a network one, with
    // characters such as # and spaces escaped, ready for an HTML attribute.
    private static string FileUri(string path) => HtmlEncode(new Uri(Path.GetFullPath(path)).AbsoluteUri);

    /// <summary>
    /// Returns the value for an &lt;img src&gt;: an embedded JPEG data URI for
    /// HEIC/HEIF (which browsers can't render), otherwise a file:// URI.
    /// Falls back to the file:// URI if decoding fails.
    /// </summary>
    private static string BuildImageSrc(string path)
    {
        var fileUri = FileUri(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();

        if (ext is ".heic" or ".heif")
            return TryHeicThumbnailDataUri(path) ?? fileUri;

        return fileUri;
    }

    private static string? TryHeicThumbnailDataUri(string path)
    {
        try
        {
            using var image = new MagickImage(path);
            image.AutoOrient();   // respect EXIF orientation
            image.Thumbnail(new MagickGeometry((uint)ThumbnailMaxPixels, (uint)ThumbnailMaxPixels));
            image.Quality = 80;
            image.Format = MagickFormat.Jpeg;
            var bytes = image.ToByteArray();
            return "data:image/jpeg;base64," + Convert.ToBase64String(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static string HtmlEncode(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
