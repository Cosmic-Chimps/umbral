<!--
SYNC IMPACT REPORT
==================
Version change: 2.1.0 → 3.0.0
Rationale: MAJOR. The product's purpose is redefined: Umbral was "social login
only"; it becomes "passwordless sign-in: social login or a passkey". The
principles now permit something they did not: a person can become an Umbral
user with no social provider at all, signing in with a passkey Umbral verifies
itself. The exclusion list does not shrink: MFA and password-based local
accounts stay out, and a bullet says why a passkey is neither.

What the product becomes (required for amendments touching Principle VII):
Umbral is still the one place an operator wires sign-in up for every app, and
still not an identity platform. For a group whose members will not use Google,
GitHub or Discord, it no longer forces a provider on them: the operator enables
the built-in passkey connector, invites people (or opens sign-up), and every app
behind Umbral gets them as ordinary OIDC users. Someone who signs in with a
provider can add a passkey to the same account. Umbral now stores one more kind
of credential (passkey public keys, which are not secrets), and gains one
self-service screen: a user managing their own passkeys.

Trigger: a member of the maintainer's group, asked which social provider they
would use, answered "none: I prefer a cryptographic key, implement FIDO".

Principles:
  I.   Three Concepts, No More                     CLARIFIED (passkeys are a Connector kind; a User's passkeys belong to the User)
  II.  One Artifact, Modest Hardware               unchanged
  III. Standards Over Hand-Rolled Protocol Code    EXPANDED (WebAuthn through a maintained library)
  IV.  Secure by Default, Not by Configuration     EXPANDED (passkey ceremonies, invitations, security log)
  V.   (Removed in 2.0.0)                          unchanged
  VI.  Documentation Is the Support Channel        unchanged
  VII. The Default Answer Is No                    REDEFINED (scope now includes passkeys; MFA and passwords still excluded)

Sections changed:
  - Preamble: "only job is social login" becomes passwordless sign-in through a
    social provider or a passkey.
  - Principle I: a bullet keeps the concept count at three.
  - Principle III: a bullet requires a maintained WebAuthn library and forbids
    hand-written attestation or assertion verification.
  - Principle IV: passkey changes are credential changes in the security log;
    ceremonies need a single-use challenge, an origin and RP ID check, and user
    verification; invitations are single-use, expiring and stored hashed.
  - Principle VII: the scope phrase; a passkey is neither MFA nor a password
    account; open sign-up versus invitations is the operator's choice per
    connector and is not authorization; users may manage their own passkeys.
  - Technology Constraints: fido2-net-lib named; ASP.NET Core Identity passkeys
    not used (no Identity store); the built-in passkey connector noted.
  - Quality gates: Protocol safety also covers the WebAuthn library; passkey
    ceremonies are tested through real endpoints with a software authenticator.
  - Version line: 3.0.0, Last Amended 2026-10-19.

Templates requiring updates:
  ✅ .specify/templates/plan-template.md: the gate table lives in this file. No
     edit required.
  ✅ .specify/templates/spec-template.md: no edit required.
  ✅ .specify/templates/tasks-template.md: no edit required.

Plans invalidated: none. No plan in specs/ depends on "social login only"; the
existing specs stay valid.

Deferred items (land with the passkey feature, Principle VI):
  - README.md opening ("whose only job is social login") and "What Umbral is
    not": describe passkeys once they ship.
  - docs/vision.md: note the 3.0.0 scope.
  - A spec for the passkey connector: enrolment (invitation or open, per
    connector), adding passkeys to an existing user, recovery by a new
    invitation, what claims a passkey-only user has (no verified email, so
    email-domain limits do not apply to them).
-->

# Umbral Constitution

Umbral is a self-hosted OpenID Connect provider whose only job is signing people
in without passwords: through a social provider they already use, or with a
passkey. It exists because the gap between "wire OAuth into every app by hand"
and "run Keycloak" is real and nobody fills it. Every rule below protects that
gap.

## Core Principles

### I. Three Concepts, No More

Umbral's entire conceptual surface is **App**, **Connector**, and **User**. A
person who understands those three understands Umbral completely.

- Umbral MUST NOT introduce realms, tenants, organizations, groups, roles,
  permissions, policies, flows, stages, or any other grouping abstraction.
- Any new user-facing noun MUST be justified in the feature spec against this
  principle, and the justification MUST explain why it cannot be expressed with
  the existing three.
- The admin UI MUST remain navigable in three top-level screens: Apps,
  Connectors, Users.
- A passkey is a way to sign in, so passkeys are a **Connector** of a built-in
  kind, with Umbral itself as the provider; an app offers it like any other
  connector. A person's passkeys belong to their **User**, as the provider
  identities they sign in with do. Neither is a new concept.

**Rationale**: Authentik and Keycloak are not disliked for being wrong; they are
disliked for being large. Conceptual count is the product. A fourth concept is a
larger regression than a bug.

### II. One Artifact, Modest Hardware

Umbral ships as a single container image with an embedded SQLite database.

- `docker run` with no sidecar, no external database, and no message broker MUST
  produce a working Umbral. A first social login MUST be reachable within five
  minutes of that command, counting the time to paste provider credentials.
- Umbral MUST run on a Raspberry Pi-class machine: `linux/amd64` and
  `linux/arm64` images, and an idle resident set under 150 MB.
- Adding a required runtime dependency outside the container is a breaking change
  to this principle and requires the amendment procedure in Governance.
- All state MUST live in one mounted data directory, so that backup is `cp` and
  restore is `cp` back.

**Rationale**: The audience is a homelabber with a Pi and five hobby apps.
Operational weight is the thing they are escaping.

### III. Standards Over Hand-Rolled Protocol Code

Umbral implements OpenID Connect and passkeys by delegating to established
libraries, not by writing protocol cryptography.

- Token issuance, signature, encryption, key management, and the authorization
  code flow MUST be handled by OpenIddict. Umbral code MUST NOT construct, sign,
  or validate a JWT by hand.
- Umbral MUST publish a correct discovery document at
  `/.well-known/openid-configuration` and a JWKS endpoint, so that any standard
  OIDC client library works against it with no Umbral-specific code.
- The authorization code flow with PKCE is the only flow offered. Implicit and
  password grants MUST NOT be implemented.
- An integrating app MUST need no more than three values to configure: issuer
  URL, client ID, client secret.
- Passkey registration and sign-in (WebAuthn / FIDO2) MUST be verified by a
  maintained WebAuthn library (fido2-net-lib today). Umbral code MUST NOT parse
  attestation objects or verify assertion signatures by hand. An app sees a
  passkey sign-in as an ordinary OIDC sign-in; it needs no WebAuthn code.

**Rationale**: "Self-hosted auth written by one person" is a scary sentence. The
answer is to write as little auth as possible. The parts a bug would be
catastrophic in are the parts that must not be ours.

### IV. Secure by Default, Not by Configuration

A default Umbral deployment MUST be a safe Umbral deployment. Security MUST NOT
depend on the operator reading a hardening guide.

- Upstream provider client secrets and app client secrets MUST be encrypted at
  rest. Secrets MUST NOT be readable from the admin UI after they are saved; the
  UI offers replacement, never retrieval.
- Signing and encryption keys MUST persist across container restarts, so that a
  restart does not invalidate live sessions, and MUST be stored encrypted in the
  data directory.
- Every state-changing admin request MUST carry CSRF protection. Every upstream
  OAuth round trip MUST use a single-use `state` value, and a `nonce` wherever
  the provider returns an `id_token`.
- Redirect URIs MUST be matched exactly against registration. Wildcards MUST NOT
  be accepted.
- Authentication failures, credential changes, app registration changes, and
  admin logins MUST be recorded in a structured security log. Adding or removing
  a passkey is a credential change.
- Every passkey ceremony MUST use a single-use challenge issued by the server,
  MUST check the origin and relying-party ID against the configured public
  origin, and MUST require user verification (the authenticator's PIN or
  biometric), since a passkey is the whole sign-in, not a second step.
- An invitation to register a passkey MUST be single-use, MUST expire, and MUST
  be stored only as a hash.

**Rationale**: This product is the front door to everything behind it. An
operator who misconfigures it will not find out until it matters.

### V. (Removed in 2.0.0)

This principle required a YAML file equivalent to the admin UI, importable and
exportable. Spec 004 dropped it on 2026-10-10. Every feature had to extend the
file, its validation and its round-trip tests. The file undid admin changes
on every restart. And an export could never carry app secrets, which are stored
only as hashes. The admin UI is now the one place configuration changes, and
the data directory is the whole state of an instance (Principle II): copying it
moves or rebuilds an instance, secrets included.

The number stays so that references to Principles VI and VII keep pointing at
them.

### VI. Documentation Is the Support Channel

Umbral has no support team. Documentation is therefore a shipped deliverable,
not an afterthought.

- A feature is not done until its documentation is written. "Done" in a task
  list MUST include the doc change.
- Each connector MUST ship a provider setup walkthrough covering where to create
  the OAuth app, which redirect URI to paste, and which scopes to request.
- Every error message shown to an operator MUST say what to do next, not only
  what went wrong.
- The README MUST get a reader from zero to a working login without leaving the
  page.

**Rationale**: The adoption path is a stranger at 1am with a Pi and no patience.
Documentation is the only thing present at that moment.

### VII. The Default Answer Is No

Feature requests that do not serve "passwordless sign-in (social login or a
passkey), self-hosted, simple" are declined, and declining them is the job.

