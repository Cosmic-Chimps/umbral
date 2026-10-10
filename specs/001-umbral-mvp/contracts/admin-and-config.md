# Contract: admin UI and configuration file

**Feature**: `001-umbral-mvp`

Two operator-facing surfaces. Both are contracts because both are things a human
learns once and expects to keep working.

## Routes

Three screens, per FR-015. Everything else is an action on one of them.

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/admin` | Redirects to `/admin/apps`, or to first-run setup |
| GET, POST | `/admin/setup` | First run: set the operator credential. Refuses once set. |
| GET, POST | `/admin/signin` | Operator sign-in |
| POST | `/admin/signout` | Form POST, never a GET link |
| GET | `/admin/apps` | **Screen 1** |
| POST | `/admin/apps` | Register an app → the shown-once secret page |
| POST | `/admin/apps/{clientId}/secret` | Regenerate |
| POST | `/admin/apps/{clientId}/delete` | Delete, with confirmation |
| GET | `/admin/connectors` | **Screen 2** |
| POST | `/admin/connectors/{slug}` | Create or update credentials |
| POST | `/admin/connectors/{slug}/toggle` | Enable / disable *(htmx)* |
| POST | `/admin/connectors/{slug}/delete` | Delete, with confirmation |
| GET | `/admin/users` | **Screen 3**, with search |
| POST | `/admin/users/{id}/toggle` | Enable / disable *(htmx)* |
| POST | `/admin/users/{id}/delete` | Delete, with confirmation |
| GET | `/admin/config` | Export the configuration file |
| GET | `/signin` | **End-user** connector picker (not an admin screen) |
| GET | `/healthz` | Liveness, unauthenticated, no detail (FR-032) |

`/admin/*` requires the operator session. `/signin` and `/connect/*` do not.

### htmx

Marked routes return a bare fragment when `HX-Request` is present and the same
element inside `Layout.page` otherwise. Every one is a real form that works with
htmx disabled — that is what keeps a single test helper able to drive both paths
(D11).

## Rules the screens must enforce

These are contract, not styling:

- **No saved secret is ever displayed.** Connector secrets show masked with the
  last four characters and a **Replace** action — never **Edit**, because there is
  nothing to edit from. App secrets appear exactly once, on the page immediately
  after creation, with a copy button and unambiguous "this will not be shown
  again" copy (FR-019, FR-033).
- **The computed callback URL is shown read-only, with a copy button**, beside
  each connector's credential fields (FR-013). The operator never types it. This
  single element is the largest contributor to the five-minute claim.
- **An origin-mismatch banner** appears on every admin page when the request's
  apparent origin differs from `UMBRAL__PUBLIC_ORIGIN`, naming both. Advisory
  only — it never changes a redirect URI (D4).
- **Destructive actions confirm**, and the confirmation names what breaks:
  deleting an app invalidates its tokens; deleting a user is permanent; disabling
  the last enabled connector means nobody can sign in anywhere (FR-023).
- **Every state-changing request carries an antiforgery token** (FR-024), checked
  via `IAntiforgeryValidationFeature` inside the handler before the form is read.
- **App permissions are derived from an app-type choice, never exposed as
  checkboxes.** A hand-composed permission set produces an app whose login works
  and whose token exchange fails.
- **Every error message names the next action** (FR-037, SC-010). All strings live
  in `Views/Texts.fs`, which makes SC-010 auditable by reading one file.
- **The Users screen stays minimal, and that is a requirement rather than a
  stylistic preference.** It shows a display name, the linked providers, first
  and last sign-in, and enabled state. Nothing else.

  A generated mock-up of this screen produced per-user IP addresses, network
  segment labels, `@alias` handles, an "enrolled" count and a live token
  introspection stream. Every one of those is either data Umbral does not hold
  (it records a remote IP against a *security event*, not a user, and has no
  network awareness at all) or a concept it rejects — nobody *enrols* with
  Umbral; a user exists because they signed in once.

  Umbral stores the least it can get away with while still being able to answer
  "who is this" and "cut this person off". The screen must look like that, or it
  invites the collection that makes it untrue.

## Test hooks

`data-*` attributes are the contract between the UI and the test suite. Tests
never assert on CSS classes, so a restyle cannot break them — and a hook may not
be removed without changing the tests deliberately.

| Hook | On |
| --- | --- |
| `data-screen="apps\|connectors\|users"` | The screen root |
| `data-app="{clientId}"` | An app row |
| `data-connector="{slug}"` | A connector card |
| `data-connector-state="enabled\|disabled"` | That card |
| `data-callback-url` | The read-only callback field |
| `data-user="{id}"` | A user row |
| `data-user-state="enabled\|disabled"` | That row |
| `data-linked-provider="{kind}"` | A provider badge in a user row |
| `data-secret-once` | The shown-once secret element |
| `data-notice="{code}"` | A flash message |
| `data-origin-mismatch` | The banner |

## The configuration file

One declarative file, equivalent to the admin UI (FR-027 – FR-029, Principle V).
Supplied as `UMBRAL__CONFIG_FILE` or found at `/data/umbral.yaml`.

```yaml
version: 1

connectors:
  - slug: discord                      # immutable; it is in the callback URL
    kind: discord                      # discord | github | google
    display_name: Discord
    client_id: "1234567890"
    client_secret: ${UMBRAL_DISCORD_SECRET}
    enabled: true
    extra_scopes: []                   # optional

  - slug: github
    kind: github
    display_name: GitHub
    client_id: Iv1.abc123
    client_secret: ${UMBRAL_GITHUB_SECRET}
    enabled: true

apps:
  - client_id: grafana                 # identity; applying twice is a no-op
    name: Grafana
    client_secret: ${UMBRAL_GRAFANA_SECRET}
    redirect_uris:
      - https://grafana.example.com/login/generic_oauth
    post_logout_redirect_uris:
      - https://grafana.example.com/
```

### Validation

Whole-document, before anything is written, so a bad file never half-applies
(FR-030). Each error names the offending entry and what to do:

| Rule | Error |
| --- | --- |
| `version` is `1` | `unsupported version: 2 (this build understands 1)` |
| `slug` matches `^[a-z0-9-]{1,32}$` and is unique | `connectors[2].slug "My Discord" is not a valid slug` |
| `kind` is a known kind | `connectors[0].kind "slack" is not supported (discord, github, google)` |
| `client_secret` is `${VAR}` | `connectors[0].client_secret is a literal. Use ${VAR}.` |
| The referenced variable is set | `connectors[0].client_secret references UMBRAL_DISCORD_SECRET, which is not set` |
| `redirect_uris` is non-empty, absolute, no wildcards | `apps[1].redirect_uris[0] "https://*.example.com/cb" contains a wildcard` |
| `client_id` is unique | `apps[3].client_id "grafana" is used twice` |

A literal secret is an **error, not a warning**. That rule is the thing keeping a
secret out of the operator's git repository, and a warning would be ignored.

### Idempotence and the one asymmetry

Applying the same file twice changes nothing and creates no duplicates: `slug`
and `client_id` are the identity (FR-027.3).

Export (`GET /admin/config`) emits exactly this format with
`${UMBRAL_<SLUG>_SECRET}` placeholders, so export → import round-trips given the
environment (FR-029).

**App secrets cannot round-trip.** OpenIddict hashes them on write and
`GetClientSecretAsync` returns the hash, so there is nothing to export. The file
therefore carries a `client_secret: ${VAR}` reference per web app, and the export
emits `${UMBRAL_<CLIENT_ID>_SECRET}` placeholders with a comment saying so. On
apply, Umbral sets the app's secret to the variable's value (OpenIddict hashes
it); re-applying verifies the stored hash against the value and rewrites it only
when it differs, so a changed variable takes effect and an unchanged one changes
nothing. An entry with no `client_secret` is a public client (single-page or
native) and is never given one. This asymmetry is forced by the hashing, not
chosen, and it must appear in the documentation rather than be discovered.
