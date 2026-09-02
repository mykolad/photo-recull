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

| Flag | Description |
|------|-------------|
| `--cache <file.json>` | On the first run, save the picked Google timestamps here. On later runs, reuse them and **skip the picker** (and Google sign-in). Lets you run dry-run → stage → delete from one pick. |
| `--repick` | Ignore an existing cache and open the picker again, overwriting the cache. Use when your Google Photos have changed. |

The cache stores only timestamps (no image data). If the cache's date range differs
from `--from`/`--to`, the tool warns you to `--repick`.

### Authentication

| Flag | Description |
|------|-------------|
| `--credentials <file>` | Path to the Google OAuth `credentials.json`. Default: `credentials.json` in the working directory. Not needed when reusing a `--cache`. |
| `--browser <name\|path>` | Browser used to open the Google Photos picker: `chrome`, `edge`, `firefox`, an executable name, or a full path. Default: system default. Handy to sign in to Google via a browser that isn't your default. The HTML report always opens in your default browser. |

### Output

| Flag | Description |
|------|-------------|
| `--report [file.html]` | Generate a visual HTML review with thumbnails (HEIC decoded to JPEG). `file` is optional and defaults to `<range>.html` (e.g. `2026-08.html`). Opens automatically. Cards are clickable to open the original file. |
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

```bash
# 1. Pick once, review with thumbnails, and cache the selection
dotnet run -- --onedrive "C:\...\Camera Roll" --from 2026-08 --to 2026-08 \
  --dry-run --report --cache 2026-08.json --browser chrome

# 2. Reuse the cache to stage candidates for review (no re-picking)
dotnet run -- --onedrive "C:\...\Camera Roll" --from 2026-08 --to 2026-08 \
  --stage --cache 2026-08.json

# 3. Inspect _staged_2026-08, drag anything wrong back into Camera Roll,
#    then delete the staged folder yourself — or, if fully confident:
dotnet run -- --onedrive "C:\...\Camera Roll" --from 2026-08 --to 2026-08 \
  --delete --cache 2026-08.json --yes
```

After a dry run with candidates, the tool prints the exact command to proceed with
staging, pre-filled with your paths, range, and cache.

## How matching works

- OneDrive iPhone photos are detected by filename (`YYYYMMDD_hhmmssSSS_iOS.ext`) or by
  EXIF (`Make=Apple`, `Model` contains `iPhone`). Non-iPhone files are skipped.
- Capture time is normalized to UTC: OneDrive filenames encode UTC directly; EXIF times
  use `DateTimeOriginal` + `OffsetTimeOriginal` + `SubSecTimeOriginal`.
- Matching is **one-to-one** within a ±2s tolerance: each Google photo keeps at most one
  OneDrive file (the nearest). Burst frames you culled in Google Photos therefore show
  up as removal candidates instead of all being kept.
