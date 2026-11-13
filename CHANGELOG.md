# Changelog

What changed in each release, for people who run Umbral. Newest first. A release's notes on
GitHub are its section here: `.github/scripts/changelog.sh notes <version>` prints it.

Notes are not written here. Each change adds one file in `changelog.d/` (see
[its README](changelog.d/README.md)), and the release gathers those files into the new version's
section below and deletes them, so this file holds released sections only. The release workflow
refuses to release when `changelog.d/` has no notes.

Each section has the changes that matter to whoever runs Umbral, in this order and only as
needed: Added, Changed, Fixed, Security, then Upgrade when an upgrade needs more than pulling the
new image.

## 0.11.0 - 2026-11-13

### Added

- "Sign in with Apple": a built-in Apple connector. You enter the Services ID, the Team ID, the
  Key ID and the `.p8` key from Apple's developer site, and people can sign in with their Apple
  Account. Umbral signs Apple's client secret itself for each sign-in, from the key it keeps in
  the secret store, so there is no token to generate and nothing to renew every six months. The
  name Apple sends only the first time a person signs in is kept. Apple accepts only an https
  address on a real domain name as the return URL, and there is one Apple connector per
  instance. The button follows Apple's rules for it (its title, black or white) but does not
  carry Apple's logo yet. The walkthrough is `docs/connectors/apple.md`.
- Apple's logo on the "Sign in with Apple" button. Apple's licence for its logo artwork forbids
  redistributing it, so Umbral cannot ship it: you paste the SVG you download from Apple Design
  Resources into the new **Sign-in button logo** section of the Apple pane, and the button shows
  it. Umbral keeps only the logo's outline and refuses a file with anything else in it. Apple's
  rules and App Review expect the logo, so add it before you submit an app that offers Sign in
  with Apple. See step 4 of `docs/connectors/apple.md`.
- "Sign in with Bluesky": a built-in Bluesky connector. People type their handle and sign in
  at their own server, whether Bluesky hosts the account or it is a self-hosted AT Protocol
  server. There is nothing to create at any provider: you turn the connector on, and choose
  **Any server** or **Only these servers**. Apps receive `name`, `preferred_username` (the
  handle), `picture` and `profile`, and no email; a person who changes handle stays the same
  user, because Umbral identifies them by their DID. It needs Umbral at an https address
  without a port that servers can reach from the internet; the pane says so otherwise.
  There is one Bluesky connector per instance. The walkthrough is
  `docs/connectors/bluesky.md`.
- "Sign in with Microsoft": a built-in Microsoft connector. You register an app in the Microsoft
  Entra admin center, enter its client ID and client secret, and choose who may sign in:
  personal Microsoft accounts, the people of one organization (by its tenant ID), any work or
  school account, or both kinds. Umbral checks each person's tenant against that choice, and
  checks the identity token's issuer against exactly the tenant it names. People are identified by
  their tenant and account together, so replacing the app registration keeps everyone. An email
  address counts as verified only when Microsoft's optional claim `xms_edov` says so, which you
  add in the registration; without it no address is verified. Narrowing the choice turns away people no
  longer admitted the next time an app authorizes them or refreshes their token. Microsoft client secrets expire (180
  days is the recommended lifetime, 2 years the longest): the walkthrough says how to renew one.
  There is one Microsoft connector per instance, no avatar, and the button carries no Microsoft
  logo yet. The one-tenant case can stay on the OpenID Connect connector. The walkthrough is
  `docs/connectors/microsoft.md`.

### Changed

- The sign-in page's GitHub button now shows the GitHub logo, in white or black as GitHub's
  logo rules require, and the passkey button shows a key. Google, Discord, Fediverse and
  OpenID Connect buttons keep the link icon: Google's rules allow its "G" only on Google's own
  button colours and typeface, and Discord's terms do not cover a sign-in button.
- When Umbral cannot reach a provider, the warning now gives the reason in one pair of brackets
  instead of brackets inside brackets: "Umbral could not reach Microsoft (the connection failed:
  Connection refused, 127.0.0.1:9)". The OpenID Connect provider warning puts its reason in
  brackets too, like the Apple and Microsoft ones.

### Upgrade

- The database becomes schema version 10. An older image refuses to start on it: take a backup
  before you upgrade.
