using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using ImageMagick;

namespace PhotoRecull;

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
        string oneDriveFolder,
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
                section { margin-bottom: 40px; }
                section h2 {
                  font-size: 1.15rem; margin-bottom: 14px;
                  padding-bottom: 6px; border-bottom: 2px solid #ddd;
                }
                section.remove h2 { border-color: #e74c3c; }
                section.keep   h2 { border-color: #2ecc71; }
                section.skip   h2 { border-color: #bdc3c7; }
                .grid {
                  display: grid;
                  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
                  gap: 14px;
                }
                .card {
                  background: #fff; border-radius: 8px;
                  overflow: hidden; box-shadow: 0 1px 4px #0001;
                  transition: box-shadow .15s ease, transform .15s ease;
                }
                .card:hover { box-shadow: 0 4px 14px #0003; transform: translateY(-2px); }
                .card a.thumb { display: block; cursor: pointer; text-decoration: none; }
                .card img {
                  width: 100%; aspect-ratio: 4/3; object-fit: cover;
                  display: block; background: #e8e8e8;
                }
                .card .no-thumb {
                  width: 100%; aspect-ratio: 4/3;
                  display: flex; align-items: center; justify-content: center;
                  font-size: 2.5rem; background: #e8e8e8; color: #aaa;
                }
                .card .info {
                  padding: 8px 10px; font-size: 0.75rem;
                  word-break: break-all; color: #444;
                }
                .card .info .name { font-weight: 600; color: #222; margin-bottom: 2px; }
                .card .info .ts   { color: #888; }
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
                OneDrive folder: <code>{{HtmlEncode(oneDriveFolder)}}</code>
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

        // --- REMOVE section --------------------------------------------------
        sb.AppendLine("""  <section class="remove">""");
        sb.AppendLine($"""    <h2>&#x1F5D1; To Remove ({result.Unmatched.Count})</h2>""");
        if (result.Unmatched.Count == 0)
        {
            sb.AppendLine("    <p>No candidates — all iPhone photos in this range exist in Google Photos.</p>");
        }
        else
        {
            sb.AppendLine("""    <div class="grid">""");
            foreach (var p in result.Unmatched.OrderByDescending(x => x.Timestamp))
                sb.AppendLine(PhotoCard(p, srcMap[p.Path]));
            sb.AppendLine("    </div>");
        }
        sb.AppendLine("  </section>");

        // --- KEEP section ----------------------------------------------------
        sb.AppendLine("""  <section class="keep">""");
        sb.AppendLine($"""    <h2>&#x2714; To Keep ({result.Matched.Count})</h2>""");
        if (result.Matched.Count == 0)
        {
            sb.AppendLine("    <p>No matched photos in this range.</p>");
        }
        else
        {
            sb.AppendLine("""    <div class="grid">""");
            foreach (var p in result.Matched.OrderByDescending(x => x.Timestamp))
                sb.AppendLine(PhotoCard(p, srcMap[p.Path]));
            sb.AppendLine("    </div>");
        }
        sb.AppendLine("  </section>");

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

    private static string PhotoCard(OneDrivePhoto p, string src)
    {
        var fileUri = "file:///" + p.Path.Replace('\\', '/').TrimStart('/');
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
              <div class="card">
                <a class="thumb" href="{fileUri}" target="_blank" rel="noopener" title="Open {name}">
                  <img src="{src}" alt="{name}" loading="lazy"
                       onerror="this.style.display='none';this.nextElementSibling.style.display='flex'">
                  <div class="no-thumb" style="display:none">{fallbackIcon}</div>
                </a>
                <div class="info">
                  <div class="name">{name}</div>
                  <div class="ts">{ts}</div>
                </div>
              </div>
            """;
    }

    /// <summary>
    /// Returns the value for an &lt;img src&gt;: an embedded JPEG data URI for
    /// HEIC/HEIF (which browsers can't render), otherwise a file:// URI.
    /// Falls back to the file:// URI if decoding fails.
    /// </summary>
    private static string BuildImageSrc(string path)
    {
        var fileUri = "file:///" + path.Replace('\\', '/').TrimStart('/');
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
