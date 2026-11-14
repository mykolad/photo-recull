# Usage

```bash
dotnet run --project src/PhotoRecull.Cli -- --path <folder> [options] <mode>
```

You must supply `--path`. The **mode** defaults to a dry run; pass `--stage` or
`--delete` to act on the candidates.

## Parameters

### Target

| Flag | Description |
|------|-------------|
| `--path <folder>` | **Required.** The folder your iPhone photos are backed up to. |
| `--target <name>` | What messages call the folder. Default: told from the folder (OneDrive, Synology Photos, the NAS's name, or the folder's own name). |
| `--onedrive <folder>` | Same as `--target onedrive --path <folder>`. |

Any folder works, whichever app backs up to it, as long as it holds the original photo
files. It's read with its subfolders; ones named after dates (`2026\01`, or `2026-01`)
are only read when the range can be in them, with two days' margin for time zones.
PhotoRecull's own stage folders and NAS housekeeping folders (`@eaDir`, `#recycle`,
`#snapshot`) are skipped. For example:

- **OneDrive:** your local (synced) Camera Roll folder, e.g.
  `C:\Users\you\OneDrive\Pictures\Camera Roll`, with the year and month folders OneDrive
  can sort uploads into.
- **A NAS:** a network folder or mapped drive that File Explorer can already open, such
  as Synology Photos' mobile backup, `\\nas\home\Photos\MobileBackup\iPhone`. See
  [Network folders](#network-folders).

### Mode (default: dry run)

| Flag | Description |
|------|-------------|
| *(none)* | **Default.** Dry run — show candidates and write the report; change nothing. |
| `--dry-run` | Explicit dry run (same as the default). |
| `--stage [dir]` | Move candidates to a folder for manual review. `dir` is optional and defaults to `_staged_<range>` (e.g. `_staged_2026-08`). Nothing is deleted — review the folder, then delete it yourself. |
| `--delete` | **Permanently** delete candidates. Prompts for confirmation unless `--yes`. |

After a dry run with candidates, the tool prints the ready-to-run `--stage` and
`--delete` commands, pre-filled with your paths, range, and cache.

### Date range

| Flag | Description |
|------|-------------|
| `--from <date>` | Start of range: `YYYY-MM` or `YYYY-MM-DD`. `YYYY-MM` expands to the 1st. |
| `--to <date>` | End of range: `YYYY-MM` or `YYYY-MM-DD`. `YYYY-MM` expands to the last day. |

The date range filters the **target** side. The Google Photos Picker has no date
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
| `--browser <name\|path>` | Browser used for Google sign-in and the Google Photos picker: `chrome`, `edge`, `firefox`, an executable name, or a full path. Default: system default. Handy to sign in to Google via a browser that isn't your default. The HTML report always opens in your default browser. |

### Output

| Flag | Description |
|------|-------------|
| `--report [file.html]` | Generate a visual HTML review with thumbnails (HEIC decoded to JPEG). **Generated automatically on `--dry-run`.** `file` is optional and defaults to `<range>.html` (e.g. `2026-08.html`). Opens in your default browser. One timeline, newest first and grouped by day (UTC): photos to remove are dimmed and outlined in red next to the ones that stay, and bursts are stacked (click a stack to see its frames). A filter at the top shows all photos, hides the ones to remove, or shows only those. Click a photo to open the original file. |
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
dotnet run --project src/PhotoRecull.Cli -- --onedrive "C:\...\Camera Roll" --from 2026-08 --to 2026-08 \
  --dry-run --browser chrome

# 2. Stage candidates. Reuses 2026-08.json automatically — no re-picking.
dotnet run --project src/PhotoRecull.Cli -- --onedrive "C:\...\Camera Roll" --from 2026-08 --to 2026-08 --stage

# 3. Inspect _staged_2026-08, drag anything wrong back into Camera Roll,
#    then delete the staged folder yourself — or, if fully confident:
dotnet run --project src/PhotoRecull.Cli -- --onedrive "C:\...\Camera Roll" --from 2026-08 --to 2026-08 --delete --yes
```

After a dry run with candidates, the tool prints the exact stage command, pre-filled with
your paths, range, and cache.

For another folder, swap `--onedrive "C:\...\Camera Roll"` for `--path "<folder>"`,
e.g. `--path "\\nas\home\Photos\MobileBackup\iPhone"`.

## Network folders

PhotoRecull reads a NAS as a network folder; it doesn't use Synology's or any other
NAS's API.

- **Folders:** a backup filed in date folders, like Synology Photos', is read a month at
  a time: only the months a cleanup's dates can be in, so a month takes seconds rather
  than a scan of the whole library.
- **Stage folder:** the app stages photos outside the `Photos` folder that Synology Photos
  shows, e.g. `\\nas\home\PhotoRecull staged\2026-08-01_2026-08-31`, so they don't show up
  in Synology Photos again; other folders get one next to them. With the CLI, `--stage`
  defaults to `_staged_<range>` in the current folder; pass a folder on the NAS to keep
  the move on the NAS.
- **Delete:** deleting over the network skips the Windows Recycle Bin. Turn on the NAS's
  own recycle bin for the shared folder to be able to undo it: on Synology, Control
  Panel › Shared Folder › Edit (deleted photos then wait in the share's `#recycle`
  folder); on QNAP, the shared folder's Network Recycle Bin (`@Recycle`).

## How matching works

- OneDrive iPhone photos are detected by filename (`YYYYMMDD_hhmmssSSS_iOS.ext`); other
  photos (such as Synology's `IMG_1234.HEIC`) by EXIF (`Make=Apple`, `Model` contains
  `iPhone`), and videos by their QuickTime metadata. Non-iPhone files are skipped.
- Capture time is normalized to UTC: OneDrive filenames encode UTC directly; EXIF times
  use `DateTimeOriginal` + `OffsetTimeOriginal` + `SubSecTimeOriginal`; videos use the
  time recording started (Apple's `creationdate`).
- A Live Photo's video (`IMG_1234.MOV` next to `IMG_1234.HEIC`) counts as part of its
  photo, as in Google Photos: it's kept, staged or deleted together with the photo.
- Matching is **one-to-one** within a ±2s tolerance: each Google photo keeps at most one
  target file (the nearest). Burst frames you culled in Google Photos therefore show
  up as removal candidates instead of all being kept.
