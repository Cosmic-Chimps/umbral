# Feature Specification: Declare apps and the passkey connector at start

**Feature Branch**: `alta/twilight-fornax-23bd` (worktree branch, on a5pr's `alta/sturdy-partridge-kwqh` at eeae146; spec directory `specs/019-declared-apps`)

**Created**: 2026-11-14

**Status**: Security decisions cleared by the code reviewer with C1 and C2 (a962660); Declared marking answered by the designer; ready for the build

**Input**: Task `2026-11-14-aspire-integration-declare-apps-from-the-apphost-a5ap` (Aspire integration, Tier 2).

## Background

After a5pr, `aspire run` in `samples/aspire` starts Umbral and stops there.
The person then claims Umbral, enables a connector, creates the app, copies
the client ID and the once-shown secret into user secrets, and runs again.
Keycloak's Aspire integration removes the equivalent with `WithRealmImport`:
the AppHost declares the clients and the project that references one receives
its credentials. A developer trying Umbral on a laptop compares against that.

This feature lets whoever starts Umbral declare, in its environment, the apps
it serves and the passkey connector. Umbral applies the declaration when it
starts. The Aspire package writes the declaration from `umbral.AddApp(...)`
and `WithPasskeys(...)`, and hands each referencing project its issuer, client
ID and secret.

```csharp
var umbral = builder.AddUmbral("umbral").WithDataVolume().WithPasskeys(PasskeySignUp.Open);
var web = umbral.AddApp("web");
builder.AddProject<Projects.Web>("web").WithReference(web).WaitFor(umbral);
```

### Principle V, and why this is not the file spec 004 removed

Principle V once required a YAML file equal to the admin UI. Spec 004 removed
it because every feature had to extend the file, the file undid admin changes
at every restart, and an export could never carry app secrets. This feature
differs on each point:

- It covers apps and the passkey connector only, never a secret-bearing
  connector, users, the operator credential, branding or settings. A new
  feature extends it only if it adds a field to an app or to the passkey
  connector.
- It never undoes an admin change: what it declares is read-only in the admin
  area while declared, and what it does not declare stays the admin area's.
- It is input only. There is no export.
- It holds no secret. Each app secret comes from its own variable, and Umbral
  stores it as it stores every app secret, as an OpenIddict PBKDF2 hash.
- The old file stays dead: `UMBRAL:CONFIG_FILE` and an `umbral.yaml` in the
  data directory still change nothing (`NoConfigFileTests` keeps passing).

The sentence of Principle V "The admin UI is now the one place configuration
changes" is no longer true once this lands. See decision D11.

## Decisions Already Taken

From the task and the `umbral` session (2026-11-14); not open:

- Declared: apps (name, type, redirect URIs, post-logout redirect URIs,
  connector choice) and the passkey connector (enabled, sign-up mode).
- Not declared: secret-bearing connectors, users, the operator credential.
- Claiming stays manual. The package may point at `/admin/setup` and the setup
  token; it never skips the claim.
- App secrets are Aspire secret parameters with a generated default, given to
  Umbral and to the referencing project, never printed, never in the
  repository.
- Off unless asked: Umbral reads a declaration only when a variable gives one.
  The package sets it only when `AddApp` or `WithPasskeys` was used.

## Decisions taken in this spec (for the code reviewer)

The questions of the task, answered. Cleared by the code reviewer on
2026-11-14 (`.alta/reviews/2026-11-14-declared-apps/security-decisions.md`),
with conditions C1 and C2 and nits N1 to N3 written into D1, D5, D6, D8 and
D10 below.

**D1. Mechanism: one environment variable holding JSON, and one variable per
app secret.** `UMBRAL__DECLARATION` holds the declaration as a JSON document
with a `version` (1). `UMBRAL__APP_SECRETS__<key>` holds the secret of the
confidential app `<key>`. The declaration holds no secret, so it may appear in
a dashboard, a manifest or a compose file in plain text.
- A file named by a variable was the other choice. It was rejected: Aspire's
  container files are made in run mode but each publish target (compose,
  Kubernetes, Azure) carries them differently or not at all, and a file is a
  second thing to carry next to the data directory. A JSON value in one
  variable works the same in `docker run`, a compose file and every Aspire
  target. Redirect lists stay readable: the AppHost writes them in C#, and a
  hand-written compose file can hold the JSON in a block scalar.
- The secrets are ordinary environment variables: visible to `docker inspect`,
  written to a compose publish's `.env`, and masked in the Aspire dashboard as
  secret parameters. The docs say so.
- (N1) Some platforms and shells refuse `-` in a variable name, so the secret of
  the key `my-app` is also found as `UMBRAL__APP_SECRETS__my_app`. That is
  unambiguous, since `_` cannot appear in a key. The package emits the
  underscore form. Both forms set for one key is refused.

**D2. The declaration owns what it names.** At every start, a declared app is
created if missing and otherwise updated to match: name, type, both URI lists,
connector choice, secret. While declared it is marked **Declared** in the admin
area, and the admin area refuses to change what the declaration owns, delete
it, or rotate its secret: each of those would be undone or broken at the next
start. What the declaration does not cover stays editable (branding). The same
for the passkey connector: enabled, sign-up mode and sharing are owned;
invitations stay the admin area's.

**D3. What leaves the declaration is left as it is.** An app that was declared
and is no longer named is **un-declared**: marked no longer declared, and from
then on an ordinary app the admin area owns. It is never deleted, disabled or
changed. The same for the passkey connector when `passkeys` is absent. When
`UMBRAL__DECLARATION` is not set at all, every item still marked declared is
un-declared the same way, so that turning the feature off never leaves apps
locked in the admin area. Nothing else happens without the variable.

**D4. Identity: the key is the client ID.** Each declared app has a `key` that
follows the connector slug rule (`[a-z0-9-]{1,32}`). Its client ID is the key,
so it is stable across restarts, and across volumes. A table records which
client IDs and which connector were declared, and whether they still are, so a
later start tells "mine" from "the admin area's". The display name is a field,
free to change.

**D5. Conflicts are refused, never merged.** A declared key equal to the client
ID of an app that was never declared (created in the admin area) is refused.
So is `passkeys` when the passkey connector exists, was never declared, and
differs from the declaration: it may be in use with other settings
(invitations only), and adopting it would change who can enrol without anyone
choosing it. (C2) When it was never declared and is already exactly what the
declaration says (enabled, shared, the same sign-up mode), it is **adopted**:
adopting changes nothing and widens nothing, and every volume set up by the old
sample steps is in that state. Adopting is recorded as `connector_updated`
with `declared;adopted`. Apps need no such rule: an app made in the admin area
has a random client ID, so the old sample's app is left as an ordinary app
that may be deleted. The log line says which
item, why, and what to do (remove it from the declaration, or give the app
another key). An item that was declared once and later un-declared may be
declared again: it was the declaration's before.

**D6. A refused declaration stops Umbral.** The whole declaration is checked
before anything is written: JSON shape and version, each key, each URI by the
admin area's own rules (exact, absolute, no wildcard, no fragment, at most 10
per list, no duplicate), types, secret presence and length, conflicts. Any
problem writes nothing and Umbral exits with the problems in the log, as it
does for a bad public origin. Everything is then applied in one transaction
with its security events. A half-applied declaration, or an app served with
yesterday's redirect URIs because today's were refused, is worse than a
container that says why it did not start.
- (C1) Any failure while applying, not only a refusal, also stops the start;
  it is never logged and passed over. OpenIddict's application manager keeps an
  in-memory cache, and an app created or updated in a transaction that rolled
  back could stay in it with the wrong redirect URIs. Stopping the process
  drops the cache with the transaction. The manager is taken from the same
  scope as the context that holds the transaction.

