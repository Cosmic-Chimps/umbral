# Android sample

A small Android app (Kotlin, Jetpack Compose) that signs in through Umbral with
[AppAuth-Android](https://github.com/openid/AppAuth-Android), the reference
library for RFC 8252, *OAuth 2.0 for Native Apps*. In Umbral it is a
**Single-page or native app**: a public client, because anything inside an app
on a phone can be read. It follows the [shared contract](../README.md), adapted
to an app (see [A phone app](../README.md#a-phone-app)): the same three screens,
settings and protocol as every other sample. Why a phone app signs in this way,
in the system's browser and never in a WebView, is in
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

## How to run it

The app reads two settings, from one file: [`umbral.properties`](umbral.properties),
next to this README.

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
git update-index --skip-worktree umbral.properties
```

Undo it before you pull a change to the file:

```sh
git update-index --no-skip-worktree umbral.properties
```

You need the Android SDK and a JDK 17 or later: [Android Studio](https://developer.android.com/studio)
brings both. There is no Docker way to run an app; the app runs on a phone or an
emulator.

**With an emulator and the quick-start Umbral.** This is the way to try it
against the Umbral of the [quick start](../../README.md#quick-start-a-first-login-on-your-own-machine)
on `http://localhost:8080`. Keep `UMBRAL_ISSUER=http://localhost:8080`, set
`UMBRAL_CLIENT_ID`, start an emulator (from Android Studio's Device Manager),
and from this folder run:

```sh
adb reverse tcp:8080 tcp:8080
./gradlew installDebug
```

`adb reverse` makes `localhost:8080` on the emulator reach port 8080 of your
computer, for the app and for the browser alike, so the issuer is the same
address on both sides. It works the same way for a phone plugged in over USB
with USB debugging on. Run it again after restarting the emulator. Without it,
the app shows **Could not reach Umbral** when you sign in.

Plain http works only on a loopback address, as with Umbral itself: the app
refuses `http://` for any other host (see [How it works](#how-it-works)).

**With a real Umbral**, set `UMBRAL_ISSUER` to its `https://` address and run
`./gradlew installDebug`. No `adb reverse` is needed.

**From Android Studio**, open this folder (`samples/android`) as a project and
run the `app` configuration.

Then open **Umbral sample** on the device.

## What you should see

- The home screen shows a heading, the buttons **Public** and **Private**, and
  **Sign in**.
- **Public** opens for anyone.
- **Private** opens a browser sheet (a Custom Tab) on Umbral's sign-in page,
  titled **Sign in to** and the app's name. After you sign in, the sheet closes
  and the private screen shows a table with `sub`, `name` and `idp`, and `email`
  and `email_verified` when Umbral has an address for you.
- **Sign out** opens the browser sheet on Umbral's end-session endpoint, which
  closes again at once, and the home screen shows **Sign in** again. **Private**
  then sends you to Umbral again.

If the browser remembers you at Google or GitHub, signing in can take a single
tap.

## How it works

The code is in [`app/src/main/java/com/cosmicchimps/umbral/sample/`](app/src/main/java/com/cosmicchimps/umbral/sample/):
`MainActivity.kt` (the screens), `UmbralAuth.kt` (AppAuth) and `TokenStore.kt`
(where the tokens are kept).

- **Discovery.** Only the issuer is set. AppAuth reads the endpoints from
  `/.well-known/openid-configuration` (`AuthorizationServiceConfiguration.fetchFromIssuer`),
  the end-session endpoint included. None is written by hand.
- **The system's browser.** AppAuth opens the sign-in page in a Custom Tab of
  the person's default browser, or in the browser itself when it has no Custom
  Tabs. The app has no WebView.
- **PKCE, `state` and `nonce`.** AppAuth creates them for every sign-in and the
  sample never turns them off: it uses PKCE with `S256`, checks `state` when the
  browser hands the redirect back, and checks the `id_token` (its issuer,
  audience, expiry and `nonce`) after the code exchange. The sample reads the
  claims from that `id_token`. AppAuth does not check its signature: the token
  comes straight from the token endpoint over the TLS connection the app opened,
  and OpenID Connect allows a client to rely on that instead. On the
  `http://localhost` trial there is no TLS either, which is acceptable only
  because Umbral and the app are on one machine.
- **Scopes.** `openid`, `profile` and `email`.
- **The redirect.** `manifestPlaceholders["appAuthRedirectScheme"]` in
  [`app/build.gradle.kts`](app/build.gradle.kts) gives the scheme to AppAuth's
  own `RedirectUriReceiverActivity`, whose intent filter catches both URIs above.
  The app declares nothing else for it.
- **No secret.** The token request has no client authentication, as a public
  client's should.
- **Tokens in the Android Keystore.** AppAuth's `AuthState` (the tokens and the
  discovery document) is encrypted with an AES-GCM key that is created in the
  Android Keystore and never leaves it, and only the encrypted form is written
  to the app's private preferences. Jetpack's `EncryptedSharedPreferences` would
  do the same, but Google has deprecated it in favour of the Keystore. Backups
  are turned off, so the tokens never leave the phone.
- **No refresh token.** The sample does not ask for `offline_access`. When the
  `id_token` has expired, the app counts as signed out and **Private** asks
  again; Umbral may then send you straight back if its session in the browser is
  still alive.
- **Sign-out.** The app forgets its tokens first, then opens Umbral's
  end-session endpoint in the browser sheet with the `id_token` as
  `id_token_hint`, the post-logout redirect URI and a `state`
  (`EndSessionRequest`), so that Umbral's session in that browser ends too.
  Umbral redirects to the registered post-logout URI, and the sheet closes.
  If you close the sheet by hand instead, the app is still signed out, but
  Umbral's session in the browser is still alive, so the next sign-in may come
  back without showing Umbral's page.
- **Plain http on loopback only.** The app's network security configuration
  allows cleartext only to `localhost` and `127.0.0.1`, and the connection
  AppAuth uses refuses `http://` for any other host, so a mistyped issuer cannot
  send anything in the clear. For an issuer on `http://localhost`, AppAuth's own
  check that the `id_token`'s issuer is `https://` is skipped; for any other
  issuer it stays on.

For what each claim means, see [Integrate an app](../../README.md#integrate-an-app).

## App Links instead of a custom scheme

Any app can declare the same custom scheme, and Android may then hand the code
to it. PKCE keeps that code useless to the other app, since only this one has
the verifier, which is why a custom scheme is enough here. An
[Android App Link](https://developer.android.com/training/app-links) is
stronger: an `https://` redirect URI, such as
`https://app.example.com/oauth/callback`, that Android hands only to the app
that proves it belongs to that domain. It needs:

1. A domain you control, serving
   `https://app.example.com/.well-known/assetlinks.json`, which names the app's
   package and the SHA-256 fingerprint of its signing certificate.
2. In the app's manifest, AppAuth's `RedirectUriReceiverActivity` declared
   again, as AppAuth's README does, with `tools:node="replace"` (and
   `xmlns:tools="http://schemas.android.com/tools"` on `<manifest>`) and an
   intent filter for that address with `android:autoVerify="true"`:

   ```xml
   <activity
       android:name="net.openid.appauth.RedirectUriReceiverActivity"
       android:exported="true"
       tools:node="replace">
       <intent-filter android:autoVerify="true">
           <action android:name="android.intent.action.VIEW" />
           <category android:name="android.intent.category.DEFAULT" />
           <category android:name="android.intent.category.BROWSABLE" />
           <data android:scheme="https"
                 android:host="app.example.com"
                 android:path="/oauth/callback" />
       </intent-filter>
   </activity>
   ```

3. That `https://` address as the redirect URI in the code and as the
   **Redirect URI** in Umbral, character for character.

`tools:node="replace"` puts this declaration in place of the library's own,
whose intent filter catches the custom scheme from `appAuthRedirectScheme`. The
merged manifest then has the `https://` filter only, and the placeholder is no
longer used. Without `replace`, the two declarations merge: both filters stay,
the custom scheme included, and `appAuthRedirectScheme` must stay set in
`app/build.gradle.kts`, since the library's manifest refers to it. Either way,
`./gradlew assembleDebug` and the merged manifest
(`app/build/intermediates/merged_manifest/debug/processDebugMainManifest/AndroidManifest.xml`)
show what the app will catch, without a domain.

Android checks the association when the app is installed. If the check fails,
or the address is opened before it has run, the link opens in the browser
instead of the app, and the sign-in stays there. The most common cause is the
wrong fingerprint: an app signed through Google Play's app signing needs Play's
signing certificate fingerprint in `assetlinks.json` (in Play Console), not the
upload key's. Test it on a real install of a release build before you rely on it.

## Your own app

To start from this sample, change the package name and the scheme together:
`applicationId` and `appAuthRedirectScheme` in `app/build.gradle.kts`, and the
two URIs in `UmbralAuth.kt`. Use the reverse of a domain you own as the scheme,
so that no other app is likely to pick it, and register the new URIs in Umbral.
