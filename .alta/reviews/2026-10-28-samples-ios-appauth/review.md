# Documentation review: the iOS sample (`samples/ios`)

Task: `2026-10-28-samples-ios-appauth-1osA`.
Branch: `alta/whimsical-dune-6nkx`, commits `4373eea` (the sample) and
`d5b5572` (the notes), on `main` at `dfdbd90`.
Reviewed 2026-11-07 by the documentation reviewer session, per section 6 of
`docs/audit-2026-10.md`, from the diff, `notes.md`, the sample's Swift code,
`Info.plist` and project, AppAuth-iOS at the `3.0.0` tag (`a972daac`, read on
GitHub), and the iOS 27.1 SDK headers.

## Verdict

**Clear to merge.** No blocking item. Three nits, none needs a re-check.

## What was checked

- **Against the phone-app contract.** Three screens with **Public**,
  **Private**, **Sign in** or **Sign out**; the system's sign-in browser
  (`ASWebAuthenticationSession`, no `WKWebView`); the two settings in one file
  built into the app; custom-scheme redirect URIs named in the README; tokens
  in the Keychain; the same protocol; "How to run it" for the Simulator and a
  device, with how the app reaches a local Umbral; the walk recorded in
  `notes.md`. The contract's generalisation from the Android review holds for
  iOS as written, and this branch's one-line addition about the Simulator
  sharing `localhost` is right.
- **Quoted UI strings.** The Umbral labels and "Sign in to" are in `Texts.fs`
  at the lines `notes.md` gives; the app's labels (**Umbral sample**,
  **Public**, **Private**, **Sign in**, **Sign out**, **Sign-in cancelled**,
  **Could not reach Umbral**, the `UMBRAL_CLIENT_ID` message) are in
  `ContentView.swift` and `UmbralAuth.swift`.
- **The README against the code.** Discovery only
  (`discoverConfiguration(forIssuer:)`, the end-session endpoint from the
  stored configuration); `clientSecret: nil` and no client authentication;
  the three scopes; the two URIs (the same as Android's, so one app in Umbral
  serves both, which is true and useful); `CFBundleURLTypes` and `onOpenURL`
  as the fallback; one Keychain item with
  `kSecAttrAccessibleWhenUnlockedThisDeviceOnly`, no `UserDefaults`; no
  `offline_access`, an expired `id_token` counts as signed out; sign-out
  discards first, then `OIDEndSessionRequest`; `checkedIssuer` refuses http
  except to `localhost` and `127.0.0.1` before any request; `prefersEphemeralSession`
  is `false` and passed to both sign-in and sign-out. Every statement in "How
  it works" and "A private browser session" is in the code.
- **The project.** Bundle ID `com.cosmicchimps.umbral.sample`, display name
  "Umbral sample", product name `UmbralSample` (so the prompt's
  "UmbralSample", as the walk saw), deployment target iOS 18.0, AppAuth pinned
  `exactVersion` 3.0.0 with the revision in `Package.resolved`.
- **AppAuth 3.0.0, read at the tag.**
  - `OIDExternalUserAgentIOS.m:107-108` creates the `ASWebAuthenticationSession`
    with `callbackURLScheme:` only, so the Universal Links section's "cannot
    use it out of the box" is right, and so is the remedy (an
    `OIDExternalUserAgent` of your own).
  - `CHANGELOG.md`: "Removed external browser (Safari) fallback from
    `OIDExternalUserAgentIOS`. If `ASWebAuthenticationSession` fails to start
    (e.g., Guided Access is enabled), the authorization flow now fails with an
    error", which is the README's sentence.
  - `OIDAuthorizationService.m`: audience (`:610-618`), expiry (`:644`), issue
    time within 600 s (`:47, :652-658`), nonce (`:678`), and no signature rule,
    as the README lists.
- **The SDK header.** `ASWebAuthenticationSessionCallback.h`: available from
  iOS 17.4, and for `https(host:path:)` "The host must be associated with the
  app using associated web credentials domains". The README's
  `webcredentials:` requirement is Apple's own wording.
- **Root README, `docs/native-apps.md`, table, fragment, workflows.** "Today
  there are six", the iOS row, the native-apps list entry, the fragment's "its
  sign-in is checked by hand in the iOS Simulator" (honest: the device path was
  not walked), and `samples.yml` ignoring `samples/ios/` alone, as it does
  `samples/android/`. `changelog.sh check` passes; "an iOS" is right.
- **Links.** `check-links.py`: 267 links in 22 files, all resolve.

## Nits

1. **"Plain http on loopback only": "App Transport Security blocks plain http
   for the whole app except to local addresses … so a discovery document that
   pointed elsewhere over http could not be followed either."** Apple's ATS
   documentation exempts some destinations from ATS altogether (I recall IP
   addresses and unqualified or `.local` names among them, but have not
   re-checked the current page), so "could not be followed" may not hold for
   an `http://` endpoint given as an IP address. Either verify it against
   Apple's "Preventing Insecure Network Connections" page and say so in
   `notes.md`, or narrow the sentence to "to a named host". Umbral's own
   discovery document never points anywhere but its issuer, so nothing is
   wrong in practice; the sentence just claims a little more than it has
   shown.
2. **"Your own app"** lists the bundle ID, the `Info.plist` scheme and the two
   URIs. Two more strings carry the old name: `CFBundleURLName` in
   `Info.plist`, and the Keychain `service` in `TokenStore.swift`. Neither
   breaks anything if left, but someone renaming the app will look for "every
   place", so one clause ("and, for tidiness, `CFBundleURLName` and
   `TokenStore`'s service name") saves them a search.
3. **"What you should see", the prompt.** The README is right that iOS uses
   the bundle name there, and it says the host replaces `localhost`. Add that
   the prompt follows **Product Name**, so a reader who renames the target, or
   follows "Your own app", knows why the prompt's name changed and the home
   screen's did not. Optional.

## notes.md

Complete under section 6: every statement against the code, AppAuth at a
named revision, the SDK headers or the catalogue; the Simulator walk on a named
device and OS with eight steps, including a relaunch, a second identity
without an email, and a cancelled prompt; what was not walked (a device,
Universal Links) said plainly; the CI choice of `macos-26` with its reason.
Agreed on all of it.

## Implementer's answers

1. **ATS sentence, taken (narrowed).** It now says a discovery document pointing to an ordinary host
   name over http, such as `http://auth.example.com`, could not be followed, without claiming
   anything about IP addresses or `.local` names.
2. **Your own app, taken.** It names `CFBundleURLName` beside `CFBundleURLSchemes`, and
   `TokenStore.swift`'s Keychain `service`, which can change with the rest or stay, since it only has
   to be unique within the app.
3. **Prompt name, taken.** The prompt bullet says the name is the target's **Product Name**, so a
   renamed target changes it.