**D7. Secrets.** A confidential (`web`) app needs `UMBRAL__APP_SECRETS__<key>`
of at least 32 characters; a public (`spa`) app must have none. Umbral never
logs, prints or stores the value; OpenIddict stores its PBKDF2 hash, as for an
app made in the admin area. At each start Umbral checks the value against the
stored hash (`ValidateClientSecretAsync`); when it no longer matches, the
secret is replaced: a rotation, logged `app_secret_rotated` as the admin
area's rotation is. Umbral never generates a declared app's secret: the
referencing app could not know it.

**D8. Security events.** Every change the declaration makes is a security event
with actor `system`, the client ID or connector slug, and a detail starting
`declared`, never a secret or a URI list:
- `app_created` (`declared`), `app_updated` (`declared;fields=name,type,redirect_uris,post_logout_redirect_uris,connectors`, the fields that changed), `app_secret_rotated` (`declared`), `app_updated` (`undeclared`);
- `connector_created` / `connector_updated` (`declared;enrolment=open|invitations`), `connector_enabled` (`declared`), `connector_updated` (`declared;adopted`, C2), `connector_updated` (`undeclared`).

A start that changes nothing records nothing. One Information line per start
says what was applied (counts only, and `passkey connector adopted` when it
was, N3). A refused declaration records no event;
the log line is the record, since nothing changed.

