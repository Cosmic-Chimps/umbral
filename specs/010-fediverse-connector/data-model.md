# Data Model: The Fediverse Connector

No new concept for the operator. A **Connector** of a new built-in kind, a
stored **Server registration** that Umbral creates by itself, two profile fields
on a **User**, and one more field in the sign-in correlation. Schema version 6
to 7 (research R7, R10).

## Connector (changed)

| Field | Stored as | Rule |
| --- | --- | --- |
| `Kind` | `connectors.kind = 'fediverse'` | New `ProviderKind.Fediverse`. At most one row of this kind; its slug is always `fediverse`. |
| `FediverseAccess` | `connectors.fediverse_scope TEXT NOT NULL DEFAULT 'any'` and `connectors.fediverse_servers TEXT NOT NULL DEFAULT ''` | See below. Read for the fediverse kind only; every other kind keeps the defaults and ignores them. |
| `ClientId`, `ClientSecretRef`, `ClientSecretHint` | `''` | Empty for the fediverse kind. A reference that does not decode means "no secret": `SecretMove` skips the row, `ConnectorDelete` deletes nothing from a store, the card shows no store pill. The per-server secrets live in `fediverse_servers`, not here. |
| `IssuerUrl` | `NULL` | As for every non-`oidc` kind. |
| `AllowedEmailDomains` | `''` | Always empty; no field on the card; `EmailDomainRules.ruleFor` answers `NoRule` for the kind (R11). |

Enabled, shared, created and updated as for every connector.

```text
FediverseAccess = { OnlyListed: bool; Servers: string list }   // Domains/Connectors.fs, a field of Connector
allows : FediverseAccess -> string -> bool                      // Domains/Fediverse.fs: not OnlyListed, or host in Servers
```

Storage of the scope:

- `fediverse_scope` is `any` (`OnlyListed = false`) or `listed` (`OnlyListed = true`).
- `fediverse_servers` holds the normalised hosts joined by one space, in the
  order saved, with no duplicates. It is saved whichever radio is chosen (the
  field is always visible, design contract section 1), and consulted only when
  the scope is `listed`.
- An empty list under `listed` is valid and refuses everyone (R3).

Validation (FR-003, R5), applied by one function `normaliseServer` to every
operator entry and to every typed value:

- Trim; drop a leading `@` or `https://` (`http://` under the development
  setting only); drop a trailing `/`; refuse a path, query or fragment; drop
  `:443`; refuse any other port; lower-case; drop one trailing `.`; convert an
  internationalised name to ASCII.
- Then require a public-looking domain: at least two labels, valid labels,
  non-numeric top label. IP literals and `localhost` are refused. Under the
  development setting, `localhost` and IP literals with a port are allowed.
- A wildcard (`*`) is refused.
- One refused entry saves nothing and the message names that entry.
- Saving replaces the whole list. The form is one text field, entries separated
  by commas or spaces, shown back normalised (design contract section 4).

## Server registration (new table `fediverse_servers`)

```sql
CREATE TABLE IF NOT EXISTS fediverse_servers (
    host           TEXT NOT NULL PRIMARY KEY,   -- normalised host, with a port only under the development setting
    client_id      TEXT NOT NULL,
    client_secret  TEXT NOT NULL,               -- Data Protection ciphertext, purpose Umbral.FediverseServers.v1
    authorize_url  TEXT NOT NULL,               -- as the server's metadata says, https
    token_url      TEXT NOT NULL,               -- as the server's metadata says, https
    redirect_uri   TEXT NOT NULL,               -- the callback address this registration was made with
    registered_utc TEXT NOT NULL,
    checked_utc    TEXT     NULL,               -- last time the registration was proved to still work
    last_used_utc  TEXT     NULL                -- last sign-in through this server
);
```

Rules:

- One row per normalised host. The host is the same spelling the allowlist, the
  correlation and the subject use (R5).
- `client_secret` is protected with the local Data Protection key ring in the
  data directory, always, whatever secret store the operator configured for
  connector secrets (R7). It is never logged, never shown, never sent anywhere
  but the token endpoint of the same host, and read through `Secrets.Protected`.
