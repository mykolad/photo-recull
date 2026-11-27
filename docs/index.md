---
title: PhotoRecull
description: Deleted it from Google Photos? PhotoRecull removes it from OneDrive, your NAS and your other backups too.
---

<div class="hero">
  <img src="assets/logo-256.png" alt="" width="88" height="88">
  <h1>PhotoRecull</h1>
  <p class="tagline">Deleted it from Google Photos? Now it's gone from your other backups too.</p>
  <p>
    Your iPhone backs up to Google Photos <strong>and</strong> to OneDrive, a NAS or another
    folder. You tidy up in Google Photos, but every photo you deleted there stays in your
    other copies. PhotoRecull finds them and clears them out, so your backups match the
    library you actually kept.
  </p>
  <p class="actions">
    <span class="button button-primary">Coming soon to the Microsoft Store</span>
    <a class="button" href="#how-it-works">How it works</a>
    <a class="button" href="#pricing">Pricing</a>
  </p>
</div>

<figure class="screenshot">
  <img src="assets/screenshots/review.jpg" alt="PhotoRecull's review timeline: photos deleted from Google Photos are dimmed and marked to remove, bursts are stacked" width="1600" height="994">
  <figcaption>Every photo you deleted from Google Photos, dimmed and marked to remove, before anything happens.</figcaption>
</figure>

## Why not just do it by hand?

Because the same photo has a different name in every backup. Your iPhone and Google Photos
call it one thing; OneDrive renames it after the moment it was taken, in UTC, to the
millisecond; Synology Photos keeps the name but files it in a folder per month:

<div class="same-photo">
  <div><span>iPhone and Google Photos</span><code>IMG_4821.HEIC</code></div>
  <div><span>OneDrive</span><code>20261025_092323544_iOS.heic</code></div>
  <div><span>Synology Photos</span><code>2026\10\IMG_4821.HEIC</code></div>
</div>

So there's nothing to search for. Finding the copies of the photos you deleted means
comparing capture times, photo by photo, across hundreds of photos a month. A burst is a
dozen near-identical files, and a Live Photo is a photo and a video that must go together.
PhotoRecull reads each photo's capture time and matches it to Google Photos for you.

## Why PhotoRecull

<div class="features">
  <div>
    <h3>Your Google Photos cull, repeated</h3>
    <p>You pick the photos you kept in Google's own Photos Picker. Any copy in your other
    backups with no match is one you deleted, so it's marked to go.</p>
  </div>
  <div>
    <h3>You review everything first</h3>
    <p>A timeline shows every photo, newest first, with the ones to remove dimmed. Spot one
    you forgot to pick? Press <strong>Keep</strong>.</p>
  </div>
  <div>
    <h3>Safe by default</h3>
    <p>Stage first: candidates move to a review folder and nothing is deleted. Changed your
    mind? Move them back. Delete only when you're sure.</p>
  </div>
  <div>
    <h3>Bursts and Live Photos understood</h3>
    <p>A burst is one stack: keep your favourite frame and the rest go. A Live Photo's video
    always goes with its photo.</p>
  </div>
  <div>
    <h3>Any backup folder</h3>
    <p>OneDrive's Camera Roll, Synology Photos or another NAS, or any folder File Explorer
    can open, whichever app backs up to it. Add every one and clean them in one go.</p>
  </div>
  <div>
    <h3>Private by design</h3>
    <p>PhotoRecull runs on your PC. It uses only the capture time of the photos you pick,
    never downloads them, and has no servers or analytics.</p>
  </div>
</div>

<figure class="screenshot">
  <img src="assets/screenshots/burst.jpg" alt="An expanded burst: one frame stays, three go" width="1012" height="270">
  <figcaption>Bursts open up so you can see which frame you kept.</figcaption>
</figure>

## How it works

