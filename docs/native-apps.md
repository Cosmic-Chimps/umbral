# Native and mobile apps

How an iPhone, Android or desktop app signs people in through Umbral. There is
nothing Umbral-specific to install: Umbral is a standard OpenID Connect server,
and the app uses its platform's ordinary OAuth library (AppAuth on iOS and
Android, or whatever your framework wraps it in). This page says what that
library needs from Umbral and what people see.

Back to the [README](../README.md#integrate-an-app). The shared rules every
sample follows are in [samples/](../samples/README.md).

## How it works

The app never shows a sign-in form of its own. It opens Umbral in the system's
sign-in browser, a browser sheet that belongs to the system, not to the app:

- on iOS and iPadOS, `ASWebAuthenticationSession`;
- on Android, a Custom Tab;
- on a desktop, the person's default browser.

Then:

1. The app opens Umbral's authorization endpoint (read from the discovery
   document, `<issuer>/.well-known/openid-configuration`) with a PKCE challenge,
   `state` and `nonce`.
2. The person signs in on Umbral's sign-in page, with whichever connectors the
   app offers: Google, GitHub, a passkey and so on.
3. Umbral redirects to the app's redirect URI with a code. The system hands that
   address back to the app and closes the browser sheet.
4. The app exchanges the code at the token endpoint with its PKCE verifier, and
   gets an `id_token` and an access token. The claims are the same as for any
   app: see [Integrate an app](../README.md#integrate-an-app).

This is the pattern of RFC 8252, *OAuth 2.0 for Native Apps*, which AppAuth
implements.

## Why not a sign-in form in the app, or a WebView

Umbral offers no native sign-in screen and no component to embed, on purpose.
Signing in inside an embedded WebView, or in a form the app draws, is the wrong
way for a phone app, for these reasons:

- **RFC 8252 requires an external browser.** In a WebView the app can read
  everything typed into the page, the provider's password included, which is
  exactly what the person must not have to trust it with.
- **Google refuses sign-in in an embedded WebView.** It answers with an error
  page instead of its sign-in page, so the Google connector would fail.
- **A form of the app's own would be a custom login flow,** which Umbral does not
  offer: people sign in at the provider or with a passkey, on Umbral's page.
- **The system browser shows the real address,** so the person can see they are
  on Umbral's host and not on a look-alike. It also keeps what they already have
  there: their session at Google or GitHub, and their passkeys.

To make the sign-in page look like your app, brand it: an app's logo and accent
colour go on its own sign-in page (see
[Brand an app's sign-in page](branding.md)).

## In Umbral: create the app

On the **Apps** screen, click **Create app** and pick the app type
**Single-page or native app**. The form describes it as:

> Your app runs entirely on the person's device, where anything shipped can be
> read: a React, Vue or Angular app with no back end, a mobile or desktop app, a
> command-line tool. You get a client ID only, no secret; the app proves itself
> with PKCE.

That is why there is no secret: anything inside an app on a phone can be read by
whoever has the app, so a secret there would be no secret. Umbral makes the app
a public client, and PKCE is what proves that the app redeeming a code is the one
that asked for it. Umbral requires PKCE with `S256` for every app: it refuses an authorization
request without it, and one that uses the `plain` method.

Fill in the **Redirect URI** (next section) and, for signing out, the
**Post-logout redirect URI (optional)**. The app needs the **Issuer URL** and
the **Client ID** from the app's page, and nothing else.

## Redirect URIs

An app has one redirect URI, and Umbral matches it **exactly**, character for
character: scheme, host, port, path and a trailing slash all count, and
wildcards are refused. A request with any other value gets an Umbral error page,
not a redirect. The one exception is the port of a desktop app's loopback
address, under [Desktop apps](#desktop-apps). Two kinds work for a phone app:

- **A custom scheme**, such as `com.example.app:/callback`. Use the reverse of a
  domain you own, so no other app is likely to pick the same scheme. It is the
  simplest to set up. Its weakness is that any app can declare the same scheme,
  and the system may hand the code to it. PKCE keeps that code useless to the
  other app, since only yours has the verifier.
- **A claimed `https://` link**, such as `https://app.example.com/oauth/callback`:
  iOS Universal Links or Android App Links. The system gives it only to the app
  that proves it belongs to that domain, so no other app can claim it. It is the
  stronger choice when you control a domain and can publish the association file
  the platform asks for.

Register exactly what the app sends. `com.example.app:/callback` and
`com.example.app://callback` are two different URIs to Umbral, and so are
`https://app.example.com/oauth/callback` and the same address with a capital
letter in the host.

What each platform needs in the app itself (the scheme in `Info.plist`, the
intent filter in the Android manifest, the association file for a claimed link)
is in each sample.

## Staying signed in

When someone signs in, Umbral keeps a session for them in the browser that
showed its page. The next time an app on the same device opens Umbral in that
browser, and the session came through a connector this app offers, Umbral sends
the code straight back without showing its page. The person sees the browser
sheet open and close.

Two things decide whether that happens:

- **Umbral's session cookie lasts 14 days, renewed when it is used after the
  first week, so it never runs out while in regular use.** It
  is a persistent cookie, so it survives the browser closing. A non-ephemeral
  `ASWebAuthenticationSession` uses Safari's cookie store, and a Custom Tab uses
  the default browser's, so a second app on the same device can find the session
  the first one left. After 14 days without use, or once the person signs out or
  clears the browser's data, they see Umbral's page again, which costs them a
  tap when they are still signed in at the provider.
- **The app can ask for a private browser session.** On iOS,
  `prefersEphemeralWebBrowserSession` gives the sign-in a browser session of its
  own, shared with nothing and thrown away afterwards. The person then always
  sees Umbral's page, and their provider may ask for their password again. In
  return, iOS does not show its "wants to use *host* to sign in" prompt, and the
  sign-in leaves nothing behind in the browser.

To always show Umbral's page while sharing the browser, send `prompt=login` on
the authorization request: Umbral then ignores the session it has.

The tokens are a different matter: the app keeps them, in the Keychain or the
Android Keystore. Add the `offline_access` scope to get a refresh token, so the
app stays signed in without opening the browser at all.

## Signing out

Sign-out has two parts, and an app usually wants both:

1. **Forget the tokens** the app holds, and revoke the refresh token at the
   revocation endpoint (`revocation_endpoint` in discovery, `/connect/revoke`)
   so that it stops working at once.
2. **End Umbral's session** in the browser, so the next sign-in shows Umbral's
   page: open the end-session endpoint (`end_session_endpoint` in discovery,
   `/connect/logout`) in the same system browser the app signed in with, with
   `id_token_hint`, the app's **Post-logout redirect URI** and a `state`. Umbral
   ends its session and redirects to that URI with the `state`, and the system
   hands it back to the app, which closes the sheet.

Umbral redirects only to the post-logout redirect URI registered for the app,
matched exactly as the redirect URI is. Without one, or with any other address,
the person is still signed out of Umbral, but lands on Umbral's home page in the
browser sheet, and the app has to close it. A sign-out ends Umbral's session
only: the person stays signed in at Google, GitHub or whichever provider they
used (see [Signing out](integrate.md#signing-out)).

With a private browser session (above) there is no Umbral session left in the
browser, so step 1 is all there is to do.

## Passkeys from a phone

The **Passkey** connector works in the system's sign-in browser, the same as in
Safari or Chrome (see the [Passkey connector](connectors/passkey.md)). A passkey
belongs to Umbral's host name, not to the app: one made while signing in to one
app works in every app that offers the passkey connector, in the browser, and on
the person's other devices where their passkeys sync.

## Desktop apps

A desktop app can use a custom scheme, as a phone app does, or a loopback
redirect: it listens on a port of the computer itself, opens the system
browser, and the browser comes back to that port with the code.

Register the loopback address **without a port**, on a **Single-page or native
app**: `http://127.0.0.1/callback`, or `http://[::1]/callback` for IPv6. The app
then picks any free port when it starts a sign-in and sends it in the
`redirect_uri`, such as `http://127.0.0.1:53124/callback`. Umbral accepts any
port from 1 to 65535 there, as RFC 8252 §7.3 asks, and the token request sends
the same `redirect_uri`, port included. Everything else is still matched
exactly:

- **The scheme is `http`.** A loopback `https://` address is matched exactly,
  port included: no browser trusts a certificate for it anyway.
- **The host is the address `127.0.0.1` or `[::1]`.** `localhost` is matched
  exactly, port included. RFC 8252 §8.3 advises against it: it is a name, which
  can resolve to the other IP version than the one the app listens on, or be
  pointed elsewhere.
- **The path and the query** are matched character for character.
- **A loopback address registered with a port**, such as
  `http://127.0.0.1:8400/callback`, accepts only that port.
- **A Web application** gets no exception: the rule is for an app that runs on
  the person's computer and holds no secret.

The post-logout redirect URI follows the same rule: register
`http://127.0.0.1/signed-out` and send it with the port the app is listening on.

Have the app listen on the loopback address it registered only, not on every
network interface of the computer, and stop listening once the code has
arrived.

## Samples

Native samples are listed here as they are added, each following the shared
[sample contract](../samples/README.md), adapted to an app (see
[A phone app](../samples/README.md#a-phone-app)):

- [Android](../samples/android/README.md): Kotlin, Jetpack Compose and
  AppAuth-Android, in a Custom Tab, with a custom-scheme redirect URI.
- [iOS](../samples/ios/README.md): Swift, SwiftUI and AppAuth-iOS, in an
  `ASWebAuthenticationSession`, with a custom-scheme redirect URI.
- [.NET MAUI](../samples/maui/README.md): one C# project for Android and iOS,
  with `Duende.IdentityModel.OidcClient` and MAUI's `WebAuthenticator` (a
  Custom Tab or an `ASWebAuthenticationSession`), with a custom-scheme redirect
  URI.