- `authorize_url` and `token_url` are the server's own endpoints and must be
  `https` (or `http` on a development host). They are public-address checked on
  every call like any other server address (R6).
- The registration asks for the `read:accounts` scope and for the one callback
  address of the instance, nothing else (FR-011).
- Two first sign-ins racing: the insert is `ON CONFLICT(host) DO NOTHING`, and
  both read the row back; the losing registration is left on the server unused
  (R7).
- Deleting the Fediverse connector does not delete these rows. They belong to
  Umbral, not to the connector, and a later set-up reuses them. A fresh data
  directory has none.
- There is no operator screen for the table. The operator never sees a client
  id or a secret.

### Registration lifecycle

```text
none --first sign-in from host--> registered
registered --not checked in 24 h, check passes--> checked
checked --next sign-in--> checked            (last_used_utc and checked_utc stamped)
registered | checked --check says invalid_client--> forgotten
forgotten --register again, once--> registered   (row replaced, security event)
forgotten --second refusal--> stays forgotten    (attempt ends "did not answer", no retry)
registered | checked --callback exchange says invalid_client--> deleted
deleted --next attempt--> none --> registered
```

- **none to registered.** The person's sign-in reached a server with no row.
  Umbral posts to `/api/v1/apps` with its callback address and `read:accounts`,
  reads the server's metadata for the endpoints, inserts the row, and records a
  credential-change security event naming the host (FR-011, FR-023).
- **registered to checked.** Before redirecting, if `checked_utc` is null or
  older than 24 hours, Umbral asks the token endpoint for a client-credentials
  token with scope `read:accounts` (R8). Any well-formed answer other than
  `invalid_client` (a token, `unsupported_grant_type`, `invalid_scope`) stamps
  `checked_utc`. A timeout or network failure is "did not answer" and changes
  nothing.
- **Forgotten.** `invalid_client` at the check means the server dropped the app.
  Umbral registers again once and replaces the row in the same transaction as
  the new values. A second refusal ends the attempt with the "did not answer"
  message and no further retries (FR-013).
- **Deleted at exchange.** At the callback, `invalid_client` from the code
  exchange deletes the row so the next attempt registers again. The person sees
  the generic sign-in failure, and the failure is recorded with the host and no
  secret.
- **Moved origin.** When the stored `redirect_uri` is not the current callback
  address (the operator changed `UMBRAL__PUBLIC_ORIGIN`), the registration is
  treated as forgotten before the redirect: Umbral registers again with the new
  address and replaces the row. A server would otherwise refuse the redirect
  with its own error page and never send the person back.
- `last_used_utc` is stamped when a code exchange succeeds.
- Every transition reads and writes the row by host in one connection. No other
  process writes the table.

## User (changed)

| Field | Stored as | Rule |
| --- | --- | --- |
| `PreferredUsername` | `users.preferred_username TEXT NULL` | `<username>@<host>` for a fediverse user, refreshed on every sign-in. `NULL` for every other user. |
| `ProfileUrl` | `users.profile_url TEXT NULL` | The account's `url` when it is `https`, otherwise `NULL`. `NULL` for every other user. |

Existing rows keep `NULL`. `Users.User` gains `PreferredUsername: string option`
and `ProfileUrl: string option`, and `Users.SelectColumns` and `readUser` read
them.

Refresh (FR-020, R10), on every sign-in through `UserStore.resolve`:

- `display_name` is the account's `display_name`, or its `username` when that is
  empty. The adapter fills `ProviderProfile.DisplayName`, so the existing
  `COALESCE(@name, display_name)` keeps its meaning.
- `avatar_url` is the account's `avatar`.
- `preferred_username` and `profile_url` are written with
  `COALESCE(@username, preferred_username)` and the same for the profile. An
  absent value from the server leaves the stored one in place, and a user of
  another kind passes `NULL` and keeps `NULL`.
- `email` stays `NULL` and `email_verified` stays `0`. The linking rule is not
  consulted because `ProviderProfile.Email` is `None` (FR-018).

