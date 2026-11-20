# Publishing to the Microsoft Store

The Windows app ships as an MSIX package. Normal builds stay unpackaged, so
`dotnet run` keeps working; a Store build adds `-p:PhotoRecullPackage=true`.

## One-time setup (the publisher does these)

1. **Store developer account.** Register at
   [storedeveloper.microsoft.com](https://storedeveloper.microsoft.com/en-US/onboarding)
   with your Microsoft personal account, as an **Individual developer** (free; the Store
   shows your own name as the publisher). It verifies your identity with a government ID
   and a selfie. Partner Center's own Programs page doesn't open Apps and games for an
   individual.
   - *"Your account is in a deactivated state"* means the Microsoft account has an old
     Partner Center registration that lapsed. Email reportap@microsoft.com to reactivate
     it, with the account's email and its Seller ID (Account settings › Identifiers).
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
5. **Google consent screen in production and verified** (done 20 November 2026). Google
   Cloud Console › Google Auth Platform › Audience shows *In production*, and the
   Verification centre shows the branding and data access verified, so users see
   PhotoRecull's name and logo with no "unverified app" warning. Adding a scope, or
   changing the app's name or logo, needs a new verification. The consent screen links
   to the [privacy policy](https://photorecull.com/privacy) and
   [terms](https://photorecull.com/terms) on `photorecull.com`, a domain verified in
   Search Console: Google rejected `mykolad.github.io` as a homepage.

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
2. Build the package as above, then register the build output in PowerShell. With
   `dotnet build` the layout is in the `win-x64` folder itself; a Visual Studio build may
   put it in an `AppX` subfolder, so this finds whichever there is:
   ```powershell
   $manifest = Get-ChildItem src\PhotoRecull.App\bin\x64\Release -Recurse -Filter AppxManifest.xml | Select-Object -First 1
   Add-AppxPackage -Register $manifest.FullName
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
  - Privacy policy URL: `https://photorecull.com/privacy`.
  - Website: `https://photorecull.com/`.
  - Support contact: an email address or the repository's issues page.
- **Age ratings:** the questionnaire. PhotoRecull has no user-generated content, chat,
  purchases or location sharing, so it should rate for everyone.
- **Packages:** upload the `.msix` from the workflow.
- **Store listing (English):** see below. Screenshots must be 1366×768 or larger. Use the
  demo library of Unsplash photos, as the website does, never your own photos: the site's
  `docs/assets/screenshots/review.jpg` and `library.png` qualify; add Review staged and
  History taken the same way.
- **Submission options › Restricted capabilities:** the package uses `runFullTrust`, which
  every desktop (Win32) app needs. Partner Center may ask why: *"PhotoRecull is a desktop
  app that reads, moves and deletes photos in folders the user chooses, such as a OneDrive
  Camera Roll or a folder on a NAS."*

## Store listing text

**Product name:** PhotoRecull

**Short description:**
Deleted it from Google Photos? PhotoRecull removes it from OneDrive, your NAS and your other backups too.

**Description:**

> Your iPhone backs up to Google Photos and to OneDrive, a NAS or another folder. You tidy
> up in Google Photos, but every photo you delete there stays in your other copies, and over
> the years they fill up with photos you already culled. PhotoRecull finds them.
>
> Pick the photos you still have in Google Photos, for a month or any date range, in
> Google's own Photos Picker. PhotoRecull scans your backup folders for the same days,
> matches photos by their capture time, and shows the ones you deleted from Google Photos
> in a timeline, dimmed and ready to remove. Bursts are stacked, Live Photos stay with their
> video, and the same photo in two backups shows once.
>
> Nothing is removed without you: move the photos to a stage folder for a last look, keep
> any you want, then delete them. PhotoRecull uses the Recycle Bin when Windows can, and
> History records every stage and delete.
>
> Everything runs on your PC. PhotoRecull reads only the capture times of the photos you
> pick in Google Photos; it never downloads or changes them, and has no servers or
> analytics.

**Features:**
- Finds copies of photos you deleted from Google Photos in your other backups
- Works with OneDrive's Camera Roll, Synology Photos or another NAS, or any folder
- One timeline for the whole range, with the photos to remove dimmed
- Bursts stacked like in Google Photos; Live Photos kept with their video
- Stage first, review, then delete; move back anything you want to keep
- Only iPhone photos by default, so other cameras' photos are left alone
- History of every stage and delete
- Runs on your PC: only capture times are read from Google Photos

**Search terms:** Google Photos, OneDrive, Synology, NAS, backup, cleanup, iPhone

## Later

- An Arm64 package: Windows on Arm runs the x64 package under emulation for now.
- Releases need a higher version each time (1.0.1, 1.1.0, …).
