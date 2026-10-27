# PhotoRecull

Repeat your Google Photos culls on your other copies: find and remove OneDrive
Camera Roll photos that you've already deleted from Google Photos.

🌐 **Website:** [mykolad.github.io/photo-recull](https://mykolad.github.io/photo-recull/)
(setup and usage guides, [privacy policy](https://mykolad.github.io/photo-recull/privacy),
[terms of service](https://mykolad.github.io/photo-recull/terms))

## Why

iPhone photos back up to Google Photos, iCloud, **and** OneDrive. Deleting a photo
from Google Photos also removes it from iCloud (they sync) — but OneDrive keeps its
copy forever. Over years, OneDrive accumulates thousands of stale duplicates you
already curated out of Google Photos. PhotoRecull finds them.

## How it works

1. You select the photos you **still have** in Google Photos using the Google Photos
   Picker (opens in your browser, multi-select).
2. PhotoRecull scans your local OneDrive folder, identifies iPhone photos, and reads
   each one's capture time.
3. It matches OneDrive photos to Google photos by UTC timestamp (±2s, **one-to-one** —
   so a culled burst doesn't keep every frame).
4. OneDrive iPhone photos with **no** match were deleted from Google Photos → they're
   flagged as removal candidates.
5. Review them in an HTML report, then **stage** (move for review) or **delete**.

> Note: because of a 2025 Google API change, the whole-library read API is gone — you
> pick the surviving photos manually. See [docs/SETUP.md](docs/SETUP.md).

## Quick start

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download) and a Google OAuth
client — see **[docs/SETUP.md](docs/SETUP.md)** for the one-time Google Cloud setup and
`credentials.json`.

Caching and the review report are automatic, so the normal flow needs no extra flags:

```bash
# 1. Dry run (the default) — pick surviving photos in the browser. Auto-saves the
#    pick and opens an HTML report to review. Prints the stage/delete commands.
dotnet run -- --onedrive "<Camera Roll folder>" --from 2026-08 --to 2026-08

# 2. Stage the candidates for review (reuses the cached pick — no re-picking)
dotnet run -- --onedrive "<Camera Roll folder>" --from 2026-08 --to 2026-08 --stage

# 3. Once you're happy with the staged folder, delete it yourself,
#    or re-run with --delete (permanent).
```

Work **one month at a time** so the manual pick stays manageable, and prefer
`--dry-run`/`--stage` over `--delete`.

## Documentation

- **[docs/SETUP.md](docs/SETUP.md)** — Google Cloud / OAuth setup
- **[docs/USAGE.md](docs/USAGE.md)** — full command-line reference and workflows
- **[Website](https://mykolad.github.io/photo-recull/)**, with the
  [privacy policy](https://mykolad.github.io/photo-recull/privacy) and
  [terms of service](https://mykolad.github.io/photo-recull/terms)

## Tests

```bash
dotnet test
```

The two sample images under `PhotoRecull.Tests/TestData/` are neutral stock photos
stamped with only the EXIF the tests need (no location or personal data).

## License

MIT — see [LICENSE](LICENSE).
