# Samples: the Android (Kotlin, AppAuth) sample, implementer's notes

Task `2026-10-28-samples-android-appauth-4ndK`, branch `alta/mindful-starling-nw5b` on `main` (`4657c66`).
Files for the documentation review: `samples/android/README.md`, the row and the new "A phone app"
section in `samples/README.md` (plus one sentence under "Adding a sample" and one in the intro),
the list sentence in `README.md` ("Integrate an app"), the "Samples" list of `docs/native-apps.md`,
`changelog.d/samples-android.md`, and the header comments of `.github/workflows/samples-android.yml`
and `samples.yml`.

Each statement of the sample's README, against the code (the code is normative), AppAuth-Android
0.11.1, or the UI strings (quoted from `src/Umbral/Views/Texts.fs`). Paths below are relative to
`samples/android/app/src/main/java/com/cosmicchimps/umbral/sample/` unless they say otherwise.

## What to create in Umbral (quoted strings)

| README says | Where |
| --- | --- |
| **Apps** | `Texts.fs:973` `AppsTitle` |
| **Create app** | `Texts.fs:991` `AppCreateButton` |
| **Name**, **Redirect URI**, **Post-logout redirect URI (optional)**, **App type** | `Texts.fs:983`, `:984`, `:986`, `:987` |
| **Single-page or native app** | `Texts.fs:1056` `AppTypeSpaOption` |
| **Client ID**, **Issuer URL** | `Texts.fs:994`, `:995` |
| sign-in page titled **Sign in to** and the app's name | `Texts.fs:970` `PickerTitleForApp` |
| `com.cosmicchimps.umbral.sample:/callback`, `...:/signout-callback` | `UmbralAuth.kt:147-148` |
| `://callback` is a different URI, answered with an error page | verified live for `docs/native-apps.md` (task n4tv: `com.example.app:/callback` matched, `://` refused); Umbral matches exactly (`README.md` "Redirect URIs are matched exactly") |

## How to run it

- One file, `samples/android/umbral.properties`, read by `app/build.gradle.kts:9-11` and copied into
  `BuildConfig` at `:30-31`. Committed with `UMBRAL_ISSUER=http://localhost:8080` and an empty
  `UMBRAL_CLIENT_ID`.
- "With no client ID, **Sign in** and **Private** say so": `MainActivity.kt` `signIn()` sets
  "Set UMBRAL_CLIENT_ID in umbral.properties, then rebuild." and returns before discovery.
- "**Could not reach Umbral**": `UmbralAuth.kt:72` (`describe("Could not reach Umbral", ex)`).
- `adb reverse tcp:8080 tcp:8080` / `./gradlew installDebug`: what the hand walk below did (with
  `tcp:18080` on the host side, since 8080 was taken on this machine).
- Plain http only on loopback: `UmbralAuth.kt:162` (the connection builder refuses http for any
  host but `localhost` and `127.0.0.1`, since N1 of the code review) and `app/src/main/res/xml/network_security_config.xml`
  (cleartext only for `localhost` and `127.0.0.1`).
- JDK 17 or later: AGP 9.5.1 requires it; Gradle 9.9.0 (wrapper, `distributionSha256Sum` checked
  against services.gradle.org; `gradle-wrapper.jar` matches Gradle's published
  `gradle-9.9.0-wrapper.jar.sha256`).

## What you should see

All of it seen on the emulator (screenshots were taken, not committed): the three buttons, Public,
Private opening a Custom Tab on **Sign in to android**, the table with `sub`, `name`, `idp`, `email`,
`email_verified`; Sign out closing the sheet and the home screen back on **Sign in**.

## How it works

