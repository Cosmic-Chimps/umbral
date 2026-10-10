# Research: Federated Login for Every App, with Optional Per-App Connectors

The spec's three assumed decisions (FR-008, FR-011, FR-014) were accepted by the
project owner on 2026-10-10.

## R1. Where an app's connector choice lives

**Decision**: two Umbral tables. `app_connector_choices` has one row per app
that is limited, keyed on `client_id`. `app_connectors` holds the chosen
`(client_id, connector_slug)` pairs, with a foreign key to `connectors` that
cascades on delete. An app with no row in `app_connector_choices` offers all
shared connectors.

**Rationale**: the cascade gives FR-011 for free: deleting a connector removes
it from every chosen set, and the marker row keeps the app limited even when its
set empties. The rows are queryable for FR-010 without loading every app.

**Alternatives considered**: OpenIddict's `Properties` JSON on the application.
No foreign key, so deleting a connector means rewriting every app's JSON, and
`applyApps` would have to start comparing a JSON bag. A single text column
listing slugs has the same rewrite problem.

`AppDelete` deletes the app's rows itself. OpenIddict's table belongs to its EF
migrations, so no foreign key points into it.

## R2. Upgrading an existing database

**Decision**: bump `SchemaVersion` to 2. The two new tables come from
`CREATE TABLE IF NOT EXISTS` as today. The new column comes from an
`ALTER TABLE connectors ADD COLUMN is_shared INTEGER NOT NULL DEFAULT 1` that
runs inside the same transaction when the stored version is 1, and the stored
version is then set to 2.

**Rationale**: the default of 1 makes every existing connector shared and the
absence of choice rows makes every existing app "all shared connectors", which
is FR-019 with no data rewrite. SQLite has no `ADD COLUMN IF NOT EXISTS`, so
the stored version decides.

**Alternatives considered**: introducing a migration framework for Umbral's
tables. Too much machinery for one column.

## R3. What the `idp` claim says

**Decision**: apps keep receiving the provider kind (`discord`) in `idp`, as
the README promises. The session cookie gains a separate `connector` claim
holding the slug, which FR-008 checks. A session from before the upgrade has
no `connector` claim: its `idp` value is the slug, and the slug equalled the
kind for every connector that could exist then, so that value serves as both.

**Rationale**: today the code puts the slug in `idp` and the README says kind.
They only agree because each kind has had one slug. With `discord-frunchy`, an
app would see a new value for the same person. Keeping `idp` as the kind means
no integrating app changes.

**Alternatives considered**: send the slug, as the code does today. Rejected
because it breaks the README promise the first time a second connector of a
kind exists.

## R4. How the sign-in page knows the app

**Decision**: parse `client_id` from the already-sanitized `returnUrl` when its
path is `/connect/authorize`, then look the app up through OpenIddict's
application manager. An unknown or absent `client_id` means "no app"
(FR-002). `Callback.fail` keeps the `returnUrl` when it sends someone back to
the page after a failed sign-in, so the page still names the app and filters
its connectors.

**Rationale**: the authorize request in `returnUrl` is what the person will
actually be sent back to, so the page cannot name one app while returning to
another.

**Alternatives considered**: a separate `client_id` query value on `/signin`.
Rejected because it can disagree with `returnUrl`, which would let a crafted
link say "Sign in to A" while signing in to B.

## R5. One rule for "which connectors does this app offer"

**Decision**: a pure function in `Domains/Connectors.fs`:
`offeredTo (choice: AppChoice) (connectors: Connector list)`, where
`AppChoice = AllShared | Only of Set<string>`. It returns the enabled
connectors that are shared (for `AllShared`) or in the set (for `Only`). A
second function, `strandedApps`, takes the apps with their choices and the
connectors as they would be after a change, and returns the apps left with
nothing. The picker, the FR-008 check, the FR-010 warnings and the Apps
screen's empty-state gate all call these.

**Rationale**: four callers that each work out the rule would drift. The
current guard, `enabledCountExcluding`, counts connectors across the whole
instance and misses a limited app that is left stranded.

## R6. Creating more than one connector of a kind

**Decision**: the Connectors screen shows one card per stored connector. A kind
with no connector keeps today's empty card, posting to
`/admin/connectors/{kind}`, so the first-run path is unchanged. A new
`POST /admin/connectors` form adds a connector with a kind, a slug and a
display name. Card forms gain a display name field and a shared switch.
`save` stops inferring the kind from the slug and reads it from the stored
row. A slug must pass `Ids.isValidSlug` (lower-case letters, digits and
hyphens, up to 32 characters), the rule the configuration document already
applies, and must not be taken.

**Rationale**: the slug is in the callback URL and the secret path, so it is
validated once and fixed (FR-013). Keeping empty cards per kind means the
five-minute path still involves no "add" step.

## R7. FR-018 checks only against the document

**Decision**: as specified, a chosen set naming a connector the same document
does not define is refused.

**Rationale**: validation is pure and sees only the document. Checking against
the database would make whether a file is valid depend on the instance it is
applied to. The document is also due to be removed, so it gets no new
machinery.

## R8. Google's consent name belongs to the Cloud project

**Decision**: the docs say that a Google connector with its own consent-screen
name needs its own Google Cloud project, because Google's branding screen
applies to the whole project.

**Rationale**: a second OAuth client in the same project would show the same
name. SC-002's ten minutes holds for Discord and GitHub. For Google, creating
the project is part of those ten minutes.