- The following are permanently out of scope: SAML, LDAP, RBAC or any
  authorization model, MFA, password-based local accounts, user federation,
  custom login flow builders, multi-tenancy, and admin audit trails beyond the
  structured security log of Principle IV.
- A passkey is a sign-in method in its own right, never a second step after
  another one: a passkey account is not a password account, and offering a
  passkey is not MFA. Asking for a passkey after a social login, or for anything
  after a passkey, stays excluded as MFA.
- A request to add any of the above MUST be answered by pointing at Keycloak or
  Authentik, which solve that problem well. That is a successful outcome, not a
  lost user.
- Reversing an entry on that list requires a MAJOR constitution amendment with
  written rationale, because it redefines what the product is.
- Limiting, per connector or per instance, which verified email domains may
  sign in is not an authorization model. It decides who can be an Umbral user,
  the same way disabling a user does, and it says nothing about what anyone may
  do. Any rule on which people may use a given app, and any rule on what a user
  may do, stays excluded.
- Whether a passkey connector lets anyone create an account or only people the
  operator invites is the operator's choice, per connector. Like a domain limit,
  it decides who can be a user and nothing else. Invitations are links the
  operator hands out; Umbral does not send email.
- A signed-in user may add passkeys to their own account and remove them. That
  is the only self-service account management there is.

