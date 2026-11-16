---
title: Privacy Policy
---

# Privacy Policy

*Effective date: 16 November 2026*

PhotoRecull is open-source software that runs entirely on your own computer. It has no
servers, no accounts and no analytics. This policy explains what data it accesses and what
it does with it.

## In short

- PhotoRecull reads only the **capture time** of the Google Photos items you select.
- That data stays on your computer. It's never sent to the developer or anyone else.
- PhotoRecull only talks to Google, to sign you in and to read the items you picked.
- The sign-in token Google issues is encrypted on your computer.

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

- **Windows app:** the capture times, with the date range and when you picked them, and the
  encrypted sign-in token Google issues, in the app's database (`photorecull.db` in
  `%LOCALAPPDATA%\PhotoRecull`), together with your settings and cleanup history.
- **Command-line app:** the capture times in a cache file (for example `2026-08.json`) in
  the folder you run it from, and the encrypted sign-in token in a `token_store_picker`
  folder next to the app.

You can delete these files at any time.

**Sharing.** PhotoRecull doesn't share, sell or transfer Google user data to anyone.

**Limited Use.** PhotoRecull's use and transfer of information received from Google APIs
adheres to the
[Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy),
including the Limited Use requirements.

**Revoking access.** You can remove PhotoRecull's access at any time in your
[Google Account](https://myaccount.google.com/connections). In the Windows app,
**Settings → Sign out** deletes the sign-in token from your computer and asks Google to
revoke the app's access. For the command-line app, deleting the `token_store_picker` folder
signs it out on your computer.

## How your data is protected

PhotoRecull protects Google user data with these security measures:

- **No copies anywhere else.** PhotoRecull has no servers. Google user data stays on your
  computer and is never sent to the developer or anyone else, so it can't be exposed by a
  breach of an online service.
- **Encryption in transit.** PhotoRecull connects only to Google, always over HTTPS (TLS).
- **Encryption at rest.** The sign-in token Google issues, the data that could be used to
  access your Google account, is encrypted with Windows Data Protection (DPAPI) for your
  Windows user account before it's saved. Another user of the computer, or anyone with a
  copy of the file, can't use it.
- **Access limited to your Windows account.** The Windows app keeps its data in your Windows
  user profile, which other standard users of the computer can't open.
- **Least access.** PhotoRecull asks only for read-only access to the items you select, and
  keeps only their capture times. It never sees your other photos, and never downloads a
  photo, your name, your email address or your contacts.
- **You stay in control.** In the Windows app, signing out deletes the token and asks Google
  to revoke PhotoRecull's access. You can also remove that access in your Google Account, and
  delete the stored data at any time.
- **Open to review.** PhotoRecull's source code is public, so anyone can check how it handles
  your data.

## Files on your computer

PhotoRecull reads the folders you point it to: file names and photo metadata such as capture
time and camera make and model. It creates thumbnails for a review report saved on your
computer. It moves or deletes files only when you explicitly choose to stage or delete them.

## Command-line app and builds from source

The Windows app from the Microsoft Store signs in through PhotoRecull's own Google OAuth
client, so there's nothing to set up. The command-line app, and the Windows app built from
source, sign in through a Google Cloud project and OAuth client you create yourself (see
[Setup](SETUP.md)). Google's handling of that project is covered by Google's own privacy
policy. What PhotoRecull reads and stores is the same either way.

## Changes

Changes to this policy are published on this page with a new effective date. The full
history is on [GitHub](https://github.com/mykolad/photo-recull/commits/master/docs/privacy.md).

## Contact

Questions: email [photorecull-support@googlegroups.com](mailto:photorecull-support@googlegroups.com),
or open an issue at
[github.com/mykolad/photo-recull/issues](https://github.com/mykolad/photo-recull/issues).
