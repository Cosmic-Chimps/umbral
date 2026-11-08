# Samples: the iOS (Swift, AppAuth) sample, implementer's notes

Task `2026-10-28-samples-ios-appauth-1osA`, branch `alta/whimsical-dune-6nkx` on `main` (`dfdbd90`).
Files for the documentation review: `samples/ios/README.md`, the row and the "A phone app" changes in
`samples/README.md`, the list sentence in `README.md` ("Integrate an app", now "six"), the "Samples"
list of `docs/native-apps.md`, `changelog.d/samples-ios.md`, and the header comments of
`.github/workflows/samples-ios.yml` and `samples.yml`.

Each statement of the sample's README, against the code (the code is normative), AppAuth-iOS 3.0.0
(tag `3.0.0`, `a972daac`), the iOS SDK headers of Xcode 27.1, or the UI strings (quoted from
`src/Umbral/Views/Texts.fs`). Paths below are relative to `samples/ios/UmbralSample/` unless they
say otherwise; AppAuth paths are relative to the AppAuth-iOS repository.

## What to create in Umbral (quoted strings)

| README says | Where |
| --- | --- |
| **Apps** | `Texts.fs:973` `AppsTitle` |
| **Create app** | `Texts.fs:991` `AppCreateButton` |
| **Name**, **Redirect URI**, **Post-logout redirect URI (optional)**, **App type** | `Texts.fs:983`, `:984`, `:986`, `:987` |
| **Single-page or native app** | `Texts.fs:1056` `AppTypeSpaOption` |
| **Client ID**, **Issuer URL** | `Texts.fs:994`, `:995` |
| sign-in page titled **Sign in to** and the app's name | `Texts.fs:970` `PickerTitleForApp`; seen in the walk |
| `com.cosmicchimps.umbral.sample:/callback`, `...:/signout-callback` | `UmbralAuth.swift:17-18` |
| the same URIs as the Android sample's | `samples/android/README.md`, "What to create in Umbral" |
| `://callback` is a different URI, answered with an error page | as in the Android notes: verified live for `docs/native-apps.md` (task n4tv); exact matching in `README.md` |

## How to run it

- One file, `UmbralSettings.swift` (`UMBRAL_ISSUER` line 7, `UMBRAL_CLIENT_ID` line 9), committed with
  `http://localhost:8080` and an empty client ID.
- "With no client ID, **Sign in** and **Private** say so": `ContentView.swift:71` returns before
  anything starts, with the message at `:14`.
- Xcode 26 or later: the project is `objectVersion = 77` (Xcode 16 format, synchronized folders), so
  Xcode 26.6 (CI) and 27.1 (here) both open and build it. iOS 18 or later: `IPHONEOS_DEPLOYMENT_TARGET = 18.0`
  (AppAuth 3.0.0 needs iOS 15).
- AppAuth at the exact version: `XCRemoteSwiftPackageReference` with `kind = exactVersion; version = 3.0.0`
  in `project.pbxproj`, and `Package.resolved` pinning revision `a972daac82d449d58ab119e91c68153e29ddac33`.
- The Simulator shares the Mac's network: the walk below reached the Umbral on the Mac's
  `localhost:18080` from the app and from the sheet with no forwarding.
