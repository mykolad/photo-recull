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
3. **Product identity** (done). Product management › Product identity shows three values,
   and `src/PhotoRecull.App/Package.appxmanifest` has them:
   - `Package/Identity/Name`: `53119mykolad.PhotoRecull`
   - `Package/Identity/Publisher`: `CN=15C476D5-4965-4FA2-9B11-2A4E6839B433`
   - `Package/Properties/PublisherDisplayName`: `mykolad`

   The package family name is `53119mykolad.PhotoRecull_brc5zbkqaxtwe`. They're public
   (every Store listing shows them), so they belong in the repository.
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

## Paid, with a free trial

The Store build is a paid app with an unlimited free trial; every other build (`dotnet run`,
a fork, the command-line app) is the open-source app and is never limited. The Store build
gets its license from the Microsoft Store (`Services/LicenseSources/StoreLicenseSource.cs`),
which `-p:PhotoRecullPackage=true` compiles in instead of the open-source one.

- **What the trial does:** picking, scanning and review without limit. Stage and Delete work
  in one cleanup, the first one a stage or delete starts in, which stays free to finish
  (deleting what it staged, moving photos back, Run again). Stage or Delete in any other
  cleanup asks the user to buy first; nothing moves until they do.
  The rule is `PhotoRecull.Core.Licensing.TrialAllowance`. Which cleanup is the free one
  comes from when its first stage or delete started (saved with the cleanup), so picking
  again, or runs after a purchase that was refunded, never move it. Deleting from Review
  staged asks too; moving photos back never does.
- **How the app knows:** `Services/StoreLicense.cs` holds what a trial may do, and asks the
  build's license source. The Store build's, `StoreLicenseSource`, asks the Store for the
  app's license (`StoreContext.GetAppLicenseAsync`): bought means active and not a trial. Its **Buy**
  buttons, in the purchase prompt and in Settings › PhotoRecull, open the Store's own
  purchase window. If the Store can't be asked at all, the app treats itself as bought,
  rather than hold back someone who paid.
- **Uninstalling** deletes the app's database, so a reinstall gets a new free cleanup. That's
  accepted: it's one month at a time, and the cleanups' history goes with it.
- **Trying the screens without the Store:** run a build from source with the environment
  variable `PHOTORECULL_SIMULATE_TRIAL=1`. It acts as a trial with a made-up price, and its
  Buy succeeds after half a second:
  ```powershell
  $env:PHOTORECULL_SIMULATE_TRIAL = '1'; dotnet run --project src/PhotoRecull.App
  ```
- **Testing the trial in the Store build:** the same variable makes the Store build act as
  unbought, whatever its license says: on a PC or account that already owns the app, or
  with a package registered from the build output. Its price and Buy stay the Store's own,
  so the variable can only hold the app back, never unlock it. Set it for the user, then
  start PhotoRecull from the Start menu:
  ```powershell
  [Environment]::SetEnvironmentVariable('PHOTORECULL_SIMULATE_TRIAL', '1', 'User')
  ```
  Remove it afterwards with the same command and `$null` in place of `'1'`.

## Build the package

On GitHub: Actions › **Store package** › Run workflow, with a version like `1.0.0`. Or
push a tag `v1.0.0`. The workflow stamps the version (the Store wants the fourth part to
be 0), builds an x64 and an Arm64 package with the built-in Google client, bundles them
into `PhotoRecull_<version>.msixbundle`, and uploads the bundle and both packages as an
artifact. Upload the bundle: the Store gives each PC the package for its architecture, so
Windows on Arm runs PhotoRecull natively.

Locally, with the client in `GoogleOAuthClient.local.props`, one architecture at a time
(`x64` or `ARM64`):

```bash
dotnet build src/PhotoRecull.App -c Release -p:Platform=x64 -p:PhotoRecullPackage=true
```

The package lands in `artifacts/store/`.

## Try the package before uploading

The Store signs what you upload, so the built package isn't signed. To run it locally:

