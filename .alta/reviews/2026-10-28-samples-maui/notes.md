# .NET MAUI sample: notes for the reviews

Task `2026-10-28-samples-maui-m4ui`, branch `alta/vibrant-regulus-bmdk`, on `main` at 3c87b72.

## What changed

New:

- `samples/maui/`: the app (code-only C#, no XAML), its README, `global.json` (SDK 10.0.402,
  `latestPatch`, so CI stays on the 10.0.4xx band its workload set belongs to).
- `.github/workflows/samples-maui.yml`: builds the Android target.
- `changelog.d/samples-maui.md`, Added.

Changed:

- `samples/README.md`: a row in the samples table, the MAUI sample named in "A phone app".
- `README.md`, "Integrate an app": seven samples, the MAUI one named.
- `docs/native-apps.md`, "Samples": one entry.
- `.github/workflows/samples.yml`: ignores a change under `samples/maui/` alone, as it does for
  Android and iOS.

## Three choices that differ from the task text

1. **Its own scheme, `com.cosmicchimps.umbral.sample.maui`**, not the task's
   `com.cosmicchimps.umbral.sample://callback`. The Android and iOS samples already use
   `com.cosmicchimps.umbral.sample`; with the same scheme, two samples installed on one phone would
   both claim the sign-in callback. On the emulator the AppAuth Android sample and this one were
   installed side by side during the walk. The single-slash form (`:/callback`) follows the other
   two samples and `docs/native-apps.md`.
2. **The app sends and checks `nonce` itself.** Duende's OidcClient (7.1.1) checks `state`
   (`ResponseProcessor.cs`, "Invalid state."), but its code flow neither sends nor checks a nonce
   (no `nonce` in `AuthorizeClient.cs` or `ResponseProcessor.cs`). The contract requires one, so
   `UmbralAuth.cs:84-90` sends a random 32-byte nonce and `:109-116` refuses the sign-in when the
   validated `id_token`'s `nonce` is missing or different.
3. **The app validates the `id_token` signature itself.** OidcClient has no default validator: with
   none set it throws, or, with `RequireIdentityTokenSignature = false`, it only decodes the token
   (`NoValidationIdentityTokenValidator`). `UmbralIdentityTokenValidator.cs` validates with
   `Microsoft.IdentityModel.JsonWebTokens` against discovery's keys: signature (RS/PS/ES only),
   issuer, audience, lifetime, claim names kept (`MapInboundClaims = false`).

## The task's checklist and where each is