- The database becomes schema version 11. An older image refuses to start on it: take a backup
  before you upgrade.
- The database becomes schema version 12, which adds one nullable column. It is automatic and
  needs no action. An older image refuses to start on it: take a backup before you upgrade.
- The database becomes schema version 13, which adds one nullable column. It is automatic and
  needs no action. An older image refuses to start on it: take a backup before you upgrade.

## 0.10.0 - 2026-11-11

### Added

- An app can have up to 10 redirect URIs and up to 10 post-logout redirect URIs,
  one for each place it runs (your machine, staging, production). Add and remove
  them on the app's page on the **Apps** screen. The client ID and the secret
  stay the same, so there is no need to delete and register an app again to
  change its redirect URI. Each one is still matched exactly, an app keeps at
  least one redirect URI, and every addition and removal is written to the
  security log.
- A documentation site, at <https://cosmic-chimps.github.io/umbral/>, with a sidebar and search. It
  is built from the Markdown in `docs/`, which still reads the same on GitHub, and it loads nothing
  from another host: no analytics, no CDN script, no external font.
- A desktop app can sign in through a loopback redirect on any free port, as RFC 8252 §7.3 asks.
  Register the address with no port, such as `http://127.0.0.1/callback` or
  `http://[::1]/callback`, on a **Single-page or native app**, and the app may send it with any
  port. Only `http`, only those two addresses (never `localhost`), and the path and query must
  match exactly. A loopback address registered with a port still accepts only that port, and the
  post-logout redirect URI follows the same rule. See "Desktop apps" in `docs/native-apps.md`.
- Samples: an Android (Kotlin, AppAuth) sample in `samples/android/`: a Jetpack Compose app
  signing in through Umbral with AppAuth-Android as a **Single-page or native app**, in a Custom
  Tab, with a custom-scheme redirect URI and sign-out through Umbral. CI builds it; its sign-in
  is checked by hand on a device or an emulator.
- Samples: a FastAPI sample in `samples/fastapi/`: a Python app signing in through Umbral
  with Authlib's Starlette client as a **Web application**. The samples workflow walks it too.
- Samples: an iOS (Swift, AppAuth) sample in `samples/ios/`: a SwiftUI app signing in through
  Umbral with AppAuth-iOS as a **Single-page or native app**, in an `ASWebAuthenticationSession`,
  with a custom-scheme redirect URI and sign-out through Umbral. CI builds it; its sign-in is
  checked by hand in the iOS Simulator.
- Samples: a .NET MAUI sample in `samples/maui/`: one C# project for Android and iOS signing in
  through Umbral with `Duende.IdentityModel.OidcClient` and MAUI's `WebAuthenticator` as a
  **Single-page or native app**, with a custom-scheme redirect URI, tokens in `SecureStorage`
  and sign-out through Umbral. CI builds the Android target; its sign-in is checked by hand.
- Samples: a Next.js sample in `samples/nextjs/`: a Next.js app (App Router) signing in
  through Umbral with Auth.js (`next-auth` v5) as a **Web application**. Sign out also ends
  the Umbral session. The samples workflow walks it too.

### Changed

- The "Documentation" link at the foot of every page opens the documentation site,
  <https://cosmic-chimps.github.io/umbral/>, in place of the README on GitHub.
- A message about a secret store that fails (the warning at startup, the notice on Connectors, a
  save that is refused, a connector whose secret cannot be read) gives the address of that store's
  page on the documentation site. It used to say `docs/secret-stores/`, a folder the image does
  not have.
- The documentation site carries a Content-Security-Policy, so the browser itself refuses anything
  a page would load from another host. Until now only a check of the built files guaranteed it.
- The README is shorter: it keeps what Umbral is, the requirements, the quick start to a first
  login and a short "Integrate an app", and links to the documentation site for the rest. The
  sections that left it are pages under `docs/`: an anchor into the old README (for example
  `#troubleshooting`) no longer resolves, and the README's Contents block says where each one is.
- When an address you add to an app's redirect URIs or post-logout redirect
  URIs is refused as invalid, the field keeps what you typed, so you can correct
  it instead of typing it again.

### Fixed

- The warning the upgrade to schema version 9 writes to the log, when it sets `email_verified`
  back to false for some users, gives the address of the Troubleshooting page. It named a
  Troubleshooting section of the README, which moved to the documentation site.
