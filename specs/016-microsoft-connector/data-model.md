# Data model: The Microsoft accounts connector

## The kind

`ProviderKind.Microsoft`, stored as `microsoft`, display name "Microsoft". Built
in, at most one connector, slug `microsoft`. Not in `allKinds` (the add form).

Static endpoints: empty URLs (they come from the audience's discovery document),
scopes `openid email profile`, PKCE on, nonce on, no form post.

## `MicrosoftAudience` (Domains/Microsoft.fs, pure)

```fsharp
type MicrosoftAudience =
    | PersonalAccounts                 // consumers
    | OneOrganization of tenant: Guid  // the tenant's GUID
    | AnyOrganization                  // organizations
    | PersonalAndAnyOrganization       // common
```

| Member | Value |
| --- | --- |
| `Stored` | `consumers` / the GUID, lower case / `organizations` / `common` |
| `Segment` | the same string: the path segment of every Microsoft URL |
| `DiscoveryIssuer` | R3's table: the personal tenant's issuer, the tenant's issuer, or the template |
| `Admits (tid: Guid)` | R3's last column |

Functions:

| Function | Rule |
| --- | --- |
| `personalTenant` | `9188040d-6c67-4c5b-b112-36a304b66dad` |
| `authority` | `https://login.microsoftonline.com` |
| `issuerTemplate` | `https://login.microsoftonline.com/{tenantid}/v2.0` |
| `issuerFor (tid: string)` | the template with `{tenantid}` replaced |
| `documentBase audience` | `<authority>/<segment>/v2.0` |
| `parseStoredAudience (s: string)` | the inverse of `Stored`; None for anything else |
| `parseTenantId (s: string)` | trimmed; a GUID in any of .NET's `D` spellings, case-insensitive; returned as `Guid` |
| `parseClientId (s: string)` | the same GUID rule |
| `canonicalTid (s: string)` | `Some guid` only when `s` is exactly `guid.ToString("D")` (lower case, 36 characters) |
| `microsoftSubject tid oid` | `"<tid>#<oid>"`, both canonical lower case |
| `parseMicrosoftSubject` | the inverse; None unless both halves are canonical GUIDs |

## Storage

Schema version 13: `connectors.microsoft_audience TEXT NULL`. Holds `Stored`;
NULL for every other kind. Upgrade step: `ALTER TABLE connectors ADD COLUMN
microsoft_audience TEXT NULL;`.

`Connector` gains `MicrosoftAudience: MicrosoftAudience option`, None for every
other kind. A `microsoft` row whose column is NULL or unreadable loads with
None, and such a connector cannot be enabled or used (its Resolve fails with
`MalformedResponse "the connector has no audience"`).

The client ID is the row's `client_id`; the client secret is in the secret store
like every other client secret (`client_secret_ref`, `client_secret_hint`).

`linked_accounts`: provider `microsoft`, subject `microsoftSubject tid oid`. No new
column: the re-check reads the tenant from the subject.

## `ConnectorError.AccountRefused`

A new case, `AccountRefused of detail: string`: the provider proved who the person
is and the operator's rule does not admit them. `Detail` is `refused: <detail>`;
`Picker.failureCode` maps it to `account-not-allowed`. Only the Microsoft adapter
returns it in this change.