**D9. Run mode and publish mode.** Both are allowed: nothing in a declaration
is laptop-only once secrets come from parameters. In publish mode the package
requires explicit redirect URIs (a project's endpoint there is an internal
address no browser reaches). The docs say what a declaration means for a
production instance: the apps and the passkey connector are then changed only
by redeploying.

**D10. Claiming and the declaration are independent.** Umbral applies a
declaration whether or not it has been claimed. Claiming guards the admin area;
signing in to an app never needed it. Whoever sets the container's environment
already holds more than the setup token (they can run `reset-credential`), so
applying the declaration before the claim gives nobody anything new. On a
fresh volume the sample therefore signs in before the claim; the claim is only
needed to open the admin area. The docs add: with `"signUp": "open"` an
unclaimed instance on a public host lets people sign up to the declared apps
before anyone claims it, so claim the instance before you publish it.

**D11. Constitution.** A MINOR amendment (3.2.0, guidance materially expanded): Principle V's last paragraph
says the admin UI is where configuration changes *except what a declaration
owns*, with a pointer to this spec. The gate table is unchanged: no new
concept (Declared is a state of an App or a Connector), no state outside the
data directory (the declaration is input, like every `UMBRAL__` variable),
secure default (off unless given, and refused rather than merged).

## Clarifications

### Session 2026-11-14

