# Code review: the iOS (Swift, AppAuth) sample (task 1osA)

Branch `alta/whimsical-dune-6nkx`, `dfdbd90..d5b5572` (the sample is `4373eea`). Reviewer: the `umbral` session, 2026-11-07. I read every file and built the app myself; the sign-in rests on the implementer's Simulator walk (iPhone 17, iOS 27.1), recorded in `notes.md`.

## Verdict

**Clear to merge.** No blocking items; one nit, and one thing for the maintainer to watch.

## What was checked, against the task's list

- **The system browser, not a WebView.** Sign-in goes through `OIDAuthState.authState(byPresenting:presenting:prefersEphemeralSession:)` and sign-out through `OIDExternalUserAgentIOS`, both `ASWebAuthenticationSession`; there is no `WKWebView`. `onOpenURL` hands a redirect that reaches the app as a URL back to AppAuth's session, the documented fallback.
- **PKCE, `state` and `nonce`.** The `OIDAuthorizationRequest` initializer used generates all three (S256); AppAuth checks `state` on return and validates the `id_token`'s issuer, audience, expiry and nonce during the exchange. The code says plainly that AppAuth does not verify the `id_token`'s signature and relies on TLS to the token endpoint, which is honest and true.
- **No secret in the app.** `clientSecret: nil`, a public client; `UmbralSettings` holds only the issuer and the client ID.
- **Tokens in the Keychain.** The whole `OIDAuthState`, archived with secure coding, in one generic-password item with `kSecAttrAccessibleWhenUnlockedThisDeviceOnly`: out of iCloud Keychain and out of a backup restored to another device. An unreadable item is deleted and the person is signed out.
- **Transport.** `checkedIssuer()` refuses any issuer that isn't https, or http to `localhost` or `127.0.0.1`, before a network call; ATS allows plain http only to local networking (`NSAllowsLocalNetworking`), nothing else is exempt.
- **The redirect scheme.** `CFBundleURLTypes` registers `com.cosmicchimps.umbral.sample`; `redirectURL` and `postLogoutRedirectURL` use one slash after the colon, matching Umbral's exact comparison.
- **Sign-out through Umbral.** Tokens are discarded from memory and the Keychain first, then `OIDEndSessionRequest` with `id_token_hint`, the post-logout URI and AppAuth's `state`; a cancel counts as signed out, only a real error is shown.
- **Session expiry.** `claims()` treats an expired `id_token` as signed out (no `offline_access`, so no refresh token). `prefersEphemeralSession = false` shares Safari's cookies, which with Umbral's persistent session cookie (0.9.0) is what lets a second sign-in skip Umbral's page; the README explains both settings.
- **Concurrency.** Swift 6, `@MainActor` on the model, and AppAuth's non-`Sendable` callbacks handed over with `nonisolated(unsafe)` inside `MainActor.assumeIsolated`, since AppAuth calls back on the main queue. That is the honest way to bridge a non-annotated Objective-C library, and the build accepts it with warnings as errors.
- **Pins.** AppAuth-iOS `exactVersion 3.0.0` in the project and revision `a972daac` in `Package.resolved`; CI builds with `-onlyUsePackageVersionsFromResolvedFile`, so no resolution happens there.
- **The CI job.** `samples-ios.yml`: `contents: read`, `persist-credentials: false`, checkout pinned by SHA, `macos-26`, a 20-minute timeout, Xcode pinned through `DEVELOPER_DIR` (26.6), a Simulator build without signing. Warnings are errors through the project's own `SWIFT_TREAT_WARNINGS_AS_ERRORS = YES`. `samples.yml` now skips a change under `samples/ios/` alone.
- **Built by the reviewer.** `xcodebuild … -destination 'generic/platform=iOS Simulator' -onlyUsePackageVersionsFromResolvedFile CODE_SIGNING_ALLOWED=NO build` on Xcode 27.1: `** BUILD SUCCEEDED **`, exit 0, no warning from the sample or from AppAuth (the one line in the log is Xcode's own App Intents metadata note).

## Nit

- **N1. A failed Keychain write passes silently.** `TokenStore.save` ignores `SecItemAdd`'s status, so if the write fails the app is signed in until it closes and signed out after, with no hint why. Check the status and print it (`os_log` or `print`), the way the sign-in errors are reported.

## For the maintainer

- The implementer and I built on Xcode 27.1; CI pins **Xcode 26.6**, because `macos-26` is the runner. The first `samples-ios` run on the merge push is the first build on that Xcode, so check that it is green.

## Implementer's answers

- **N1, taken.** `TokenStore.save` returns the `OSStatus` of the write (`errSecParam` when the state
  cannot be archived; nothing is stored on failure), and sign-in prints and shows "Signed in, but the
  Keychain did not keep the tokens (OSStatus …): you will be signed out when the app closes." The
  person stays signed in for the run, as before. Rebuilt with the CI command on Xcode 27.1: `** BUILD
  SUCCEEDED **`, no warning.
- **For the maintainer:** noted; the first `samples-ios` run (Xcode 26.6) has to be green before this
  counts as built in CI.
