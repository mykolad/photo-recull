---
title: PhotoRecull
---

<img src="assets/logo-256.png" alt="PhotoRecull logo" width="96" height="96">

# PhotoRecull

Repeat your Google Photos culls on your other copies: find and remove OneDrive and
Synology Photos copies of photos you've already deleted from Google Photos.

iPhone photos often back up to Google Photos **and** OneDrive or a Synology NAS. Deleting a
photo from Google Photos doesn't remove those copies, so over the years they fill up with
photos you already culled. PhotoRecull finds them.

## How it works

1. You select the photos you **still have** in Google Photos, using Google's own Photos
   Picker in your browser.
2. PhotoRecull scans your OneDrive or Synology Photos folder and reads each iPhone photo's
   capture time.
3. Photos with no matching picked photo were deleted from Google Photos, so they
   become removal candidates.
4. You review them in a report, then move them to a review folder or delete them.

Everything runs on your own computer. See the [privacy policy](privacy.md) for exactly what
PhotoRecull reads from your Google account.

## Get started

- [Setup](SETUP.md): the one-time Google Cloud setup
- [Usage](USAGE.md): command-line reference and the recommended workflow
- [Source code on GitHub](https://github.com/mykolad/photo-recull) (MIT License)

## Legal

- [Privacy policy](privacy.md)
- [Terms of service](terms.md)

PhotoRecull is an independent open-source project and isn't affiliated with or endorsed by
Google or Microsoft.
