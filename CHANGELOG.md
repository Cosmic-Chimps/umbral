# Changelog

What changed in each release, for people who run Umbral. Newest first. A release's notes on
GitHub are its section here: `.github/scripts/changelog.sh notes <version>` prints it, and the
release workflow refuses a tag when there is nothing under Unreleased to release.

Each section has the changes that matter to whoever runs Umbral, in this order and only as
needed: Added, Changed, Fixed, Security, then Upgrade when an upgrade needs more than pulling the
new image. Add to "Unreleased" as you work; a release renames it.

## Unreleased

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
