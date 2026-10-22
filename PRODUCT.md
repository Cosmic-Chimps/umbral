# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

**Primary: the operator.** An indie developer, homelabber or maintainer of a
small community who runs Umbral on modest hardware (a Raspberry Pi is enough)
to sign people into several of their own apps. They meet Umbral in the
first-run setup, the operator sign-in, and the four operator screens: Apps,
Connectors, Users and Credential. Design work puts their experience first
(confirmed 2026-10-22).

**Secondary: the people who sign in.** Members of the operator's apps, often
non-technical friends, family or community members. They see the end-user
sign-in page (plain or branded by the app), the message pages, and the
self-service page where a person manages their own passkeys. Some of them
have no social provider at all and sign in with a passkey only.

**Tertiary: prospective adopters.** People reading the README or the GitHub
repository to decide whether to run Umbral instead of Keycloak, Authentik or
per-app OAuth wiring. There is no landing page; the README is the front door.

## Product Purpose

Umbral is a self-hosted OpenID Connect provider whose only job is signing
people in without passwords: with a social provider they already use
(Discord, GitHub, Google, or any OpenID Connect provider the operator runs),
or with a passkey that Umbral verifies itself. Apps talk to Umbral with any
standard OIDC library. The operator wires each provider up once, in one place,
instead of in every app.

It ships as one container with an embedded SQLite database: no Postgres, no
Redis, no sidecar. Success looks like a first login within a few minutes of
`docker run`, three operator screens that cover everything, three
configuration values per app, and an operator who never has to touch login
again.

## Positioning

Umbral sits between "wire OAuth into each app" and "run Keycloak". It is the
only thing in that gap: self-hosted, passwordless sign-in only, with exactly
three concepts (App, Connector, User) and nothing else. It answers "who is
this person?"; what they may do is the app's job.

The exclusion list is part of the position, not a roadmap: no realms,
tenants, groups, roles or permissions, no SAML or LDAP, no MFA, no local
password accounts, no login-flow builder, and it will not get them. A passkey
is neither MFA nor a password account: it is the whole sign-in. A neighbouring
product that offers those cannot truthfully claim this simplicity.

## Operating Context

- Runs as a Docker container on `linux/amd64` or `linux/arm64`, designed to
  idle under 150 MB (160 MB with an external secret store). Tagged releases
  are published to GHCR; the README also covers deploying on Caravana.
- A real deployment needs a domain and HTTPS; `http://` is refused for
  anything but `localhost`, which is for trying it on a laptop only.
- The data directory (`/data`, holding `umbral.db`) is the whole state.
  Backing it up is the operator's one chore. The database upgrades itself on
  start; an older image refuses a newer database and says so in the logs.
- First run prints a setup token in the container log; the operator opens the
  setup page, pastes it, and chooses an operator credential.
- Connector credentials come from OAuth apps the operator creates at Discord,
  GitHub, Google or their own OIDC provider, following the walkthroughs in
  `docs/connectors/`. Secrets can optionally live in AWS Secrets Manager,
  Google Cloud Secret Manager, Azure Key Vault or BellaBaxter
  (`docs/secret-stores/`).
- The passkey connector needs no account anywhere, only HTTPS (or localhost)
  and a device that holds passkeys. The operator chooses invitations only
  (default) or open sign-up; invitations are single-use links good for seven
  days.
- An app can brand its own sign-in page with a logo and an accent colour, set
  on the app's card. The "Secured by Umbral" line with the Gnomon cannot be
  removed or recoloured; it is how a person tells the real page from a
  lookalike. The error page shown when an app's request fails its checks
  never carries app branding.
- Documentation is the support channel (`README.md`, `docs/`, `CHANGELOG.md`
  for people who run it). Product rules live in
  `.specify/memory/constitution.md` (version 3.0.0, 2026-10-19); features are
  specified in `specs/NNN-*/` with spec, plan and tasks.

## Capabilities and Constraints

**Concepts and screens**

- App: a program that signs in through Umbral; gets a client ID and a client
  secret shown once. Created in a dialog over the Apps list.
- Connector: Umbral's identity at an upstream provider; several per provider
  are allowed. Kinds: `discord`, `github`, `google`, `oidc`, and `passkey`
  (one per instance, Umbral is the provider).
- User: a person who has signed in at least once. A provider user can add up
  to ten passkeys to the same account; a passkey-only user has no email.
  Operators can invite a user to add a passkey as recovery from a lost
  device.
- Pages: first-run setup, operator sign-in, Apps (with app detail and
  branding), Connectors, Users, Credential, the end-user sign-in page, the
  home page, `/account/passkeys`, and message or error pages. The operator
  screens have a side rail with the four screens and Sign out; the end-user
  pages have a name bar and a footer instead.
- The sign-in page remembers the way in, not the person: the last connector
  used in that browser comes first with a "Last used" badge (unreleased, on
  `main` as of 2026-10-22).