- The command-line block: `xcodebuild … -destination 'generic/platform=iOS Simulator' -derivedDataPath build build`
  is the CI build plus a derived-data path; `open -a Simulator` failed on this Mac ("Unable to find
  application named 'Simulator'"), hence `open "$(xcode-select -p)/Applications/Simulator.app"`;
  `simctl install booted` / `launch` are standard. The walk itself installed the app through `xcodebuild test`.
- On an iPhone: not walked (no phone here). Free personal-team signing, **Developer Mode** under
  **Privacy & Security**: Apple's documentation for running on a device.
- Plain http only on loopback: `UmbralAuth.swift:181-194` (`checkedIssuer`, before any request).

## What you should see

All seen in the walk below (screenshots taken, not committed):

- Home: heading **Umbral sample**, **Public**, **Private**, **Sign in**.
- The prompt reads **"UmbralSample" Wants to Use "localhost" to Sign In**, with **Cancel** and
  **Continue**. iOS uses the bundle name (`CFBundleName`, `UmbralSample`) there, not the display name
  (`Umbral sample`, shown on the home screen icon), hence the README's wording.
- The sheet shows `localhost` and Umbral's page; after the fake connector, the table with `sub`,
  `name`, `idp`, `email`, `email_verified` (`true`, not `1`: `UmbralAuth.swift:58-63`).
- **Sign out** shows the same prompt again (seen), then the sheet closes and Home shows **Sign in**.

## How it works

| README says | Where |
| --- | --- |
| Discovery only, end-session endpoint included | `UmbralAuth.swift:81` `discoverConfiguration(forIssuer:)`; sign-out reads `endSessionEndpoint` from the stored configuration (`:145-149`) |
| `ASWebAuthenticationSession`, no `WKWebView` | AppAuth `Sources/AppAuth/iOS/OIDExternalUserAgentIOS.m:106-108`; `grep -rn WKWebView samples/ios` finds only comments |
| no fallback to Safari when the session cannot start | AppAuth `CHANGELOG.md`, 2.1.0: "Removed external browser (Safari) fallback from `OIDExternalUserAgentIOS`" |
| PKCE S256, `state`, `nonce` generated, never turned off | AppAuth `Sources/AppAuthCore/OIDAuthorizationRequest.m:172-181` (the initializer `UmbralAuth.swift:95-102` uses) |
| `state` checked on return | AppAuth `OIDAuthorizationService.m:171` ("State mismatch") |
| `id_token` checked: issuer, audience, expiry, issue time, `nonce` | AppAuth `OIDAuthorizationService.m:595-680` (rules 2, 3, 9, 10 (600 s), 11), run by `authState(byPresenting:…)` (`UmbralAuth.swift:103`) |
| no signature check | same function: no signature rule (OIDC Core 3.1.3.7 allows TLS for a token from the token endpoint) |
| Scopes `openid profile email` | `UmbralAuth.swift:99` (AppAuth's `OIDScopeOpenID`, `OIDScopeProfile`, `OIDScopeEmail`) |
| the session catches the redirect; scheme in `CFBundleURLTypes`; `onOpenURL` | AppAuth passes the redirect's scheme as `callbackURLScheme` (`OIDExternalUserAgentIOS.m:107-108`); `Info.plist:6-18`; `UmbralSampleApp.swift:12` |
| No secret | `clientSecret: nil` (`UmbralAuth.swift:98`), no other mention |
| Keychain, `WhenUnlockedThisDeviceOnly`, not `UserDefaults` | `TokenStore.swift:20-32`; `grep -rn UserDefaults samples/ios/UmbralSample` finds only the comment. Not synced and not restored onto another device: Apple's documentation of `kSecAttrAccessibleWhenUnlockedThisDeviceOnly` |
| No refresh token; expired `id_token` means signed out | no `offline_access`; `UmbralAuth.swift:44-46` |
| Sign-out: tokens forgotten first, then `OIDEndSessionRequest` with `id_token_hint`, post-logout URI, `state` | `UmbralAuth.swift:144-168` (`discard()` at `:147`, before the request); AppAuth's initializer generates `state` (`OIDEndSessionRequest.m`) |
| Plain http: the issuer check, and ATS for the rest | `checkedIssuer`; `Info.plist:20-23` `NSAllowsLocalNetworking` (no `NSAllowsArbitraryLoads`) |

## A private browser session, Universal Links

- `prefersEphemeralSession`: `UmbralAuth.swift:20-23`, passed to sign-in (`:103-106`) and to the
  sign-out user agent (`:150-151`). What `true` changes: Apple's `prefersEphemeralWebBrowserSession`
  (header `ASWebAuthenticationSession.h:83`) and `docs/native-apps.md`, "Staying signed in". Not walked
  with `true`.
- Universal Links: the `https(host:path:)` callback and its requirement are from the SDK header
  `ASWebAuthenticationSessionCallback.h:19-23` ("The host must be associated with the app using
  associated web credentials domains"); iOS 17.4 from the same header's availability. That AppAuth
  3.0.0 passes a scheme only: `OIDExternalUserAgentIOS.m:107-108` (`callbackURLScheme:`). Not
  exercised: it needs a domain and a paid team.

## The hand walkthrough

Done 2026-11-07 in the iOS Simulator, **iPhone 17, iOS 27.1** (Xcode 27.1, macOS 26.6.2, Apple
silicon), driven by an XCUITest written for the walk in a scratch copy of the project (not committed),
which tapped through the README's steps and took screenshots.

Umbral: built from this branch's `main` (`umbral:samples`), started with the harness's
`samples/test/docker-compose.yml` (fake connector) plus an override putting it on
`http://localhost:18080` inside and out (host port 8080 is taken on this Mac), the app registered
through the admin screens by a copy of `setup.spec.ts` with the two custom-scheme URIs, type
**Single-page or native app**. In the scratch copy only, `UMBRAL_ISSUER=http://localhost:18080` and
the client ID; the committed file is unchanged. The Simulator was erased before the final run, so
Safari started with no Umbral session.

1. Home: heading, **Public**, **Private**, **Sign in**; no **Sign out**. Public opens.
2. **Private** (signed out): the prompt, **Continue**, the sheet on `localhost` with Umbral's page
   (**Sign in to ios**), the fake connector with its defaults (email `test@example.com`, verified) →
   back in the app on Private: `sub`, `name` (`Test User`), `idp` (`fake`), `email`,
   `email_verified` (`true`).
3. App terminated and relaunched: still signed in, Private shows the claims (state read back from
   the Keychain).
4. **Sign out**: the prompt again, **Continue**, the sheet opens and closes, Home shows **Sign in**.
5. **Private** again: the prompt, then Umbral's page is shown again, not a silent return, so
   Umbral's session in Safari ended.
6. On that page, the fake connector as a new subject with no email (`ios-person-2`, display name
   `iOS Person Two`, email empty) → Private shows `sub`, `name`, `idp` only, no `email` and no
   `email_verified` row.
7. Back, **Sign out** (prompt, sheet), then **Sign in** from Home: the prompt, Umbral's page again,
   the fake connector → back on Home, which now shows **Sign out**.
8. **Sign out**, then **Sign in** and **Cancel** on the prompt → Home shows **Sign-in cancelled** in
   red and nothing else happens (AppAuth's `userCanceledAuthorizationFlow`, `UmbralAuth.swift:197-201`).

The prompt appeared before every sign-in and every sign-out (seven times in the run), each reading
"UmbralSample" Wants to Use "localhost" to Sign In. Earlier runs of the same script failed only on the
script's own side (Umbral's `sub` is its own identifier, not the fake subject; typing into the web
form; a prompt slower than the script waited on a freshly erased Simulator), never on the app's.

## CI

`.github/workflows/samples-ios.yml`: only on changes under `samples/ios/` (and the workflow itself),
`permissions: contents: read`, checkout pinned by SHA (v5.1.0, as in the other workflows) with
`persist-credentials: false`, no other action. `macos-26`, since `macos-14`, which the task named,
was retired from GitHub Actions on 2026-11-02 (actions/runner-images README); Xcode pinned through
`DEVELOPER_DIR=/Applications/Xcode_26.6.app` (the image's default, listed in its readme;
`xcode-27` is a preview label). `xcodebuild … -onlyUsePackageVersionsFromResolvedFile CODE_SIGNING_ALLOWED=NO build`
for the Simulator, with warnings as errors on the app target. Locally (Xcode 27.1) it builds with no
warning. actionlint 1.7.12 is clean on both workflows. `samples.yml` now ignores a change under
`samples/ios/` alone. Not run on GitHub yet (nothing is pushed).

## Changelog

`changelog.d/samples-ios.md`, `### Added`, one bullet. The task text said "a iOS"; written "an iOS".
