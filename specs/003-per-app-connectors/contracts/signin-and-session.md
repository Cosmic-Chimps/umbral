# Contract: the sign-in page, the session and the `idp` claim

**Feature**: `003-per-app-connectors`

What an end user, an integrating app and the test suite see change when an app
has a connector choice. Everything not listed here keeps the contract in
[001 oidc-provider.md](../../001-umbral-mvp/contracts/oidc-provider.md) and
[001 connectors.md](../../001-umbral-mvp/contracts/connectors.md).

## The rule every caller shares

One pure function in `Domains/Connectors.fs` decides which connectors an app
offers. The sign-in page, the authorize endpoint (FR-008), the admin warnings
(FR-010) and the Apps screen all call it. None of them works the rule out for
itself.

```fsharp
/// An app's connector choice (FR-005). No row in `app_connector_choices`
/// means AllShared, which is also the default for a new app.
type AppChoice =
    | AllShared
    | Only of Set<string>          // connector slugs

/// The enabled connectors an app offers. AllShared: every enabled connector
/// whose IsShared is true (FR-006, FR-014). Only: every enabled connector
/// whose slug is in the set (FR-007), shared or not.
val offeredTo: choice: AppChoice -> connectors: Connector list -> Connector list

/// The apps left with nothing offered, given the connectors as they would be
/// after a change. Input is each app's client id, name and choice. Output keeps
/// the input order. Used by every FR-010 warning.
val strandedApps:
    apps: (string * string * AppChoice) list -> connectors: Connector list -> (string * string) list
```

`Connector` gains `IsShared: bool`. An `Only` set that names a slug no
connector has offers nothing for that slug; it is never widened (FR-011).

## Routes

No route is added or removed on the end-user side.

| Method | Path | Change |
| --- | --- | --- |
| GET | `/signin` | Names the app and filters its connectors, read from `returnUrl` |
| GET | `/connect/authorize` | Honours a session only through a connector the app offers (FR-008) |
| GET | `/connect/{slug}/start` | Unchanged. Not filtered by app: the check happens at authorize |
| GET | `/connect/{slug}/callback` | On failure, keeps `returnUrl` when it sends someone back to `/signin` |

### `GET /signin?returnUrl=...&msg=...`

The page learns the app from `returnUrl`, never from a separate query value.

1. `returnUrl` is sanitized by `Picker.sanitizeReturnUrl` exactly as today.
2. If its path is `/connect/authorize`, its `client_id` query value is read and
   looked up through OpenIddict's application manager.
3. A found app gives the app's name and its `AppChoice`. The page shows the
   title `Sign in to {app name}` and offers `offeredTo choice enabledConnectors`
   (FR-001, FR-006, FR-007).
4. Any other case (no `returnUrl`, another path, no `client_id`, an unknown
   `client_id`) is "no app". The page shows the general title `Sign in`, names
   no app, and offers every enabled connector that is shared (FR-002).

A `client_id` query value on `/signin` itself is ignored. It could disagree with
`returnUrl` and let a crafted link say "Sign in to A" while signing in to B.

Each button links to `/connect/{slug}/start?returnUrl={returnUrl}` and reads
`Sign in with {display name}`, as today. Two connectors of the same kind give
two buttons, told apart by their display names.

#### Markers

| Hook | On | Meaning |
| --- | --- | --- |
| `data-screen="signin"` | The page root | Unchanged |
| `data-signin-app="{clientId}"` | The element naming the app | Present only when the page serves a registered app; absent for "no app" (FR-002) |
| `data-connector="{slug}"` | Each sign-in button | Unchanged. The set of these is exactly what `offeredTo` returned |
| `data-no-connectors` | The empty-state message | Shown when the app offers nothing, including a limited app whose set is empty or all disabled |

A test asserts the offered set by collecting `data-connector` values, and the
named app by reading `data-signin-app`. It never asserts on the title text.

#### Texts

In `Views/Texts.fs`, beside `PickerTitle`:

| Field | Value |
| --- | --- |
| `PickerTitle` | `Sign in` (unchanged, FR-002) |
| `PickerTitleForApp` | `fun app -> $"Sign in to {app}"` |
| `PickerNoConnectorsForApp` | Says this app has no sign-in method available and that whoever runs it can fix that. Used instead of `PickerNoConnectors` when an app is named |

### `GET /connect/{slug}/callback` on failure

`Callback.fail` redirects to `/signin?returnUrl={returnUrl}&msg={code}` when the
correlation record carries a `returnUrl`, and to `/signin?msg={code}` only when
it does not (state missing or expired). The failure codes are unchanged:
`sign-in-failed`, `access-denied`, `provider-unreachable`. The page after a
failure therefore still names the app and still filters its connectors.

## The session

The session cookie (scheme unchanged) carries two claims besides the subject:

| Claim | Constant | Value |
| --- | --- | --- |
| `idp` | `Sessions.IdpClaim` | The provider **kind** (`discord`, `github`, `google`, `fake`). Was the slug before this feature |
| `connector` | `Sessions.ConnectorClaim` | The **slug** of the connector that established the session. New |

`Sessions.signIn ctx userId kind slug` writes both. `Sessions.currentConnector
principal` returns the `connector` claim, or, for a session from before the
upgrade that has none, the `idp` claim. Every slug that could exist before the
upgrade equalled its kind, so that value is the slug.

## `GET /connect/authorize` and FR-008

When a session exists and the request does not carry `prompt=login`:

| Session's connector is offered by the requesting app | Request has `prompt=none` | Result |
| --- | --- | --- |
| yes | either | Unchanged: the session is honoured and a code is issued |
| no | no | 302 to `/signin?returnUrl={the authorize request}`. Nothing is signed |
| no | yes | `login_required`, as for no session at all |

"Offered" means the session's connector slug is in `offeredTo` of the requesting
app's choice. A disabled or deleted connector is never offered, so a session
established through it is not honoured either. The session is not signed out:
the person may still use it for another app that offers that connector.

This is the only place the choice is enforced. Opening
`/connect/{slug}/start` by hand for a connector the app does not offer still
completes, and the replayed authorize request then sends the person back to the
app's sign-in page.

## What apps receive

| Claim | Before | After |
| --- | --- | --- |
| `sub` | The Umbral user id | Unchanged. The same person through two connectors of one kind gets one `sub` (FR-015) |
| `idp` | The slug, which equalled the kind | The kind, as the README promises. A connector named `discord-frunchy` still yields `idp: discord` |

No other claim and no token destination changes. The `connector` claim never
leaves the session cookie.

## Linking accounts (FR-015)

A linked account stays keyed on `(kind, provider subject)`, never on the slug.
The callback resolves the user with the connector's kind, so a Discord account
used through `discord` and through `discord-frunchy` finds the same linked
account and the same user. The verified-email linking rule is unchanged.