- Theme follows the system by default; a person can choose System, Light or
  Dark, remembered by a cookie in that browser only.

**Technical constraints that future surfaces inherit**

- F# on .NET 10 with Oxpecker, OpenIddict, fido2-net-lib and SQLite.
  Server-rendered HTML, htmx and one small script. Views are F# in
  `src/Umbral/Views/`; the text catalogue `src/Umbral/Views/Texts.fs` wins
  whenever a mockup and the product disagree on wording.
- Styling is Tailwind compiled offline: design tokens in
  `src/Umbral/Assets/tailwind.config.js`, component classes in
  `umbral.input.css`, regenerated with `src/Umbral/Assets/build-css.sh` and
  committed as `umbral.css`. The image build fails on a stale file.
- Nothing is loaded from a third party: stylesheet, scripts and fonts (a
  Latin subset of about 84 KB, SIL Open Font License, licences shipped) are
  served by the instance, under a strict Content-Security-Policy. No page
  may request anything from another host, so no one else learns that someone
  opened a sign-in page.
- Every control must work with JavaScript off (the theme control is a plain
  form; the logo crop is the one JavaScript-only convenience, with a
  documented no-JavaScript path).
- Logos are stored as PNG only, never fetched from a URL, never SVG.
- Memory budget: idle under 150 MB.

**Terminology to keep**: operator, app, connector, user, passkey, invitation,
operator credential, setup token, "Secured by Umbral", the Umbra look, the
Gnomon mark, the lowercase `umbral` wordmark. "Social login" is the old
scope; the current phrase is "passwordless sign-in".

## Brand Commitments

Confirmed binding on 2026-10-22, all four:

- **The name and the wordmark.** "Umbral", written lowercase as `umbral` in
  the wordmark. The name means threshold; an umbra is the shadow a thing
  casts.
- **The Gnomon mark.** A sun, an upright, and the shadow it casts on a
  threshold line, as shipped in the product. The providers are the light,
  Umbral stands in it, every app receives the same shadow.
- **The Umbra look.** Warm gold on near-black in the dark theme, on paper in
  the light theme; both themes always; the palettes are fixed in
  `specs/007-umbra-look/spec.md` (FR-005, FR-006). Three self-hosted
  typefaces: Bricolage Grotesque (headings and wordmark, extra bold),
  Schibsted Grotesk (text), Spline Sans Mono (identifiers and secrets).
  Reference artboards: `design/umbra/*.dc.html`. The Stitch export in
  `design/` is not a reference.
- **The writing voice.** Plain, precise, in full sentences, and as ready to
  say what Umbral is not as what it is. No marketing register. The README is
  the model.

## Evidence on Hand

- **A running instance of the maintainer's own.** Umbral signs real people
  into real apps for the maintainer's group. Surfaces may say so and describe
  it; they may not name people or apps. The passkey connector exists because
  a member of that group, asked which social provider they would use, said
  "none" and asked for FIDO (recorded in the constitution).
- **Public feedback.** Issues and discussions on
  `github.com/Cosmic-Chimps/umbral` (per the maintainer, 2026-10-22). They
  may be quoted with attribution to the public source. Nothing private is
  quotable.
- **Captures.** None are stored. Any surface that needs a screenshot or
  recording takes a fresh one from a local instance (README quick start) or
  from the artboards in `design/umbra/`.
- **Written material.** `README.md` (with a provider walkthrough per
  connector), `docs/connectors/`, `docs/secret-stores/`, `docs/vision.md`,
  `CHANGELOG.md`, and the specs in `specs/`.
- **Absences, not to be filled in.** No named testimonials, no adoption or
  usage numbers, no benchmark beyond the memory target, no public demo
  instance, no pricing (MIT licence, free), no landing page.

## Product Principles

1. **Three concepts, no more.** App, Connector, User. Anything that needs a
   fourth is not Umbral's.
2. **One artifact on modest hardware.** One container, one data directory,
   a Raspberry Pi is enough. Nothing in the UI may need a heavier runtime.
3. **Secure by default, not by configuration.** Safe without the operator
   doing anything, nothing leaves the instance, and the page a person signs
   in on can always be told from a fake.
4. **Documentation is the support channel.** If the operator needs to ask,
   the product or the README failed. Screens and messages explain
   themselves; wording comes from the text catalogue.
5. **The default answer is no.** Feature pressure toward RBAC, MFA, SAML and
   flow builders is expected and declined.

## Accessibility & Inclusion

- WCAG 2.x AA in both themes: text at 4.5:1 or better against what it sits
  on; input borders and focus indicators at 3:1 or better (spec 007, FR-007).
- Every control operable by keyboard with visible focus and an accessible
  name; current state exposed (the theme control is the reference case).
- Everything works with JavaScript off and follows the system theme by
  default.
- An app's accent colour gets black or white text chosen by Umbral for
  readability, and a low-contrast colour is saved with a warning rather than
  refused.
- Passkey-only users exist and have no email; nothing may assume an email or
  a social account.