- The **Choose File** button of an app's logo upload has the same plain outline as the other
  buttons, instead of the browser's two-tone bevelled border.

### Security

- A **Single-page or native app** registered with a loopback address and no port accepted that
  address on any other port, also for `localhost` and `https://` addresses, and for sign-out as
  well as sign-in. The OpenIddict library behind Umbral did this by default, while the
  documentation said the port had to match. Umbral now applies only the narrower rule above. If an
  app registered `http://localhost/...` or `https://127.0.0.1/...` without a port and relied on
  another port, register `http://127.0.0.1/...` instead.

## 0.9.0 - 2026-11-06

### Added

- A browser-only app (a single-page app with no server of its own) can now sign in through
  Umbral: the discovery document, the keys and the token, userinfo and revocation endpoints
  answer cross-origin requests from any origin, preflight included. There is nothing to
  configure.
- A page on signing people in from an iPhone, Android or desktop app:
  `docs/native-apps.md`. It covers the system browser (never an embedded WebView),
  the **Single-page or native app** type, custom-scheme and claimed `https://`
  redirect URIs, staying signed in, signing out, passkeys and loopback redirects.
- Samples: a browser-only app (public client) sample in `samples/spa/`: a single-page app
  with no server of its own, signing in through Umbral with `oidc-client-ts` as a
  **Single-page or native app**, with no client secret. The samples workflow walks it too.
- Samples: `samples/` has one small app per stack that signs in through Umbral with the
  stack's ordinary OpenID Connect library, starting with ASP.NET Core. Every sample has the
  same three pages (home, public, private with the claims) and signs out through Umbral, and
  CI runs each one against a real Umbral in a headless browser before every release.

### Changed

- The image sets `DOTNET_GCgen0size=0x200000`, a smaller first-generation budget for the .NET
  garbage collector. Every configuration idles about 4 MB lower, so an instance with an operator
  passkey in use now idles around 145 MB instead of 149 MB on arm64. It is set in the image, so a
  container gets it without any setting of yours; running the app outside the image is not
  supported, and would not get it.
- A person's Umbral session now survives the browser closing: its cookie is persistent and
  lasts 14 days from the last use, as the session already did. Signing in to a second app
  after a browser restart no longer shows Umbral's page again, and a second app on the same
  phone can now find the session the first one left. Closing the browser no longer signs anyone out: on a shared computer, people
  sign out at `/connect/logout`. Disabling or deleting someone still ends their session at
  their next sign-in. A session from before the upgrade keeps its browser-session cookie
  until the person next signs in through a connector.

## 0.8.1 - 2026-11-05

### Changed

- The operator sign-in page offers **Sign in with a passkey** only once you have added a
  passkey on the **Credential** page. Before that it shows the password form alone, so the
  button never opens your browser's passkey picker to find nothing. A passkey could never be
  added from the sign-in page; that has not changed.

## 0.8.0 - 2026-11-05

### Added

- You can add passkeys to the operator credential, up to 10, on the **Credential** page, and sign
  in to the admin area with **Sign in with a passkey**. They need no connector, never appear on
  **Users**, and never sign anyone in to an app.
- You can turn password sign-in off once you have a passkey. While it is off, no password is
  accepted, and the stored password comes back unchanged when you turn it on again.
- `reset-credential --forget-passkeys` also removes every operator passkey, for a stolen device.

### Changed

- The **Credential** page has three parts: **Passkeys**, **Password sign-in** and **Password**.
- `reset-credential` turns password sign-in back on and keeps the operator passkeys. Its output
  says "password", not "credential".
- Release notes are now written as one file per change in `changelog.d/`, and the release gathers them
  into the new version's section of `CHANGELOG.md`. Nothing changes for running Umbral.

### Fixed

- The README's **Integrate an app** said an app gets no `email` when the provider had not
  verified it. Umbral has always sent such an address with `email_verified: false`; the README
  now says so, and that the email is the first one Umbral saw. It also said that two OpenID
  Connect providers never bring people together: a verified email links them as it does for
  every other connector. The section now lists the endpoints, what signing out does, and what an
  app sees when someone is disabled or deleted.

### Security

