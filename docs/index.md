---
title: PhotoRecull
description: Deleted it from Google Photos? PhotoRecull removes it from OneDrive and Synology Photos too.
---

<div class="hero">
  <img src="assets/logo-256.png" alt="" width="88" height="88">
  <h1>PhotoRecull</h1>
  <p class="tagline">Deleted it from Google Photos? Now it's gone from your other backups too.</p>
  <p>
    Your iPhone backs up to Google Photos <strong>and</strong> to OneDrive or a Synology NAS.
    You tidy up in Google Photos, but every photo you deleted there stays in your other
    copies. PhotoRecull finds them and clears them out, so your backups match the library
    you actually kept.
  </p>
  <p class="actions">
    <span class="button button-primary">Coming soon to the Microsoft Store</span>
    <a class="button" href="#how-it-works">How it works</a>
  </p>
</div>

<figure class="screenshot">
  <img src="assets/screenshots/review.jpg" alt="PhotoRecull's review timeline: photos deleted from Google Photos are dimmed and marked to remove, bursts are stacked" width="1600" height="994">
  <figcaption>Every photo you deleted from Google Photos, dimmed and marked to remove, before anything happens.</figcaption>
</figure>

## Why PhotoRecull

<div class="features">
  <div>
    <h3>Your Google Photos cull, repeated</h3>
    <p>You pick the photos you kept in Google's own Photos Picker. Any copy in OneDrive or
    Synology Photos with no match is one you deleted, so it's marked to go.</p>
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
    <h3>OneDrive and Synology Photos</h3>
    <p>Works with your OneDrive Camera Roll and with Synology Photos' mobile backup folder on
    your NAS, one at a time or both in one cleanup.</p>
  </div>
  <div>
    <h3>Private by design</h3>
    <p>PhotoRecull runs on your PC. Of the photos you pick it uses only their capture time,
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
  OneDrive or Synology folder.</li>
  <li><strong>Review.</strong> Photos with no match were deleted from Google Photos. Check them
  in the timeline and keep anything you want.</li>
  <li><strong>Clean up.</strong> Stage them to a review folder, or delete them.</li>
</ol>

<figure class="screenshot">
  <img src="assets/screenshots/library.png" alt="PhotoRecull's Library: a year's coverage bar and a card for each cleanup" width="1600" height="986">
  <figcaption>The Library shows which months are cleaned up, and how much space each cleanup freed.</figcaption>
</figure>

## Questions

**Does PhotoRecull change anything in Google Photos?**
No. It only has read access to the photos you select in the Photos Picker, and of those it
uses only the capture time.
See the [privacy policy](privacy.md).

**What do I need?**
Windows 10 or 11, and iPhone photos backed up to Google Photos and to OneDrive or Synology
Photos.

**Is it open source?**
Yes, under the MIT License. There's also a command-line version.
See [Open source](developers.md).

<footer class="site-links">
  <a href="privacy">Privacy policy</a> ·
  <a href="terms">Terms of service</a> ·
  <a href="developers">Open source</a> ·
  <a href="mailto:photorecull-support@googlegroups.com">Contact</a>
  <p>PhotoRecull is an independent project and isn't affiliated with or endorsed by Google,
  Microsoft or Synology. Screenshots show demo photos from Unsplash.</p>
</footer>
