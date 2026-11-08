# iOS sample

A small iOS app (Swift, SwiftUI) that signs in through Umbral with
[AppAuth-iOS](https://github.com/openid/AppAuth-iOS), the reference library for
RFC 8252, *OAuth 2.0 for Native Apps*. In Umbral it is a
**Single-page or native app**: a public client, because anything inside an app
on a phone can be read. It follows the [shared contract](../README.md), adapted
to an app (see [A phone app](../README.md#a-phone-app)): the same three screens,
settings and protocol as every other sample. Why a phone app signs in this way,
in the system's sign-in browser and never in a WebView, is in
[Native and mobile apps](../../docs/native-apps.md).

## What to create in Umbral

Open **Apps** and click **Create app** with:

- **Name**: anything.
- **Redirect URI**: `com.cosmicchimps.umbral.sample:/callback`
- **Post-logout redirect URI (optional)**: `com.cosmicchimps.umbral.sample:/signout-callback`
- **App type**: **Single-page or native app**

Then copy the **Client ID**. There is no secret.

Both URIs use the app's custom scheme, `com.cosmicchimps.umbral.sample`, with a
single slash after the colon. Umbral matches them exactly:
`com.cosmicchimps.umbral.sample://callback` is a different URI, and Umbral
answers it with an error page. If you change the scheme (see
[Your own app](#your-own-app)), register the new URIs instead.

These are the same URIs as the [Android sample](../android/README.md)'s, so one
app in Umbral can serve both.

## How to run it

The app reads two settings, from one file:
[`UmbralSample/UmbralSettings.swift`](UmbralSample/UmbralSettings.swift).

| Setting | What it is |
| --- | --- |
| `UMBRAL_ISSUER` | The **Issuer URL** on the app's page, for example `https://auth.example.com`. |
| `UMBRAL_CLIENT_ID` | The app's **Client ID**. |

There is no `UMBRAL_CLIENT_SECRET` and no port. The settings are built into the
app, so build it again after changing them. With no client ID, **Sign in** and
**Private** say so instead of opening Umbral.

The file is part of the repository, so your own issuer and client ID would show
up in `git status`. To keep them out of your commits, tell Git to leave your copy
alone:

```sh
git update-index --skip-worktree UmbralSample/UmbralSettings.swift
```

Undo it before you pull a change to the file:

```sh
git update-index --no-skip-worktree UmbralSample/UmbralSettings.swift
```

You need a Mac with [Xcode](https://developer.apple.com/xcode/) 26 or later,
which brings the iOS Simulator. The app runs on iOS 18 or later. Xcode fetches
AppAuth itself, through Swift Package Manager, at the exact version recorded in
the project. There is no Docker way to run an app; the app runs on a phone or in
the Simulator.

**In the Simulator, with the quick-start Umbral.** This is the way to try it
against the Umbral of the [quick start](../../README.md#quick-start-a-first-login-on-your-own-machine)
on `http://localhost:8080`. The Simulator shares your Mac's network, so
`localhost:8080` in the app and in its browser sheet is port 8080 of your Mac:
keep `UMBRAL_ISSUER=http://localhost:8080` and set `UMBRAL_CLIENT_ID`. Open
`UmbralSample.xcodeproj` (in this folder) in Xcode, pick an iPhone simulator as
the run destination, and click **Run**. From the command line instead, in this
folder:

```sh
xcodebuild -project UmbralSample.xcodeproj -scheme UmbralSample \
  -destination 'generic/platform=iOS Simulator' -derivedDataPath build build
xcrun simctl boot 'iPhone 17'
open "$(xcode-select -p)/Applications/Simulator.app"
xcrun simctl install booted build/Build/Products/Debug-iphonesimulator/UmbralSample.app
xcrun simctl launch booted com.cosmicchimps.umbral.sample
```

`xcrun simctl list devices available` lists the simulators your Xcode has, if
it has no iPhone 17.

Plain http works only on a loopback address, as with Umbral itself: the app
refuses `http://` for any other host (see [How it works](#how-it-works)).

**On an iPhone, with a real Umbral.** On the phone, `localhost` is the phone,
so the quick-start Umbral is out of reach: set `UMBRAL_ISSUER` to the
`https://` address of an Umbral the phone can reach. In Xcode, select the
**UmbralSample** target, pick your team under **Signing & Capabilities**
(signing a build for your own phone needs no paid membership), plug the phone
in, pick it as the run destination and click **Run**. The phone needs
**Developer Mode** turned on (in **Settings**, **Privacy & Security**), which iOS
offers once Xcode has tried to run an app on it.

Then open **Umbral sample** on the device, if Xcode has not already.

## What you should see

- The home screen shows a heading, the buttons **Public** and **Private**, and
  **Sign in**.
- **Public** opens for anyone.
- **Private** first shows iOS's own prompt, **"UmbralSample" Wants to Use
  "localhost" to Sign In** (iOS names the app by its bundle name there, and
  shows your Umbral's host name in place of `localhost`): tap **Continue**. A browser sheet then opens on Umbral's
  sign-in page, titled **Sign in to** and the app's name. After you sign in,
  the sheet closes and the private screen shows a table with `sub`, `name` and
  `idp`, and `email` and `email_verified` when Umbral has an address for you.
- **Sign out** shows the same prompt (iOS asks before any page of the issuer
  opens in the sheet, the end-session page included), then the sheet opens on
  Umbral's end-session endpoint and closes again at once, and the home screen
  shows **Sign in** again. **Private** then sends you to Umbral again.

If Safari remembers you at Google or GitHub, signing in can take a single tap.

## How it works

The code is in [`UmbralSample/`](UmbralSample/): `ContentView.swift` (the
screens), `UmbralAuth.swift` (AppAuth), `TokenStore.swift` (where the tokens are
kept), `UmbralSettings.swift` (the two settings) and `Info.plist` (the redirect
scheme and the plain-http rule).

- **Discovery.** Only the issuer is set. AppAuth reads the endpoints from
  `/.well-known/openid-configuration`
  (`OIDAuthorizationService.discoverConfiguration(forIssuer:)`), the end-session
  endpoint included. None is written by hand.
- **The system's sign-in browser.** AppAuth opens the sign-in page in an
  `ASWebAuthenticationSession`: a Safari sheet that belongs to the system, shows
  the real address, and whose pages the app cannot read. The app has no
  `WKWebView`. When the session cannot start (with Guided Access on, for
  example), the sign-in fails with an error; AppAuth no longer falls back to
  opening Safari.
- **PKCE, `state` and `nonce`.** `OIDAuthorizationRequest` creates them for
  every sign-in and the sample never turns them off: it uses PKCE with `S256`,
  AppAuth checks `state` when the sheet hands the redirect back, and
  `OIDAuthState.authState(byPresenting:presenting:prefersEphemeralSession:callback:)`
  exchanges the code and checks the `id_token` (its issuer, audience, expiry,
  issue time and `nonce`). The sample reads the claims from that `id_token`.
  AppAuth does not check its signature: the token comes straight from the token
  endpoint over the TLS connection the app opened, and OpenID Connect allows a
  client to rely on that instead. On the `http://localhost` trial there is no
  TLS either, which is acceptable only because Umbral and the Simulator are on
  one machine.
- **Scopes.** `openid`, `profile` and `email`.
- **The redirect.** AppAuth gives the session the scheme
  `com.cosmicchimps.umbral.sample`, and the session itself catches the redirect
  to either URI above and closes the sheet. The scheme is also registered for
  the app in `Info.plist` (`CFBundleURLTypes`), so that iOS opens this app with
  such an address, and `UmbralSampleApp.swift` hands it to AppAuth
  (`onOpenURL`).
- **No secret.** The token request has no client authentication, as a public
  client's should.
- **Tokens in the Keychain.** AppAuth's `OIDAuthState` (the tokens and the
  discovery document) is kept as one Keychain item, never in `UserDefaults`,
  which is a plain file in the app's folder. The item is readable only while the
  device is unlocked, and never leaves it: it is not synced to iCloud Keychain
  and not restored onto another device from a backup
  (`kSecAttrAccessibleWhenUnlockedThisDeviceOnly`).
- **No refresh token.** The sample does not ask for `offline_access`. When the
  `id_token` has expired, the app counts as signed out and **Private** asks
  again; Umbral may then send you straight back if its session in Safari is
  still alive.
- **Sign-out.** The app forgets its tokens first, then opens Umbral's
  end-session endpoint in the browser sheet with the `id_token` as
  `id_token_hint`, the post-logout redirect URI and a `state`
  (`OIDEndSessionRequest`), so that Umbral's session in Safari ends too. Umbral
  redirects to the registered post-logout URI, and the sheet closes. If you
  cancel the prompt or close the sheet instead, the app is still signed out, but
  Umbral's session in Safari is still alive, so the next sign-in may come back
  without showing Umbral's page.
- **Plain http on loopback only.** The app refuses an `http://` issuer for any
  host but `localhost` and `127.0.0.1`, before it sends anything. Below that,
  App Transport Security blocks plain http for the whole app except to local
  addresses (`NSAllowsLocalNetworking` in `Info.plist`), so a discovery document
  that pointed elsewhere over http could not be followed either.

For what each claim means, see [Integrate an app](../../README.md#integrate-an-app).

## A private browser session

`UmbralAuth.prefersEphemeralSession`, in `UmbralAuth.swift`, is `false`: the
sheet shares Safari's cookies, so Umbral's session, and the person's session at
Google or GitHub, carry over from one sign-in to the next and to other apps.
Set it to `true` to give each sign-in a browser session of its own
(`prefersEphemeralWebBrowserSession`), shared with nothing and thrown away when
the sheet closes. Then:

- the person sees Umbral's page every time, and their provider may ask for their
  password again;
- iOS does not show its "Wants to Use … to Sign In" prompt;
- nothing is left behind in Safari, so there is no Umbral session to end at
  sign-out: forgetting the tokens is all it takes. The sample still opens the
  end-session endpoint, which does no harm.

See [Staying signed in](../../docs/native-apps.md#staying-signed-in) for the
trade-off.

## Universal Links instead of a custom scheme

Any app can declare the same custom scheme. PKCE keeps a code handed to the
wrong app useless, since only this one has the verifier, which is why a custom
scheme is enough here. An `https://` redirect URI that only your app can claim,
such as `https://app.example.com/oauth/callback`, is stronger. iOS offers two
ways to claim an address, and the sign-in uses the second:

- A [Universal Link](https://developer.apple.com/documentation/xcode/supporting-universal-links-in-your-app):
  the `applinks:` service of an associated domain, which makes iOS open your app
  for that address when it is opened anywhere else.
- An `ASWebAuthenticationSession` that waits for an `https://` address:
  `ASWebAuthenticationSession.Callback.https(host:path:)`, from iOS 17.4. The
  session then catches the redirect to that host and path itself, as it does
  for the custom scheme, and Apple requires the host to be associated with the
  app through the `webcredentials:` service.

Either way it needs:

1. A domain you control, serving
   `https://app.example.com/.well-known/apple-app-site-association`, which names
   the app (your team ID and the bundle ID, `TEAMID.com.cosmicchimps.umbral.sample`)
   under `webcredentials` (and `applinks`, for a Universal Link).
2. The **Associated Domains** capability on the app, with
   `webcredentials:app.example.com` (and `applinks:app.example.com`).
3. That `https://` address as the redirect URI in the code and as the
   **Redirect URI** in Umbral, character for character.

AppAuth 3.0.0 cannot use it out of the box: its `OIDExternalUserAgentIOS` gives
the session a custom scheme only, so it never waits for an `https://` address.
You write your own `OIDExternalUserAgent`, a small class that starts the
`ASWebAuthenticationSession` with `.https(host:path:)` and, when it completes,
passes the address to AppAuth's session (`resumeExternalUserAgentFlow(_:)`), and
hand it to `OIDAuthState.authState(byPresenting:externalUserAgent:callback:)`.

iOS fetches the association file through Apple's servers when the app is
installed, so test it on a real install, signed with your team, before you rely
on it.

## Your own app

To start from this sample, change the bundle ID and the scheme together: the
target's **Bundle Identifier** in Xcode, the scheme in `Info.plist`
(`CFBundleURLSchemes`), and the two URIs in `UmbralAuth.swift`. Use the reverse
of a domain you own as the scheme, so that no other app is likely to pick it,
and register the new URIs in Umbral.