| Point | Where |
| --- | --- |
| System browser, never a WebView | `WebAuthenticatorBrowser.cs` (MAUI `WebAuthenticator`: Custom Tab / `ASWebAuthenticationSession`); no `WebView` anywhere (`grep -rn WebView samples/maui` finds only comments) |
| PKCE | OidcClient's default (S256), not turned off; the walk's sign-ins passed Umbral, which refuses a request without PKCE |
| `state` | OidcClient `ResponseProcessor` |
| `nonce` | `UmbralAuth.cs:84-90`, `:109-116` |
| No secret | `UmbralAuth.cs:196` (no `ClientSecret`), `UmbralSettings.cs` (issuer and client ID only) |
| Tokens in the platform's secure store | `TokenStore.cs` (`SecureStorage.Default` only); on the emulator the only prefs file held no `eyJ` |
| Redirect scheme registered and matched | Android `Platforms/Android/WebAuthenticationCallbackActivity.cs:17` (`DataScheme`), iOS `Platforms/iOS/Info.plist:54` (`CFBundleURLSchemes`); the URIs `UmbralAuth.cs:32-33`, registered in Umbral exactly so in the walk |
| Sign out through Umbral | `UmbralAuth.cs:148-170`: tokens cleared first, then `LogoutAsync` with `id_token_hint` (end-session endpoint from discovery) |
| Endpoints from discovery only | `UmbralAuth.cs:189-206` (`Authority` only; `RequireHttps` relaxed for an http loopback issuer only) |
| Scopes `openid profile email` | `UmbralAuth.cs:197` |
| http on loopback only | `UmbralAuth.cs` `CheckIssuer`, Android `Platforms/Android/Resources/xml/network_security_config.xml`, iOS `NSAllowsLocalNetworking` (`Info.plist:63`) |
| Pinned dependencies | `UmbralSample.csproj`: `Microsoft.Maui.Controls` 10.0.110 (the installed workload's), `Duende.IdentityModel.OidcClient` 7.1.1, `Microsoft.IdentityModel.JsonWebTokens` 8.23.0; CI pins the workload set (`--version 10.0.404`) and the SDK band (`global.json`) |
| CI permissions and pinning | `samples-maui.yml`: `contents: read`, `ubuntu-24.04`, checkout v5.1.0, setup-java v6.2.0 (JDK 21), setup-dotnet v5.4.0, all by SHA; actionlint clean |

## Doc statements and what they were checked against

- **Apps**, **Create app**, **Name**, **Redirect URI**, **Post-logout redirect URI (optional)**,
  **App type**, **Single-page or native app**, **Client ID**, **Issuer URL**: quoted from
  `src/Umbral/Views/Texts.fs` (as in the Android and iOS READMEs, which the docs review cleared).
- **Sign in to** and the app's name: the walk (`Sign in to maui`).
- "`com.cosmicchimps.umbral.sample.maui://callback` is a different URI, and Umbral answers it with an
  error page": checked live on 2026-11-05 for `com.example.app://callback` (native-apps notes) and by
  the same exact match here.
- JDK 21: .NET for Android refused Android Studio's JDK 25 with `XA0030`.
- "at the version the iOS workload asks for": MAUI 10.0.110's iOS workload refused Xcode 27.1
  ("requires Xcode 27.2"); the local build used `-p:ValidateXcodeVersion=false -p:MtouchLink=SdkOnly`.
- The iOS "Wants to Use … to Sign In" prompt and **Continue**: the iOS walk below.
- No refresh token, expired `id_token` counts as signed out: `UmbralAuth.cs:54-65`, as in the
  Android and iOS samples (`UmbralAuth.kt:48`, `UmbralAuth.swift:36-44`).

## The hand walkthrough

### Android

Done 2026-11-11 on the Android Emulator, AVD **Pixel 7, Android 15 (API 35, VanillaIceCream),
Google Play image, arm64**, Chrome as the Custom Tab browser, headless on macOS (Apple silicon).
App built with .NET 10.0.402 and the MAUI 10.0.110 workload, JDK 21.

Umbral: `umbral:samples` from this branch, started with the harness's
`samples/test/docker-compose.yml` (fake connector) plus an override putting it on
`http://localhost:18080` inside and out (host port 8080 is taken on this Mac). The app registered
through the admin screens by a throwaway copy of `setup.spec.ts`: redirect URIs
`com.cosmicchimps.umbral.sample.maui:/callback` and `:/signout-callback`, type **Single-page or
native app**. `adb reverse tcp:18080 tcp:18080`; in the build only, `Issuer` set to
`http://localhost:18080` and the client ID (the committed `UmbralSettings.cs` is unchanged). Driven
with `adb` and `uiautomator`.

1. Home: heading, **Public**, **Private**, **Sign in**. Public opens ("This page is open to anyone,
   signed in or not.").
2. **Private** (signed out): a Custom Tab on `localhost:18080`, **Sign in to maui**; the fake
   connector with its defaults → back in the app on Private: `sub`, `name` (`Test User`), `idp`
   (`fake`), `email` (`test@example.com`), `email_verified` (`true`). So the `id_token` passed the
   app's signature, issuer, audience and lifetime checks and its `nonce` matched.
3. App force-stopped and relaunched: Home shows **Sign out**, Private shows the claims (read back
   from `SecureStorage`). The app's only prefs file holds no plain token (no `eyJ`).
4. **Sign out**: the Custom Tab opens Umbral's end-session endpoint and closes; Home shows **Sign
   in**.
5. **Private** again: Umbral's sign-in page is shown, not a silent return, so Umbral's session ended.
6. The fake connector as a new subject with no email (`maui-person-2`, `MAUI Person Two`) →
   Private shows `sub`, `name`, `idp` only: no `email` and no `email_verified` row.
7. Sign out, **Private**, the fake connector with its defaults → back on Private with the claims and
   the same `sub` as in step 2.
8. Sign out, **Sign in** from Home, **Close tab** → Home shows **Sign-in cancelled**; **Sign in**
   again through the fake connector → Home shows **Sign out**.

### iOS

Done 2026-11-11 in the iOS Simulator, **iPhone 17, iOS 26.5** (Xcode 27.1, Apple silicon), the
app built for `iossimulator-arm64` with `-p:ValidateXcodeVersion=false -p:MtouchLink=SdkOnly` (the
workload wants Xcode 27.2), the same Umbral and the same issuer (`http://localhost:18080`: the
Simulator shares the Mac's `localhost`). The Simulator was erased before the run, so Safari had no
Umbral session. Driven by a throwaway XCUITest (an XcodeGen project in `/tmp`, not committed)
that launched the app by its bundle ID and tapped through the steps. The iOS 27.1 runtime here
offers only an "iPhone Duo" device, which drew no screen without the Simulator app (absent from
Xcode 27.1), so 26.5 was used; the app's minimum is iOS 18.

1. Home: heading, **Public**, **Private**, **Sign in**, no **Sign out**. Public opens.
2. **Private** (signed out): the system prompt, **Continue**, the sheet on `localhost` with
   **Sign in to maui**; the fake connector with its defaults → Private shows `sub`, `name`
   (`Test User`), `idp` (`fake`), `email`, `email_verified` (`true`).
3. App terminated and relaunched: Home shows **Sign out**, Private shows the claims (Keychain).
4. **Sign out**: the prompt, **Continue**, the sheet opens and closes, Home shows **Sign in**.
5. **Private** again: the prompt, then Umbral's page, not a silent return.
6. A new subject with no email (`ios-maui-2`, `MAUI iOS Two`) → Private shows `sub`, `name`, `idp`
   only, no `email` and no `email_verified` row.
7. Sign out, then **Sign in** from Home: the prompt, Umbral's page, the fake connector → Home shows
   **Sign out**.
8. Sign out, **Sign in**, **Cancel** on the prompt → Home shows **Sign-in cancelled**.

## CI

`.github/workflows/samples-maui.yml`: builds `net10.0-android` on `ubuntu-24.04`, only on changes
under `samples/maui/` (and the workflow itself). The iOS target is not built in CI: MAUI 10.0.110's
iOS workload accepts exactly Xcode 27.2, which GitHub's macOS images do not promise, and the
shared code is already compiled by the Android build. Locally both targets build with 0 warnings
(warnings are errors). Not run on GitHub yet (nothing is pushed).

## How it was verified

- `dotnet build -f net10.0-android` and `-f net10.0-ios` (Simulator): 0 warnings, 0 errors.
- The hand walks above.
- actionlint on `samples-maui.yml` and `samples.yml`; `.github/scripts/check-links.py`;
  `.github/scripts/changelog.sh check`.

## Changelog

`changelog.d/samples-maui.md`, Added.
