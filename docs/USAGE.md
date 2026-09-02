# Usage

```bash
dotnet run -- --onedrive <folder> [options] <mode>
```

You must supply `--onedrive` and exactly one **mode** (`--dry-run`, `--stage`, or
`--delete`).

## Parameters

### Required

| Flag | Description |
|------|-------------|
| `--onedrive <folder>` | Path to your local (synced) OneDrive Camera Roll folder. |

### Mode (pick one)

| Flag | Description |
|------|-------------|
| `--dry-run` | Show candidates and write reports; change nothing. |
| `--stage [dir]` | Move candidates to a folder for manual review. `dir` is optional and defaults to `_staged_<range>` (e.g. `_staged_2026-08`). Nothing is deleted — review the folder, then delete it yourself. |
| `--delete` | **Permanently** delete candidates. Prompts for confirmation unless `--yes`. |

### Date range

| Flag | Description |
|------|-------------|
| `--from <date>` | Start of range: `YYYY-MM` or `YYYY-MM-DD`. `YYYY-MM` expands to the 1st. |
| `--to <date>` | End of range: `YYYY-MM` or `YYYY-MM-DD`. `YYYY-MM` expands to the last day. |

The date range filters the **OneDrive** side. The Google Photos Picker has no date
filter, so in the browser you select the surviving photos for that range yourself.
Omit both flags to scan the entire library (not recommended — the manual pick becomes
huge). Process one month at a time.

### Picker cache

Caching is **on by default**: your pick is saved to `<range>.json` (e.g.
`2026-08.json`) and reused automatically on later runs, so the picker and Google
sign-in are skipped. This is what lets `--dry-run` → `--stage` → `--delete` run from a
single pick with no re-picking.

| Flag | Description |
|------|-------------|
| `--cache <file.json>` | Override the default cache file path. |
| `--repick` | Ignore the cache and open the picker again, overwriting it. Use when your Google Photos have changed. |
| `--no-cache` | Don't save or reuse any cache — open the picker every run. |

The cache stores only timestamps (no image data). If a cache's date range differs from
`--from`/`--to`, the tool warns you to `--repick`.

### Authentication

| Flag | Description |
|------|-------------|
| `--credentials <file>` | Path to the Google OAuth `credentials.json`. Default: `credentials.json` in the working directory. Not needed when reusing a `--cache`. |
| `--browser <name\|path>` | Browser used to open the Google Photos picker: `chrome`, `edge`, `firefox`, an executable name, or a full path. Default: system default. Handy to sign in to Google via a browser that isn't your default. The HTML report always opens in your default browser. |

### Output

| Flag | Description |
|------|-------------|
| `--report [file.html]` | Generate a visual HTML review with thumbnails (HEIC decoded to JPEG). **Generated automatically on `--dry-run`.** `file` is optional and defaults to `<range>.html` (e.g. `2026-08.html`). Opens in your default browser. Cards are clickable to open the original file. |
| `--no-report` | Suppress the automatic dry-run report. |
| `--output <file.csv>` | Write the candidate list to CSV. |

### Confirmation

| Flag | Description |
|------|-------------|
| `--yes`, `--force` | Skip the confirmation prompt for `--stage` / `--delete`. With `--delete` this removes files with no prompt — use carefully. |

### Help

| Flag | Description |
|------|-------------|
| `--help`, `-h` | Show built-in help. |

## Recommended workflow (safe → destructive)

Caching and the review report are on by default, so a normal flow needs no extra flags:

```bash
# 1. Pick once. Auto-saves the pick to 2026-08.json and opens 2026-08.html to review.
dotnet run -- --onedrive "C:\...\Camera Roll" --from 2026-08 --to 2026-08 \
  --dry-run --browser chrome

# 2. Stage candidates. Reuses 2026-08.json automatically — no re-picking.
dotnet run -- --onedrive "C:\...\Camera Roll" --from 2026-08 --to 2026-08 --stage

# 3. Inspect _staged_2026-08, drag anything wrong back into Camera Roll,
#    then delete the staged folder yourself — or, if fully confident:
dotnet run -- --onedrive "C:\...\Camera Roll" --from 2026-08 --to 2026-08 --delete --yes
```

After a dry run with candidates, the tool prints the exact stage command, pre-filled with
your paths, range, and cache.

## How matching works

- OneDrive iPhone photos are detected by filename (`YYYYMMDD_hhmmssSSS_iOS.ext`) or by
  EXIF (`Make=Apple`, `Model` contains `iPhone`). Non-iPhone files are skipped.
- Capture time is normalized to UTC: OneDrive filenames encode UTC directly; EXIF times
  use `DateTimeOriginal` + `OffsetTimeOriginal` + `SubSecTimeOriginal`.
- Matching is **one-to-one** within a ±2s tolerance: each Google photo keeps at most one
  OneDrive file (the nearest). Burst frames you culled in Google Photos therefore show
  up as removal candidates instead of all being kept.
