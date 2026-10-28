# Setup

PhotoRecull reads which photos are still in your Google Photos account through the
**Google Photos Picker API**, which requires a Google OAuth client. This is a one-time
setup.

## 1. Create a Google Cloud project & OAuth client

1. Go to the [Google Cloud Console](https://console.cloud.google.com).
2. Create a project (or reuse one).
3. **APIs & Services → Library** → search **"Photos Picker API"** → **Enable**.
4. **APIs & Services → OAuth consent screen**:
   - User type: **External**.
   - Under **Test users**, add the Google account whose photo library you'll compare
     against. (Only test users can use an unverified app — no Google verification needed.)
   - Recommended: **Audience → Publish app** to move it from **Testing** to
     **In production**. Google expires a Testing app's sign-in after 7 days, so you'd
     sign in again every week. An unverified app in production keeps working for your
     own account; you'll just keep seeing the "unverified app" warning when signing in.
   - **Branding** links (home page, privacy policy, terms of service) are optional.
5. **APIs & Services → Credentials → Create credentials → OAuth client ID**:
   - Application type: **Desktop app**.
   - Download the JSON.

## 2. Add credentials to the project

Rename the downloaded file to **`credentials.json`** and place it in the repository root
(where you run `dotnet run --project src/PhotoRecull.Cli` from), or
pass its path with `--credentials <file>`.

`credentials.json` and the cached OAuth token are **git-ignored** — never commit them.

## 3. First run

On the first run a browser window opens asking you to sign in and approve access. You'll
see an "unverified app" warning (expected for a personal test app) — choose
**Advanced → Go to … (unsafe)** to continue. The token is then cached under
`src/PhotoRecull.Cli/bin/.../token_store_picker/`, so later runs don't prompt again.

To sign in with a browser other than your default (e.g. keep the default signed out of
Google), use `--browser chrome`. See [USAGE.md](USAGE.md).

## Windows app: built-in Google client

Store builds of the Windows app have a Google OAuth client built in, so users only click
**Sign in with Google**. The client isn't in the repository. A build picks it up from:

- **Locally:** copy `src/PhotoRecull.App/GoogleOAuthClient.local.props.example` to
  `GoogleOAuthClient.local.props` (git-ignored) and fill in a Desktop OAuth client's ID and
  secret.
- **CI:** the `PhotoRecullGoogleClientId` and `PhotoRecullGoogleClientSecret` environment
  variables.

A build without them (a fork, for example) asks for a `credentials.json` during Setup
instead, as the command-line app does.

## Notes on the Google API change

Before 2025-03-31 an app could read a user's entire library via the Library API. Google
removed the `photoslibrary.readonly` scope, so that approach now returns
`403 PERMISSION_DENIED`. PhotoRecull uses the **Picker API** instead: you select the
surviving photos yourself. Consequence: **completeness is on you** — any photo still in
Google Photos that you forget to select will make its OneDrive twin look like a deletion
candidate. That's why the tool defaults to non-destructive `--dry-run` / `--stage`, and
why you should work one month at a time.
