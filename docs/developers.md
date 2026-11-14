---
title: Open source
---

# Open source

PhotoRecull is open source under the [MIT License](https://github.com/mykolad/photo-recull/blob/master/LICENSE).
The code, issues and releases are on GitHub:
[github.com/mykolad/photo-recull](https://github.com/mykolad/photo-recull).

## Command-line app

Besides the Windows app there's a command-line version, run with the .NET SDK. It does the
same cleanup, one date range at a time, and writes an HTML report to review before anything
moves.

- [Setup](SETUP.md): the one-time Google Cloud project and OAuth client it signs in with
- [Usage](USAGE.md): every option, and the recommended dry run → stage → delete workflow

## Building from source

The Windows app is WinUI 3 on .NET 10. Builds from source need their own Google OAuth client;
the [Setup](SETUP.md#windows-app-built-in-google-client) guide covers it. The
[README](https://github.com/mykolad/photo-recull#readme) has the repository layout and how to
run the tests.

## Feedback

Found a bug or have an idea? Open an
[issue](https://github.com/mykolad/photo-recull/issues), or email
[photorecull-support@googlegroups.com](mailto:photorecull-support@googlegroups.com).

[← Back to PhotoRecull](./)