- An email address a person never proved could be marked as verified, and the real owner of that
  address could then be joined into that person's account. Umbral keeps the first address a
  provider gives for a user, but it set `email_verified` whenever the provider verified *any*
  address. So someone could sign in through Discord with another person's address, unverified,
  then change their Discord address to one of their own and verify it. Umbral then sent apps the
  first address with `email_verified: true`. When the real owner of that address later signed in
  through another provider that verifies it, Umbral linked them into the first person's user,
  which could then sign in to every app as them. `email_verified` now becomes `true` only when a
  provider verifies the address Umbral stores for the user. Every version from 0.1.0 has this.

### Upgrade

- Operator passkeys need nothing at upgrade: there is none yet, and password sign-in stays on
  until you turn it off.
- The database becomes schema version 9. An older image refuses to start on it: take a backup
  before you upgrade.
- On its first start, Umbral sets `email_verified` back to `false` for every user whose stored
  address no linked provider account asserts as verified today, and logs how many users that
  was, as a warning when it is not 0. Nobody is linked to those users by email any more, and
  apps see `email_verified: false` for them until a provider verifies that address again.
- Joins that already happened are not undone. To look for one, run the query in the README's
  Troubleshooting entry **A user may hold someone else's provider account**.

## 0.7.0 - 2026-10-27

### Added
- Every page links to the source and the documentation: the footer has **Source on GitHub** and
  **Documentation**, except on an app's branded sign-in page, where **Secured by Umbral** stays
  alone.

### Changed
- The home page's **Building an app?** card shows the whole issuer URL, wrapping if it must, with
  **Copy** under it, instead of a field too narrow to show more than its beginning.
- On the **Users** screen, the row of someone who already has a passkey offers **Create a
  recovery link** instead of **Invite to add a passkey**, with a line saying it is for a lost
  device, and the link it shows is labelled **Recovery link**. A row without a passkey keeps
  **Invite to add a passkey**, and its link is labelled **Invitation link**. The link and what it
  does are unchanged.
- The footer line reads **Umbral — self-hosted passwordless sign-in** instead of "self-hosted
  social login", and the home page says that apps can offer a passkey as well as an account at
  Discord, GitHub, Google, a Mastodon-compatible server or any OpenID Connect provider.

### Fixed
- On the **Users** screen, a passkey's **Remove** is a quiet button in the error colour instead of a
  second red one beside the user's **Delete**: removing a passkey can be undone by adding another,
  deleting the user cannot.
- Stopping sharing the Passkey connector while an app had no other way to sign in could not be
  confirmed: **Stop sharing anyway** answered that the settings were invalid. It now goes ahead.
- After deleting the last app, its notice is shown once, not twice.
- A connector saved by an older version under the slug `new` or `domains` could not be opened,
  changed or deleted on the Connectors screen, because those are now addresses of the screen. Its
  page is now at `/admin/connectors/_new` or `/admin/connectors/_domains`, and its entry in the
  list links there. Its slug, and so its callback URL, stay as they are.

### Security
- A crafted link to the admin screens could show a confirm button that acted on another app or
  user. Opened by a signed-in operator, a link to the Connectors screen could draw a red
  **Delete anyway** or
  **Disable anyway** button that deleted an app or disabled a user. A crafted link could also draw
  the **Save anyway** button of an app that leaves the app with no way to sign in, and the
  **Remove anyway** button that clears the allowed email domains of the instance. These buttons are
  now drawn only right after the operator's own change was refused, and only for that connector or
  app. Every version from 0.1.0 to 0.5.0 has this.
- The admin pages are no longer kept by the browser (`Cache-Control: no-store`), so **Back** cannot
  show a new client secret or a passkey invitation link again. On a narrow screen, `/admin/apps`
  and `/admin/connectors` no longer use up a new secret or invitation link in a pane the screen hides.
- Every response now carries `Referrer-Policy: same-origin`, so going from Umbral to another site
  (an identity provider, an app, a link in the footer) no longer tells that site the instance's
  address. The passkey invitation page keeps its stricter `no-referrer`. The page that posts a
  sign-in back to an app that asked for `response_mode=form_post` has no policy, as before, so the
  app still sees where the post comes from.

## 0.6.0 - 2026-10-26

