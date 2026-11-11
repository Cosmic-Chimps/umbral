# .NET MAUI sample

A small .NET MAUI app (C#, Android and iOS from one project) that signs in
through Umbral with
[`Duende.IdentityModel.OidcClient`](https://github.com/DuendeSoftware/foss) and
MAUI's `WebAuthenticator`, which opens the system's sign-in browser. In Umbral
it is a **Single-page or native app**: a public client with no secret. It
follows the [shared contract](../README.md), adapted to an app (see
[A phone app](../README.md#a-phone-app)): the same three screens, settings and
protocol as every other sample. Why a phone app signs in this way, in the
system's browser and never in a WebView, is in
[Native and mobile apps](../../docs/native-apps.md).

The [ASP.NET Core sample](../aspnetcore/README.md) is a **Web application**: a
confidential client, whose server keeps a secret. This app runs on the person's
device, where a secret could be read, so it has none and proves itself with
PKCE instead.

## What to create in Umbral

Open **Apps** and click **Create app** with:

- **Name**: anything.
- **Redirect URI**: `com.cosmicchimps.umbral.sample.maui:/callback`
- **Post-logout redirect URI (optional)**: `com.cosmicchimps.umbral.sample.maui:/signout-callback`
- **App type**: **Single-page or native app**

Then copy the **Client ID**. There is no secret.

Both URIs use the app's custom scheme, `com.cosmicchimps.umbral.sample.maui`,
with a single slash after the colon. Umbral matches them exactly:
`com.cosmicchimps.umbral.sample.maui://callback` is a different URI, and Umbral
answers it with an error page. If you change the scheme (see
[Your own app](#your-own-app)), register the new URIs instead.

The scheme differs from the [Android](../android/README.md) and
[iOS](../ios/README.md) samples' (`com.cosmicchimps.umbral.sample`) on purpose.
That way the three apps can be installed on one phone without two of them
claiming the same scheme, and each has its own app in Umbral.

## How to run it

The app reads two settings, from one file:
[`UmbralSettings.cs`](UmbralSettings.cs), next to this README.

| Setting | What it is |
| --- | --- |
| `Issuer` | The **Issuer URL** on the app's page, for example `https://auth.example.com`. |
| `ClientId` | The app's **Client ID**. |

There is no client secret and no port. The settings are built into the app, so
build it again after changing them. With no client ID, **Sign in** and
**Private** say so instead of opening Umbral.

The file is part of the repository, so your own issuer and client ID would show
up in `git status`. To keep them out of your commits, tell Git to leave your copy
alone:

```sh
git update-index --skip-worktree UmbralSettings.cs
```

Undo it before you pull a change to the file:

```sh
git update-index --no-skip-worktree UmbralSettings.cs
```

You need the [.NET SDK](https://dotnet.microsoft.com/download) (version 10) and
the MAUI workloads for the platforms you want:

```sh
dotnet workload install maui-android maui-ios
```

For Android you also need the Android SDK and a JDK 21 (the Android tools
refuse newer ones): [Android Studio](https://developer.android.com/studio)
brings the SDK. For iOS you need a Mac with [Xcode](https://developer.apple.com/xcode/),
at the version the iOS workload asks for (`dotnet build` says so when it does
not match). There is no Docker way to run an app; the app runs on a phone or on
an emulator or simulator.

**On an Android emulator, with the quick-start Umbral.** This is the way to try
it against the Umbral of the [quick start](../../README.md#quick-start-a-first-login-on-your-own-machine)
on `http://localhost:8080`. Keep `Issuer` as `http://localhost:8080`, set
`ClientId`, start an emulator (from Android Studio's Device Manager), and from
this folder run:

```sh
adb reverse tcp:8080 tcp:8080
dotnet build -t:Run -f net10.0-android
```

`adb reverse` makes `localhost:8080` on the emulator reach port 8080 of your
computer, for the app and for the browser alike, so the issuer is the same
address on both sides. Run it again after restarting the emulator. Without it,
the app shows **Could not reach Umbral** when you sign in.

**In the iOS Simulator, with the quick-start Umbral.** The Simulator shares your
Mac's network, so `localhost:8080` in the app and in its browser sheet is port
8080 of your Mac, and the same `Issuer` works. Start a simulator, then from this
folder run:

```sh
dotnet build -t:Run -f net10.0-ios
```

**On a real device,** `localhost` is the phone, so the quick-start Umbral is out
of reach: set `Issuer` to the `https://` address of an Umbral the phone can
reach. An iPhone also needs a signing identity for your team; see Microsoft's
[guide to deploying a .NET MAUI app to iOS](https://learn.microsoft.com/dotnet/maui/ios/deployment/).

Plain http works only on a loopback address, as with Umbral itself: the app
refuses `http://` for any other host (see [How it works](#how-it-works)).

Then open **Umbral MAUI sample** on the device, if it has not opened already.

## What you should see

- The home screen shows a heading, the buttons **Public** and **Private**, and
  **Sign in**.
- **Public** opens for anyone.
- **Private** opens a browser sheet on Umbral's sign-in page, titled
  **Sign in to** and the app's name: a Custom Tab on Android, a Safari sheet on
  iOS. On iOS, the system first asks you to confirm that the app may use your
  Umbral's host name to sign in: tap **Continue**. After you sign in, the sheet
  closes and the private screen shows a table with `sub`, `name` and `idp`, and
  `email` and `email_verified` when Umbral has an address for you. If you close
  the sheet instead, you are back on the home screen.
- **Sign out** opens the browser sheet on Umbral's end-session endpoint, which
  closes again at once, and the home screen shows **Sign in** again. **Private**
  then sends you to Umbral again.

If the browser remembers you at Google or GitHub, signing in can take a single
tap.

## How it works

The code is in this folder. It has no XAML: `MainPage.cs`, `PublicPage.cs` and
`PrivatePage.cs` are the screens, `UmbralAuth.cs` is the sign-in logic,
`WebAuthenticatorBrowser.cs` opens the browser, `UmbralIdentityTokenValidator.cs`
checks the `id_token`, `TokenStore.cs` keeps the tokens and `UmbralSettings.cs`
holds the two settings. `Platforms/` has the Android and iOS files.

- **Discovery.** Only the issuer is set. OidcClient reads the endpoints from
  `/.well-known/openid-configuration`, the end-session endpoint included. None
  is written by hand, and every discovery check stays on, except that the https
  rule is relaxed for an `http://` issuer on a loopback address.
- **PKCE.** OidcClient creates a verifier and sends an `S256` challenge for
  every sign-in. The sample never turns it off. The token request has no client
  authentication, as a public client's should.
- **`state`.** OidcClient sends it and checks it when the browser hands the
  redirect back.
- **`nonce`.** OidcClient's code flow neither sends nor checks one, so
  `UmbralAuth` does: it generates a random nonce, sends it with the sign-in
  request, and refuses the sign-in when the `id_token`'s `nonce` claim is
  missing or different.
- **The `id_token`.** `UmbralIdentityTokenValidator` validates it: its signature
  against the keys of the discovery document, its issuer, its audience (the
  client ID) and its lifetime. The sample reads the claims from it. Scopes are
  `openid`, `profile` and `email`.
- **The system's sign-in browser.** `WebAuthenticator` opens the sign-in page in
  a Custom Tab on Android and in an `ASWebAuthenticationSession` on iOS. Both
  show the real address and the app cannot read their pages. The app has no
  WebView.
- **Tokens in the secure store.** The `id_token`, the access token and the claims
  are kept with `SecureStorage`: the Keychain on iOS, a Keystore-backed store on
  Android. Nothing goes to `Preferences` or to a file, and the Android manifest
  turns backups off. There is no refresh token (the sample does not ask for
  `offline_access`): when the `id_token` has expired, the app counts as signed
  out and **Private** asks again.
- **Sign-out.** The app forgets its tokens first, then opens Umbral's
  end-session endpoint in the browser sheet with the `id_token` as
  `id_token_hint` and the post-logout redirect URI, so that Umbral's session in
  the browser ends too. If you close the sheet instead, the app is still signed
  out, but Umbral's session is still alive, so the next sign-in may come back
  without showing Umbral's page.
- **The redirect.** The custom scheme is registered twice: in
  `Platforms/Android/WebAuthenticationCallbackActivity.cs`, whose intent filter
  catches the redirect and hands it to `WebAuthenticator`, and in
  `Platforms/iOS/Info.plist` (`CFBundleURLTypes`).
- **Plain http on loopback only.** `UmbralAuth` refuses an `http://` issuer for
  any host that is not a loopback address, before it sends anything. Below that,
  Android's network security configuration
  (`Platforms/Android/Resources/xml/network_security_config.xml`) allows
  cleartext only to `localhost` and `127.0.0.1`, and iOS's App Transport
  Security blocks plain http except to local addresses (`NSAllowsLocalNetworking`
  in `Info.plist`).

For what each claim means, see [Integrate an app](../../README.md#integrate-an-app).

## Your own app

To start from this sample, change the scheme in four places, and the app id:

1. The two URIs in `UmbralAuth.cs`.
2. `DataScheme` in `Platforms/Android/WebAuthenticationCallbackActivity.cs`.
3. `CFBundleURLSchemes` (and `CFBundleURLName`) in `Platforms/iOS/Info.plist`.
4. `ApplicationId` in `UmbralSample.csproj`, which is the Android package name
   and the iOS bundle ID.

Use the reverse of a domain you own as the scheme, so that no other app is
likely to pick it, and register the new URIs in Umbral. Any app can declare the
same custom scheme, but PKCE keeps a stolen code useless: only this app has the
verifier. For a redirect that only your app can receive, see
[Native and mobile apps](../../docs/native-apps.md).
