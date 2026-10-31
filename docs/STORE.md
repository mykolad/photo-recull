# Publishing to the Microsoft Store

The Windows app ships as an MSIX package. Normal builds stay unpackaged, so
`dotnet run` keeps working; a Store build adds `-p:PhotoRecullPackage=true`.

## One-time setup (the publisher does these)

1. **Partner Center account.** Register as an individual developer at
   [partner.microsoft.com](https://partner.microsoft.com/dashboard/registration); the
   registration page shows the current terms.
2. **Reserve the name.** In Partner Center: Apps and games › New product › MSIX or PWA app ›
   reserve **PhotoRecull**.
3. **Product identity.** Product management › Product identity shows three values. Put
   them in `src/PhotoRecull.App/Package.appxmanifest`, in place of the placeholders:
   - `Package/Identity/Name`
   - `Package/Identity/Publisher` (starts with `CN=`)
   - `Package/Properties/PublisherDisplayName`

   They're public (every Store listing shows them), so they belong in the repository.
4. **Google OAuth client for the Store build.** The repository secrets
   `PHOTORECULLGOOGLECLIENTID` and `PHOTORECULLGOOGLECLIENTSECRET` hold the Desktop OAuth
   client that Store builds have built in. The package workflow fails if they're missing.
5. **Google consent screen in production.** In Google Cloud Console › Google Auth Platform ›
   Audience, publish the app. While it's in *Testing*, only listed test users can sign
   in, and their sign-in expires after 7 days. Google may ask for verification of the
   Photos Picker scope. Have the [privacy policy](https://mykolad.github.io/photo-recull/privacy)
   and [terms](https://mykolad.github.io/photo-recull/terms) links on the consent screen's
   Branding page.

## Build the package

On GitHub: Actions › **Store package** › Run workflow, with a version like `1.0.0`. Or
push a tag `v1.0.0`. The workflow stamps the version (the Store wants the fourth part to
be 0), builds with the built-in Google client, and uploads the `.msix` as an artifact.

Locally, with the client in `GoogleOAuthClient.local.props`:

```bash
dotnet build src/PhotoRecull.App -c Release -p:Platform=x64 -p:PhotoRecullPackage=true
```

The package lands in `artifacts/store/`.

## Try the package before uploading

The Store signs what you upload, so the built package isn't signed. To run it locally:

1. Turn on **Developer Mode** (Settings › System › For developers).
2. Build the package as above, then register the build output in PowerShell:
   ```powershell
   Add-AppxPackage -Register src\PhotoRecull.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\AppxManifest.xml
   ```
3. Start PhotoRecull from the Start menu. The packaged app keeps its own settings and
   library, apart from a `dotnet run` copy, so it starts at Setup.
4. Remove it afterwards: Settings › Apps › Installed apps › PhotoRecull › Uninstall.

Check that Setup, Google sign-in, a cleanup with a saved pick, Stage, Review staged and
History work.

## Submission

In Partner Center, create a submission for PhotoRecull:

- **Pricing and availability:** Free; markets as you like.
- **Properties:**
  - Category: *Photo & video*.
  - Privacy policy URL: `https://mykolad.github.io/photo-recull/privacy`.
  - Website: `https://mykolad.github.io/photo-recull/`.
  - Support contact: an email address or the repository's issues page.
- **Age ratings:** the questionnaire. PhotoRecull has no user-generated content, chat,
  purchases or location sharing, so it should rate for everyone.
- **Packages:** upload the `.msix` from the workflow.
- **Store listing (English):** see below. Screenshots should show your own photos: take
  them at 1366×768 or larger, of Library, the Review timeline, Review staged, and History.
- **Submission options › Restricted capabilities:** the package uses `runFullTrust`, which
  every desktop (Win32) app needs. Partner Center may ask why: *"PhotoRecull is a desktop
  app that reads, moves and deletes photos in folders the user chooses (a OneDrive Camera
  Roll, a Synology NAS folder)."*

## Store listing text

**Product name:** PhotoRecull

**Short description:**
Repeat your Google Photos culls on your OneDrive and Synology Photos backups.

**Description:**

> iPhone photos often back up to Google Photos and to OneDrive or a Synology NAS. Deleting a
> photo in Google Photos doesn't remove the other copies, so over the years they fill up
> with photos you already culled. PhotoRecull finds them.
>
> Pick the photos you still have in Google Photos, with Google's own Photos Picker, for a
> date range. PhotoRecull scans your OneDrive Camera Roll and Synology Photos backup for the
> same days, matches photos by their capture time, and shows the ones you deleted from
> Google Photos in a timeline, dimmed and ready to remove. Bursts are stacked, Live Photos
> stay with their video, and the same photo on both backups shows once.
>
> Nothing is removed without you: move candidates to a stage folder for a last look, keep
> any you want, then delete them. On OneDrive they go to the Recycle Bin. History records
> every stage and delete.
>
> Everything runs on your PC. PhotoRecull reads only the capture times of the photos you
> pick in Google Photos; it never downloads or changes them.

**Features:**
- Finds OneDrive and Synology Photos copies of photos you deleted from Google Photos
- One timeline for the whole range, with photos to remove dimmed
- Bursts stacked like in Google Photos; Live Photos kept with their video
- Stage first, review, then delete; move back anything you want to keep
- Recycle Bin for OneDrive, with clear warnings for network folders
- History of every stage and delete
- Runs locally: only capture times are read from Google Photos

**Search terms:** Google Photos, OneDrive, Synology, duplicates, cleanup, iPhone, photos

## Later

- An Arm64 package: Windows on Arm runs the x64 package under emulation for now.
- Releases need a higher version each time (1.0.1, 1.1.0, …).