- **Security decisions** (`umbral`, code reviewer, before the build, a962660):
  D1 to D11 cleared. C1: any failure while applying stops the start (the
  application manager's cache), with a test that induces one mid-apply. C2:
  adopt a never-declared passkey connector that already matches, with a test
  on a volume set up by the old sample steps. Nits: N1 (`_` form of a key in
  the secret variable), N2 (the package generates 48 characters, pinned by a
  test), N3 (the start line names an adoption). Docs: the secrets are plain
  environment variables; claim before you publish with open sign-up.
- **Screens** (`umbral-designer`): a muted pill in the pane header, "· Declared"
  on the list entry's second line, an info note naming `UMBRAL__DECLARATION`
  with a docs link, owned fields as values (no disabled inputs, no closing
  row), the passkey settings as label and value lines, two Warning refusals.
  Words in `contracts/screens.md`. PRODUCT.md needs a sentence on declared
  items, through the documentation review.

## User Scenarios & Testing

### User Story 1: a developer runs the Aspire sample on a fresh volume (P1)

They run `aspire run` in `samples/aspire`. Umbral starts with the sample app
and the passkey connector declared; the sample starts with its issuer, client
ID and secret. They open the sample, sign up with a passkey and are signed in.
They claim Umbral only to look at the admin area.

**Acceptance**

1. Fresh volume, `aspire run`: the sample signs in with a new passkey with no
   step in Umbral's admin area.
2. A second `aspire run` on the same volume records no security event and
   creates no second app.
3. A redirect URI changed in the AppHost is applied at the next start, with one
   `app_updated` event (`declared;fields=redirect_uris`).
4. No secret appears in Umbral's log, the AppHost's console, the manifest's
   plain values or the repository.

### User Story 2: an operator declares apps for a production instance (P2)

They set `UMBRAL__DECLARATION` and the secret variables in a compose file (or
through the package in publish mode). Their apps exist at the first start, and
change only when the compose file does.

**Acceptance**

1. The Apps screen marks each declared app **Declared**; its fields cannot be
   changed there, it cannot be deleted and its secret cannot be rotated there,
   and the screen says where it is changed.
2. Its branding can still be set on the Apps screen.
3. Removing an app from the declaration leaves the app working and makes it an
   ordinary app at the next start.

### User Story 3: a declaration that cannot be applied (P2)

**Acceptance**

1. A key equal to an app created in the admin area: Umbral does not start; the
   log names the key and says to choose another key or remove it.
2. A redirect URI with a wildcard, a missing secret for a `web` app, a secret
   for a `spa` app, a secret shorter than 32 characters, an unknown `version`,
   invalid JSON: Umbral does not start, the log names each problem, and the
   database is unchanged.
3. `passkeys` declared on a volume whose passkey connector was set up in the
   admin area with other settings: Umbral does not start and says to remove
   `passkeys` from the declaration. With the same settings it is adopted (C2).

### Edge cases

- A connector slug in an app's `connectors` that no connector has is accepted,
  with a warning line: the operator may add that connector later, and until
  then it offers nothing (`Connectors.offeredTo`).
- The type changes from `web` to `spa`: the secret is removed and the secret
  variable must be gone. From `spa` to `web`: the secret variable is required.
- A declared app deleted outside Umbral's screens (for example a restored
  backup) is created again at the next start.

## Requirements

- **FR-001** Umbral reads a declaration only from `UMBRAL__DECLARATION`. When
  it is unset or empty, it applies nothing and un-declares what is still marked
  declared (D3).
- **FR-002** The declaration is JSON in the shape of
  `contracts/declaration.md`, version 1. Unknown properties are refused (a
  typo must not silently do nothing).
- **FR-003** Before writing anything Umbral checks the whole declaration (D6);
  any problem stops the start with every problem in the log and nothing
  written.
- **FR-004** A declared app is created with client ID = key, or updated to
  match, in one transaction with its events (D2, D4, D8).
- **FR-005** Conflicts are refused (D5).
- **FR-006** Secrets as in D7.
- **FR-007** The passkey connector is created, enabled, shared and given its
  sign-up mode when declared; updated to match when it differs (D2, D5).
- **FR-008** Items no longer declared are un-declared, never deleted or
  changed otherwise (D3).
- **FR-009** The admin area marks declared items **Declared**, shows their
  owned fields read-only with where to change them, and refuses on the server
  every route that would change an owned field, delete the item or rotate its
  secret (`app-declared`, `connector-declared`). Branding and passkey
  invitations stay available.
- **FR-010** Security events and the start line as in D8.
- **FR-011** The Aspire package: `AddApp`, `WithPasskeys`, the app's builder
  methods and `WithReference(app)` as in `contracts/package.md`.
- **FR-012** `samples/aspire` declares the sample and the passkey connector; its
  steps shrink to "claim to see the admin area"; its walk is green on a fresh
  volume and a reused one.
- **FR-013** Documentation: `docs/run/aspire.md` (declaring apps),
  `docs/run/settings.md` (the two variables), `docs/integrate.md` (pointer),
  the package README, the samples README, PRODUCT.md (declared items are the
  exception to "one place"), and a changelog fragment with an Upgrade note
  (schema 16).

## Success Criteria

- **SC-001** Fresh volume to a passkey sign-in in the sample with no click in
  Umbral's admin area.
- **SC-002** A start with an unchanged declaration writes nothing.
- **SC-003** No secret in any log, console output, manifest plain value or file
  of the repository (checked by the walk's grep and by tests on the events).

## Out of scope

Secret-bearing connectors, users, passkeys of users, the operator credential,
branding, instance settings (allowed domains), exporting a declaration.
