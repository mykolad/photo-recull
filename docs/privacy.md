---
title: Privacy Policy · PhotoRecull
---

# Privacy Policy

*Effective date: 27 October 2026*

PhotoRecull is open-source software that runs entirely on your own computer. It has no
servers, no accounts and no analytics. This policy explains what data it accesses and what
it does with it.

## In short

- PhotoRecull reads only the **capture time** of the Google Photos items you select.
- That data stays on your computer. It's never sent to the developer or anyone else.
- PhotoRecull only talks to Google, to sign you in and to read the items you picked.

## Data from your Google account

**What it accesses.** PhotoRecull asks for one permission:
`https://www.googleapis.com/auth/photospicker.mediaitems.readonly`. It lets the app read
only the items **you select** in the Google Photos Picker. PhotoRecull can't see the rest of
your library, your albums, or anything else in your Google account.

**What it uses.** For each selected item, Google returns basic information such as its
capture time and file name. PhotoRecull uses only the **capture time**, and ignores the
rest. It doesn't download your photos from Google.

**Why.** The capture times are compared with photos in the folders you choose (for example,
your OneDrive Camera Roll) to find copies of photos you've deleted from Google Photos.

**Where it's stored.** On your computer only:

- the capture times, with the date range and when you picked them, in a cache file (for
  example `2026-08.json`) in the folder you run PhotoRecull from;
- the sign-in token Google issues, in a `token_store_picker` folder next to the app.

You can delete these files at any time.

**Sharing.** PhotoRecull doesn't share, sell or transfer Google user data to anyone.

**Limited Use.** PhotoRecull's use and transfer of information received from Google APIs
adheres to the
[Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy),
including the Limited Use requirements.

**Revoking access.** You can remove PhotoRecull's access at any time in your
[Google Account](https://myaccount.google.com/connections). Deleting the
`token_store_picker` folder signs it out on your computer.

## Files on your computer

PhotoRecull reads the folders you point it to: file names and photo metadata such as capture
time and camera make and model. It creates thumbnails for a review report saved on your
computer. It moves or deletes files only when you explicitly choose to stage or delete them.

## Your own Google Cloud project

To use PhotoRecull you create your own Google Cloud project and OAuth client (see
[Setup](SETUP.md)). The app then signs in through your project, and Google's handling of
that is covered by Google's own privacy policy.

## Changes

Changes to this policy are published on this page with a new effective date. The full
history is on [GitHub](https://github.com/mykolad/photo-recull/commits/master/docs/privacy.md).

## Contact

Questions: email [photorecull-support@googlegroups.com](mailto:photorecull-support@googlegroups.com),
or open an issue at
[github.com/mykolad/photo-recull/issues](https://github.com/mykolad/photo-recull/issues).