### Added
- The Fediverse connector: people can sign in with an account on a Mastodon (4.3 and later) or
  GoToSocial server, typing a handle such as `@ana@mastodon.social` or just the server. Enable it from
  the **Fediverse** entry of the Connectors screen; there is nothing to create at any provider, because
  Umbral registers itself on each server the first time someone from it signs in. Choose **Any
  server** or **Only these servers** and list them. A server works when its OAuth metadata
  advertises PKCE (`S256`) and app registration, or when it is GoToSocial; Akkoma and Pleroma do no
  PKCE, Misskey and Sharkey have no app registration Umbral can use, and Mastodon before 4.3
  publishes no metadata, so those are refused with a message.
- Apps receive an ordinary sign-in with `idp` set to `fediverse`, and `name`, `preferred_username`
  (as `user@server`), `picture` and `profile` under the `profile` scope. A fediverse user has no
  `email`, and allowed email domains do not apply to them.
- A walkthrough, `docs/connectors/fediverse.md`, and Fediverse in the README.

### Upgrade
- The database becomes schema version 7. An older image refuses to start on it: take a backup
  before you upgrade.
- An instance with the Fediverse connector enabled idles under 150 MB, the ceiling the idle-memory
  test asserts on Linux.

## 0.5.0 - 2026-10-24

### Changed
- The **Apps** screen is a list and a detail pane: the apps are listed by name on the left, and
  the selected one's connection details, sign-in methods, branding and actions are on the right
  (one column below 1024px, with a link back to the list). Each app has its own address,
  `/admin/apps/<client id>`, where every change to it lands, its notice and a new secret included;
  **Create app** opens its form in the pane at `/admin/apps/new` instead of a dialog. Long redirect
  URIs now wrap instead of running past the screen on a phone.
- The **Connectors** screen is a list and a detail pane too: the connectors on the left (those
  set up, then the providers not set up yet, then **Passkey**, then **Allowed email domains**, the
  instance-wide list), those set up marked Enabled, Disabled, or with a warning sign when they
  need attention, and the selected one's set-up details, settings and actions on the right. Each
  connector has its own address, `/admin/connectors/<identifier>`, where every change to it lands;
  **Add connector** opens its form at `/admin/connectors/new`, and the allowed email domains are at
  `/admin/connectors/domains`. `new` and `domains` can no longer be used as a connector's
  identifier.

### Fixed
- An app card's **Sign-in page branding** heading now reads as the title of its section rather than
  a third field label beside **Logo** and **Accent colour**, and **Regenerate secret** and **Delete
  app** sit behind a hairline that closes the card, instead of hanging under the accent form.

## 0.4.0 - 2026-10-22

### Fixed
- The **Sign-in page branding** section of an app card no longer looks broken: **Remove logo**
  sits on the same row as **Save logo**, as a quiet red button rather than the filled one kept for
  **Delete app** (a logo can be uploaded again), the preview shows the logo above the sample
  sign-in button as the sign-in page does, and the no-JavaScript upload advice shows only to
  browsers without JavaScript.

## 0.3.0 - 2026-10-22

### Added
- The sign-in page shows which way this browser signed in last: that connector's button comes
  first with a **Last used** badge, above an "or" and the other buttons. The browser keeps only the
  connector's name (`umbral_last_connector`, a year), never an email or account, and only after a
  sign-in that worked. With a single connector, or one no longer offered, the page is as before.

## 0.2.0 - 2026-10-20

### Added
- The passkey connector: people can sign in with a passkey (a phone, laptop or security key,
  unlocked by fingerprint, face or PIN) and need no social provider. Enable it on the **Passkey**
  card of the Connectors screen; Umbral is the provider. Choose **Invitations only** (the default)
  or **Anyone can sign up**. Invite someone with a link shown once, good for 7 days and one use.
- A person who signs in with a provider can add passkeys to the same account, and remove them, on
  `/account/passkeys`, linked from the home page. An account holds up to 10.
- Recovery from a lost device: **Invite to add a passkey** on the person's row in Users. Same
  account, same `sub`.
- Apps receive an ordinary sign-in with `idp` set to `passkey`. A passkey-only user has no `email`.
- A walkthrough, `docs/connectors/passkey.md`, and a Passkeys section in the README.

### Fixed
- Tick boxes in the admin screens follow the dark and light themes instead of the browser's
  default look, and the list of an app's chosen connectors no longer sits flush under its options.

### Security
- User verification is required, challenges are single-use, and the origin and relying-party
  identifier are checked against `UMBRAL__PUBLIC_ORIGIN`.
