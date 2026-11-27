---
title: Deleting from a NAS
description: Turn on your NAS's recycle bin before PhotoRecull deletes photos from it, so a delete can be undone.
---

# Deleting from a NAS

Deleting photos from a folder on your PC sends them to the Windows Recycle Bin. Windows only
deletes a file for good when it can't recycle it (say, it's bigger than the Recycle Bin), and
then it asks first. The command-line version's `--delete` is always permanent.

Deleting from a NAS over the network skips the Windows Recycle Bin: the NAS has its own, and
it only catches deleted files when it's turned on for the shared folder. Turn it on before
your first delete there:

- **Synology:** Control Panel › Shared Folder › select the folder › Edit › *Enable Recycle Bin*.
- **QNAP:** *Network Recycle Bin*, in the Control Panel.
- **Another NAS:** look for a recycle bin, or a trash, for shared folders in its settings.

## What PhotoRecull checks

Before it deletes from a NAS, PhotoRecull warns you that the Windows Recycle Bin can't help,
and looks for the NAS's recycle-bin folder (`#recycle` on Synology, `@Recycle` on QNAP) at
the root of the shared folder. After the delete, it shows you that folder. Finding it shows the recycle bin was turned on at some point, not that it
still is, so check the setting on the NAS itself.

The safest way is to stage first: the photos move to a review folder on the NAS, nothing is
deleted, and you can move any of them back.

[← Back to PhotoRecull](./)