| README says | Where |
| --- | --- |
| Discovery only (`fetchFromIssuer`), end-session endpoint included | `UmbralAuth.kt:70`; `signOut()` reads `config.endSessionEndpoint` from that configuration |
| Custom Tab, or the browser when it has no Custom Tabs; no WebView | AppAuth's `BrowserSelector` prefers browsers with Custom Tabs and falls back to one without; `grep -r WebView samples/android/app/src` is empty |
| PKCE S256, `state`, `nonce` by default, not turned off | `AuthorizationRequest.Builder` defaults (`setCodeVerifier` with S256, generated `state` and `nonce`); the walk's authorize URL carried `state=…&nonce=…&code_challenge=…&code_challenge_method=S256` |
| `state` checked on return; `id_token` checked (issuer, audience, expiry, nonce) after the exchange | AppAuth `AuthorizationManagementActivity` (state mismatch → error), `AuthorizationService.TokenRequestTask` → `IdToken.validate` |
| AppAuth does not check the signature | `IdToken.validate` has no signature check; OIDC Core 3.1.3.7 allows TLS validation instead for a token received directly from the token endpoint |
| Scopes `openid profile email` | `UmbralAuth.kt:80` |
| `appAuthRedirectScheme` gives the scheme to `RedirectUriReceiverActivity` | `app/build.gradle.kts:34`; merged manifest checked: the activity with scheme `com.cosmicchimps.umbral.sample`; in the walk, sign-out came back through `RedirectUriReceiverActivity` |
| No client authentication | `UmbralAuth.kt:95` (`performTokenRequest` without a `ClientAuthentication`) |
| AES-GCM key in the Keystore, only ciphertext in prefs | `TokenStore.kt:63-74`, `:40`; on the emulator `shared_prefs/umbral_sample_auth.xml` held only base64 ciphertext (no `eyJ`, no token field names) |
| `EncryptedSharedPreferences` deprecated in favour of the Keystore | androidx `security-crypto` release notes: "Deprecated all APIs in favour of existing platform APIs and direct use of Android Keystore." (1.1.0) |
| Backups turned off | `AndroidManifest.xml:8` `allowBackup="false"`, plus `res/xml/data_extraction_rules.xml` and `backup_rules.xml` excluding everything |
| No refresh token; expired `id_token` means signed out | no `offline_access` in the scopes; `UmbralAuth.kt:55-56` |
| Sign-out: tokens forgotten first, then `EndSessionRequest` with `id_token_hint`, post-logout URI, `state` | `UmbralAuth.kt:115-118` (`discard()` before the request; AppAuth's `EndSessionRequest.Builder` generates `state`) |
| Closing the sheet by hand still leaves the app signed out | `discard()` runs before the intent is launched |
| Loopback http skips AppAuth's https-issuer check, nowhere else | `UmbralAuth.kt:40` |

## App Links, Your own app

- The App Links section follows Android's App Links documentation (`assetlinks.json` at
  `/.well-known/`, `autoVerify`) and AppAuth-Android's README (declare `RedirectUriReceiverActivity`
  with the https intent filter, `tools:node="replace"`). Not exercised on a device: that needs a domain.
  The manifest part was built (docs nit 4), in a scratch copy of the sample with the README's snippet
  added, by `./gradlew processDebugMainManifest` and a look at
  `app/build/intermediates/merged_manifest/debug/processDebugMainManifest/AndroidManifest.xml`:
  - with `tools:node="replace"`: `RedirectUriReceiverActivity` has the `https://app.example.com/oauth/callback`
    filter (`autoVerify="true"`) only; the custom-scheme filter is gone; the build also succeeds with
    `appAuthRedirectScheme` removed from `build.gradle.kts` (the placeholder is then unused);
  - without `replace`, placeholder set: both filters, the https one and `com.cosmicchimps.umbral.sample`.
  The README says exactly that.
- "Your own app": `applicationId` and `appAuthRedirectScheme` in `app/build.gradle.kts:24,34`, the
  URIs in `UmbralAuth.kt:147-148`.

## The hand walkthrough

Done 2026-11-07 on the Android Emulator, AVD **Pixel 7, Android 15 (API 35, VanillaIceCream),
Google Play image, arm64**, with Chrome as the browser, run headless on macOS (Apple silicon).

Umbral: built from this branch's `main` (`umbral:samples`), started with the harness's
`samples/test/docker-compose.yml` (fake connector, `UMBRAL__PUBLIC_ORIGIN=http://localhost:8080`),
published on host port 18080; the app registered through the admin screens by a copy of
`setup.spec.ts` with redirect URIs `com.cosmicchimps.umbral.sample:/callback` and
`:/signout-callback`, type **Single-page or native app**. `adb reverse tcp:8080 tcp:18080`, the
client ID put in `umbral.properties` for the build only (reverted, not committed).

1. Home: heading, **Public**, **Private**, **Sign in**. Public opens.
2. **Private** (signed out): Custom Tab on `localhost:8080`, **Sign in to android**; fake connector
   with an email → back in the app on Private: `sub`, `name`, `idp` (`fake`), `email`,
   `email_verified` (`true`).
3. App force-stopped and relaunched: still signed in (state read back from the Keystore-encrypted
   prefs).
4. **Sign out**: Custom Tab → Umbral's end-session endpoint → `RedirectUriReceiverActivity` → home
   shows **Sign in**; the prefs file is empty.
5. **Private** again: Umbral's page is shown again (its persistent session cookie is gone, so
   Umbral's session ended), not a silent return.
6. Fake connector with no email (new subject) → Private shows `sub`, `name`, `idp` only.
7. **Sign out**, then **Sign in** from Home → back on Home with **Sign out**.

One hiccup, not the app's: on the first try of step 5, Chrome's renderer crashed on the emulator
(`SandboxedProcessService` restarts in logcat) and AppAuth reported "User cancelled flow"; after
restarting Chrome the same step worked. Umbral answered that authorize URL with its usual 302 to
`/signin` (checked with curl).

## CI

`.github/workflows/samples-android.yml`: `ubuntu-24.04`, only on changes under `samples/android/`
(and the workflow itself), `permissions: contents: read`, actions pinned by SHA (checkout v5.1.0
as in `samples.yml`, setup-java v6.2.0, gradle/actions/setup-gradle v6.4.1, which also validates
the wrapper jar, upload-artifact v7.0.2), `./gradlew --no-daemon assembleDebug lintDebug`.
Locally that takes about 30 s warm, about 2 min cold. Lint: no errors; warnings left are the
missing launcher icon and `UseKtx` hints that would need `core-ktx`. `samples.yml` now ignores a
change under `samples/android/` alone, since the harness cannot walk an app.

Not run on GitHub yet (nothing is pushed).

## Changelog

`changelog.d/samples-android.md`, `### Added`, one bullet. The task text said "a Android"; written
"an Android".