Claims (FR-021): under the `profile` scope an app receives `name`,
`preferred_username`, `picture` and `profile`, and no `email`. `Claims.Profile`
is registered next to `PreferredUsername` and `Picture`, goes to the access
token and, with the `profile` scope, to the id token and userinfo, and is
removed before it is set, as `picture` is, so a profile address that no longer
exists leaves the next tokens.

## Linked account (existing table, one new kind)

One `linked_accounts` row per person per server:

| Column | Value |
| --- | --- |
| `provider_kind` | `fediverse` |
| `provider_subject` | `<host>#<account id>` (`fediverseSubject host id`) |
| `asserted_email` | `NULL` |
| `asserted_email_verified` | `0` |

Rules:

- The account id is the `id` field of `verify_credentials`, a string, unique on
  that server only. The host is the server the sign-in started with, from the
  correlation, never a value the server sends back (FR-017).
- A host cannot contain `#`, so the first `#` splits the subject, as for `oidc`.
  `parseFediverseSubject` is the inverse and is used by the Users screen to
  show the server.
- Never display name, username or handle: all three change, and a handle can be
  reused by another person.
- The primary key `(provider_kind, provider_subject)` makes two simultaneous
  first sign-ins resolve to one user, as today.
- No email, so `findVerifiedEmailMatches` is never run and an identity never
  joins another user (FR-018). A person on two servers is two users.

## F# shapes

```text
ProviderKind.Fediverse                                     // Value "fediverse", DisplayName "Fediverse", parse "fediverse"

Connector =  { ...existing...
               FediverseAccess: FediverseAccess }           // { OnlyListed = false; Servers = [] } for every other kind

ProviderProfile = { ...existing...
                    Username: string option                 // <username>@<host>; None for every other kind
                    ProfileUrl: string option }             // https only; None for every other kind

Correlation = { ...existing...
                Server: string }                            // normalised host; "" for every other kind
```

`Correlation.Server` (R9):

- The cookie payload becomes seven fields joined by the existing unit
  separator. `encode` writes seven. `decode` accepts seven, and still accepts six
  with `Server = ""`, for a cookie minted just before an upgrade.
- A host is lower-case ASCII (letters, digits, `-`, `.`, and `:` and digits
  under the development setting), so it cannot contain the separator.
- `start` takes the server as a new argument. Every other caller passes `""`.
- The callback reads `correlation.Server`, re-checks the scope (FR-019), loads
  the registration for that host, and exchanges the code there and nowhere
  else. A correlation for the fediverse kind with an empty server is refused as
  corrupt.
- The `consumed` replay table stays keyed on `State` only.

## Schema version 7

An additive step, `UpgradeToVersion7`, in the style of versions 3 to 6. No data
is rewritten, and a database that already has any of it is left alone.

```sql
ALTER TABLE connectors ADD COLUMN fediverse_scope   TEXT NOT NULL DEFAULT 'any';
ALTER TABLE connectors ADD COLUMN fediverse_servers TEXT NOT NULL DEFAULT '';
ALTER TABLE users      ADD COLUMN preferred_username TEXT NULL;
ALTER TABLE users      ADD COLUMN profile_url        TEXT NULL;
CREATE TABLE IF NOT EXISTS fediverse_servers ( ... );   -- as above
```

- `schema` (the `CREATE TABLE IF NOT EXISTS` text for a fresh database) carries
  the same four columns and the table, so a new database and an upgraded one
  match. Repeating the statements in the step keeps it complete on its own.
- Every connector that exists today gets `any` and `''` through the column
  defaults, which only the fediverse kind reads, so nothing about who can sign
  in changes on upgrade.
- `schema_version` is set to 7 after the step, as for the earlier ones.
- A downgrade is refused at startup, as for every version: an older build
  sees `schema_version` 7 and stops with its "written by a newer Umbral"
  message.

## What is not stored

- No access token. The token from the exchange is used once for
  `verify_credentials` and dropped (R10).
- No authorization code, `state` or PKCE verifier outside the correlation
  cookie.
- No fediverse email, because there is none.
- No per-server operator settings. A server is not a concept the operator
  manages (spec, Decisions Already Taken).