<ol class="steps">
  <li><strong>Pick.</strong> Choose a month and select the photos you still have in Google's
  Photos Picker, in your browser.</li>
  <li><strong>Scan.</strong> PhotoRecull reads the capture time of each iPhone photo in your
  backup folders: OneDrive, a NAS, or any other.</li>
  <li><strong>Review.</strong> Photos with no match were deleted from Google Photos. Check them
  in the timeline and keep anything you want.</li>
  <li><strong>Clean up.</strong> Stage them to a review folder, or delete them.</li>
</ol>

<figure class="screenshot">
  <video src="assets/screenshots/cleanup-flow.mp4" poster="assets/screenshots/cleanup-flow-poster.jpg" width="1600" height="1068" controls autoplay loop muted playsinline aria-label="A cleanup from start to finish: pick, check the range, scan, review, stage, a last look at the staged photos, and History"></video>
  <figcaption>A month's cleanup, start to finish, on a demo library. The Library shows which months are cleaned up.</figcaption>
</figure>

## Pricing

<div class="pricing">
  <div>
    <h3>Free trial</h3>
    <p class="price">Free</p>
    <p>Pick, scan and review any month, as often as you like, and stage and delete your
    first cleanup. See exactly what PhotoRecull finds before you pay anything.</p>
  </div>
  <div>
    <h3>PhotoRecull</h3>
    <p class="price">€9.99 <span>once</span></p>
    <p>Stage and delete in every cleanup, every month after. One purchase in the Microsoft
    Store, for every PC you sign in to with your Microsoft account. No subscription.</p>
  </div>
</div>

**Why pay for it?** Every photo you deleted from Google Photos is still taking up space in
your other backups, and that space costs money: a bigger OneDrive plan every month, or
disks in your NAS. Years of photos you deleted add up to gigabytes in every backup, so a
one-time €9.99 can pay for itself in the storage it gives back.

**Why is an open-source app paid?** Buying the Store app is how you support PhotoRecull's
development. The code stays open source under the MIT License, and the command-line
version stays free. The Store app is the one that's ready to use: Google sign-in built in,
nothing to set up, and updates through the Store.

## Questions

**Do I have to select every photo I kept, by hand?**
Yes, in Google's own Photos Picker. Since 2025 Google no longer lets apps read your whole
library, so you show PhotoRecull what you kept. Working one month at a time keeps each pick
short, PhotoRecull remembers every pick, and the review catches anything you forgot to
select: press **Keep** on it.

**Does PhotoRecull change anything in Google Photos?**
No. It only has read access to the photos you select in the Photos Picker, and of those it
uses only the capture time.
See the [privacy policy](privacy.md).

**What do I need?**
Windows 10 or 11, and iPhone photos backed up to Google Photos and to at least one other
folder: OneDrive, a NAS such as Synology, or any folder File Explorer can open.

**Can I undo it?**
Yes, if you stage first: photos move to a review folder and nothing is deleted, so you can
move any back. Deleting from your PC sends photos to the Recycle Bin. Deleting from a NAS
skips the Windows Recycle Bin, so turn on the NAS's own recycle bin first:
[how to, and what PhotoRecull checks](nas-recycle-bin.md).

**What about photos from other phones or cameras?**
By default PhotoRecull only touches iPhone photos: an iPhone saves the time zone with its
photos, so they match Google Photos exactly. You can turn that off for any folder to include
other phones and cameras. A photo saved without its time zone (some cameras, older iPhones)
stays whenever one of the photos you kept could be it, in any time zone, so a burst from
such a camera stays whole when you kept one frame. It's only removed when none of the
photos you kept could be it.

**Is it open source?**
Yes, under the MIT License, and the command-line version is free. Buying the Store app
supports its development; see [Pricing](#pricing) and [Open source](developers.md).

<footer class="site-links">
  <a href="privacy">Privacy policy</a> ·
  <a href="terms">Terms of service</a> ·
  <a href="developers">Open source</a> ·
  <a href="mailto:photorecull-support@googlegroups.com">Contact</a>
  <p>PhotoRecull is an independent project and isn't affiliated with or endorsed by Google,
  Microsoft, Synology or QNAP. Screenshots show demo photos from Unsplash.</p>
</footer>