1. Turn on **Developer Mode** (Settings › System › For developers).
2. Build the package as above, for this PC's architecture (`x64`, or `ARM64` on Windows on
   Arm). The package depends on the Windows App SDK runtime, which the Store installs with
   the app; locally, install it once from the build output. Set `$arch` to the platform
   you built:
   ```powershell
   $arch = 'x64'   # or 'arm64'
   Get-ChildItem artifacts\store -Recurse -Filter Microsoft.WindowsAppRuntime.2.msix | Where-Object { $_.Directory.Name -eq $arch } | ForEach-Object { Add-AppxPackage $_.FullName }
   ```
3. Register the build output in PowerShell. With
   `dotnet build` the layout is in the `win-x64` (or `win-arm64`) folder itself; a Visual Studio build may
   put it in an `AppX` subfolder, so this finds whichever there is:
   ```powershell
   $manifest = Get-ChildItem src\PhotoRecull.App\bin\$arch\Release -Recurse -Filter AppxManifest.xml | Select-Object -First 1
   Add-AppxPackage -Register $manifest.FullName
   ```
4. Start PhotoRecull from the Start menu. The packaged app keeps its own settings and
   library, apart from a `dotnet run` copy, so it starts at Setup.
5. Remove it afterwards: Settings › Apps › Installed apps › PhotoRecull › Uninstall.

Check that Setup, Google sign-in, a cleanup with a saved pick, Stage, Review staged and
History work. A package registered this way has no Store license, so it may act as an
unbought trial; if it acts as bought, set `PHOTORECULL_SIMULATE_TRIAL` (above). As a trial,
Stage and Delete in a second cleanup should ask to buy, and Buy should fail with a message
(the app isn't in the Store yet), never stage or delete. Settings › PhotoRecull shows the
trial.

After the app is published, check the purchase for real with the Store's copy: install it
from the Store on a PC (or a Microsoft account) that hasn't bought it, use the free cleanup,
then buy from the prompt in a second one. Partner Center's promotional codes let you
try a purchase without paying.

## Submission

In Partner Center, create a submission for PhotoRecull:

- **Pricing and availability:**
  - **Pricing:** Base price **9.99 EUR** (or a launch price, such as 5.99 EUR, scheduled to
    end, under Sale pricing). Partner Center sets the other markets' prices from it.
  - **Free trial:** **Unlimited**. The app itself limits the trial to one cleanup's stage
    and delete; a time limit would end the free scan and review too.
  - **Markets:** as you like.
- **Properties:**
  - Category: *Photo & video*.
  - Privacy policy URL: `https://photorecull.com/privacy`.
  - Website: `https://photorecull.com/`.
  - Support contact: an email address or the repository's issues page.
- **Age ratings:** the questionnaire. PhotoRecull has no user-generated content, chat or
  location sharing, so it should rate for everyone. Its only purchase is buying the app
  itself through the Store, with no in-app purchases (add-ons): answer the purchases
  questions that way.
- **Packages:** upload the `.msixbundle` from the workflow.
- **Store listing (English):** see below. Screenshots must be 1366×768 or larger. Use the
  demo library of Unsplash photos, as the website does, never your own photos: the site's
  `docs/assets/screenshots/review.jpg` and `library.png` qualify; add Review staged and
  History taken the same way (still to do).
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
>
> Try it free: pick, scan and review as much as you like, and stage and delete in your first
> cleanup. Buy PhotoRecull once to clean up every month after: no subscription, and it can
> pay for itself in the storage it frees. PhotoRecull is open source, and buying it
> supports its development.

**Features:**
- Finds copies of photos you deleted from Google Photos in your other backups
- Works with OneDrive's Camera Roll, Synology Photos or another NAS, or any folder
- One timeline for the whole range, with the photos to remove dimmed
- Bursts stacked like in Google Photos; Live Photos kept with their video
- Stage first, review, then delete; move back anything you want to keep
- Only iPhone photos by default, so other cameras' photos are left alone
- History of every stage and delete
- Runs on your PC: only capture times are read from Google Photos
- Free trial: scan and review without limit, and clean up your first month

**Search terms:** Google Photos, OneDrive, Synology Photos, NAS, backup, cleanup, iPhone

(Partner Center takes up to seven.)

## Later

- Releases need a higher version each time (1.0.1, 1.1.0, …).