**Rationale**: The vision names scope creep as the highest-likelihood risk. A
risk that high needs a rule, not good intentions.

## Technology Constraints

- **Language and runtime**: F# on .NET, ASP.NET Core.
- **Web framework**: Oxpecker, with htmx for interactivity. The admin UI MUST be
  server-rendered. A JavaScript build step, bundler, or SPA framework MUST NOT
  be introduced.
- **OIDC server**: OpenIddict (see Principle III).
- **Passkeys**: fido2-net-lib (see Principle III). ASP.NET Core Identity's
  passkey support is not used: Umbral has no Identity user store.
- **Storage**: SQLite, in the single mounted data directory. A second storage
  engine MUST NOT be added.
- **Upstream connectors at launch**: Discord, GitHub, Google; the built-in
  passkey connector is added by the amendment of 3.0.0. Connector
  credentials and enabled state MUST be editable at runtime from the admin UI
  and take effect without restarting the process.
- **Deployment**: one OCI image, multi-architecture, published to a public
  registry.

Any deviation from this stack MUST be recorded in the plan's Complexity Tracking
table with the simpler alternative that was rejected and why.

## Development Workflow & Quality Gates

Umbral follows the spec-driven cycle: constitution → specify → plan → tasks →
implement. Specs describe behavior and stay implementation-agnostic; plans carry
the technology decisions.

Every plan MUST pass this Constitution Check before Phase 0 research and again
after Phase 1 design:

| Gate | Question the plan must answer |
| --- | --- |
| Concept count | Does this introduce a user-facing noun beyond App, Connector, User? |
| Single artifact | Does this add a runtime dependency outside the container, or state outside the data directory? |
| Protocol safety | Does this hand-roll any token, signature, or key handling that OpenIddict or the WebAuthn library provides? |
| Secure default | Is the secure behavior the default, with no operator action required? |
| Documentation | Does the task list include the doc change for every user-visible behavior? |
| Scope | Is this on the Principle VII exclusion list? |

Testing discipline:

- Every OIDC protocol behavior MUST have an automated test that exercises the
  real endpoint, not a mocked handler.
- Every connector MUST be testable without real provider credentials; an
  abstraction seam for the upstream HTTP calls is mandatory.
- Passkey registration and sign-in MUST be tested through the real endpoints
  with a software authenticator, including a wrong origin, a reused challenge
  and a missing user verification.
- A bug fix MUST add the test that would have caught it.

## Governance

This constitution supersedes other practice documents in this repository. Where
a README, a plan, or a task list disagrees with it, this file wins.

**Amendment procedure**: An amendment is a pull request that changes this file,
states the version bump and its rationale, and updates any plan or template the
change invalidates. Amendments touching Principle VII's exclusion list MUST
additionally explain what the product becomes as a result.

**Versioning policy**: Semantic versioning.

- MAJOR — a principle is removed or redefined in a way that permits what it
  previously forbade.
- MINOR — a principle or section is added, or existing guidance is materially
  expanded.
- PATCH — clarification, wording, or typo fixes with no change in meaning.

**Compliance review**: The Constitution Check table above is evaluated at plan
time and re-evaluated after design. Violations are either removed from the
feature or recorded in the plan's Complexity Tracking table with an explicit
justification; an unjustified violation blocks the plan.

**Version**: 3.0.0 | **Ratified**: 2026-10-09 | **Last Amended**: 2026-10-19
