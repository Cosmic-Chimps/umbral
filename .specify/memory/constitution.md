<!--
SYNC IMPACT REPORT
==================
Version change: 2.0.0 → 2.1.0
Rationale: MINOR. Principle VII gets a clarification, which materially expands
its guidance. Spec 006 (specs/006-allowed-email-domains) lets the operator
limit, per connector or per instance, which verified email domains may sign in.
That is a rule on who can become an Umbral user, like disabling a user. It is not
an authorization model. The exclusion list itself does not change, so this is
not a MAJOR amendment.

Principles:
  I.   Three Concepts, No More                     unchanged
  II.  One Artifact, Modest Hardware               unchanged
  III. Standards Over Hand-Rolled Protocol Code    unchanged
  IV.  Secure by Default, Not by Configuration     unchanged
  V.   (Removed in 2.0.0)                          unchanged
  VI.  Documentation Is the Support Channel        unchanged
  VII. The Default Answer Is No                    CLARIFIED (new bullet)

Sections changed:
  - Principle VII: a bullet says that limiting which verified email domains may
    sign in decides who can be a user and is not authorization. A per-app rule
    on who may use an app, and any rule on what a user may do, stay excluded.
  - Version line: 2.1.0, Last Amended 2026-10-15.

Related change outside this file:
  - specs/001-umbral-mvp/spec.md, FR-025 ("Any enabled user may use any app")
    gets an "Amended by 006" sentence. A domain list names people, never apps.

Templates requiring updates:
  ✅ .specify/templates/plan-template.md: the gate table lives in this file. No
     edit required.
  ✅ .specify/templates/spec-template.md: no edit required.
  ✅ .specify/templates/tasks-template.md: no edit required.

Plans invalidated: none. Specs 001 to 005 stay valid; 001 FR-025 is amended as
above.

Deferred items: none.
-->

# Umbral Constitution

Umbral is a self-hosted OpenID Connect provider whose only job is social login.
It exists because the gap between "wire OAuth into every app by hand" and "run
Keycloak" is real and nobody fills it. Every rule below protects that gap.

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

Umbral implements OpenID Connect by delegating to a certified library, not by
writing protocol cryptography.

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
  admin logins MUST be recorded in a structured security log.

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

Feature requests that do not serve "social login, self-hosted, simple" are
declined, and declining them is the job.

- The following are permanently out of scope: SAML, LDAP, RBAC or any
  authorization model, MFA, password-based local accounts, user federation,
  custom login flow builders, multi-tenancy, and admin audit trails beyond the
  structured security log of Principle IV.
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

**Rationale**: The vision names scope creep as the highest-likelihood risk. A
risk that high needs a rule, not good intentions.

## Technology Constraints

- **Language and runtime**: F# on .NET, ASP.NET Core.
- **Web framework**: Oxpecker, with htmx for interactivity. The admin UI MUST be
  server-rendered. A JavaScript build step, bundler, or SPA framework MUST NOT
  be introduced.
- **OIDC server**: OpenIddict (see Principle III).
- **Storage**: SQLite, in the single mounted data directory. A second storage
  engine MUST NOT be added.
- **Upstream connectors at launch**: Discord, GitHub, Google. Connector
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
| Protocol safety | Does this hand-roll any token, signature, or key handling that OpenIddict provides? |
| Secure default | Is the secure behavior the default, with no operator action required? |
| Documentation | Does the task list include the doc change for every user-visible behavior? |
| Scope | Is this on the Principle VII exclusion list? |

Testing discipline:

- Every OIDC protocol behavior MUST have an automated test that exercises the
  real endpoint, not a mocked handler.
- Every connector MUST be testable without real provider credentials; an
  abstraction seam for the upstream HTTP calls is mandatory.
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

**Version**: 2.1.0 | **Ratified**: 2026-10-09 | **Last Amended**: 2026-10-15