- Invitations are stored hashed, single-use and expiring. Passkey requests are rate-limited like
  the other sign-in endpoints.
- New security events: `passkey_registered`, `passkey_removed`, `passkey_invitation_created`,
  `passkey_invitation_revoked`, and `sign_in_failed` with `passkey_refused;reason=…`.
- The framework's log categories are now capped, so an invitation link never reaches a log.

### Upgrade
- The database becomes schema version 6. An older image refuses to start on it: take a backup
  before you upgrade.
- An instance with the passkey connector in use idles under 160 MB (the ceiling the idle-memory
  test asserts on Linux); without it, under 150 MB as before.

## 0.1.1 - 2026-10-19

### Changed
- The Caravana guide in the README now uses a deploy token that can only deploy and read the
  `umbral` app (Caravana 0.8.1 and later), with a full API token as the fallback for older hosts.

### Fixed
- A tagged release deploys to Caravana again when its settings come from a Bella Baxter tenant
  that requires registered device keys. 0.1.0 was published but never reached Caravana.

## 0.1.0 - 2026-10-19

### Added
- Umbral is a self-hosted OpenID Connect provider for social login, in one container with an
  embedded SQLite database: no Postgres, no Redis. Your apps use any standard OIDC library with
  three values (issuer, client ID, client secret); the discovery document is at
  `/.well-known/openid-configuration`. It runs on `linux/amd64` and `linux/arm64`, a 64-bit
  Raspberry Pi included.
- Sign in with Discord, GitHub or Google: add a connector on the Connectors screen by pasting the
  OAuth application's client ID and secret. Each provider has a setup walkthrough in `docs/`.
- Sign in with your own OpenID Connect provider (Authentik, Keycloak, Pocket ID, Authelia and the
  like): give its issuer address and a short identifier, and everything else comes from the
  provider's discovery document. Two providers never merge people, and adding a provider back with
  the same issuer returns everyone to the same user.
- Apps get the authorization code flow with PKCE, exact redirect-URI matching, refresh tokens with
  `offline_access`, and a stable, opaque `sub`. The Apps screen creates an app, shows its secret
  once and can generate a new one.
- Several connectors per provider, and a choice of sign-in methods per app, so one app can show
  its own name on the provider's consent screen while the others share the default. Umbral warns
  before a change would leave an app with no way to sign in.
- A person who signs in through two providers with the same verified email is one user. An
  unverified email never links anyone. The Users screen can disable or delete users, and a
  disabled user's tokens stop working at once.
- Each app can have its own logo and accent colour on the sign-in page its people see, set on the
  app's card. The logo is uploaded, cropped in the browser and re-encoded by Umbral, never fetched
  from a web address.
- Allowed email domains: limit who can sign in to people whose provider-verified email is in a
  list, for the whole instance or for one connector. A refused person sees one neutral message,
  and every refusal is recorded in the security log.
- The Umbra look, in a dark and a light theme that follows your system or your choice, with its
  fonts served by Umbral itself, so no sign-in page asks a third party for anything.
- First-run setup with no default password: a one-time token in the container's log lets you
  choose the operator credential, which is not tied to any provider. A forgotten credential is
  replaced from the command line, and the setup can be scripted.
- Sign-in attempts are rate-limited per client address (an IPv6 address is counted per /64).
  `UMBRAL__TRUSTED_PROXIES` names the proxies whose `X-Forwarded-*` headers Umbral believes, so a
  client cannot choose its own address to avoid the limits.
- Connector client secrets can be kept in AWS Secrets Manager, Google Cloud Secret Manager, Azure
  Key Vault or Bella Baxter instead of encrypted in `/data`, chosen with `UMBRAL__SECRET_STORE`.
  Secrets are cached briefly, and the last one read is used for an hour if the manager can't be
  reached. A command moves existing secrets between stores. An instance with an external store
  idles under 160 MB rather than 150 MB, because it loads the manager's client library.
- A `/healthz` endpoint and a container health check, a security log of sign-ins and changes, and
  a compose file with Caddy that gets and renews the HTTPS certificate.
- Tagged versions are published as a multi-architecture image at `ghcr.io/<owner>/umbral`, with a
  GitHub release whose notes are this changelog, and can roll out to a Caravana host.
