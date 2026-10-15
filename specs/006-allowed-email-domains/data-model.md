# Data Model: Allowed Email Domains

No entity is added. App, Connector and User keep their meaning.

## Connector (changed)

| Field | Stored as | Rule |
| --- | --- | --- |
| `AllowedEmailDomains` | `connectors.allowed_email_domains TEXT NOT NULL DEFAULT ''` (schema v4) | Normalised ASCII domains joined by one space (research R3). Empty: use the instance list. |

Deleting a connector deletes its list with it.

## Instance setting (new row)

| Key | Value | Rule |
| --- | --- | --- |
| `allowed_email_domains` in `settings` | Normalised ASCII domains joined by one space | No row or empty value: no instance list. |

## The rule (pure, `Domains/EmailDomains.fs`)

```text
DomainList      = normalised ASCII domains, non-empty, ordered, no duplicates
ParseError      = Wildcard of entry | NotADomain of entry
Level           = Connector | Instance
Rule            = NoRule | Rule of Level * DomainList
Refusal         = NoEmail | EmailNotVerified | DomainNotAllowed of domain option
Decision        = Admitted | Refused of Level * Refusal

parse    : string -> Result<string list, ParseError>      // empty input -> Ok []
applying : connectorList -> instanceList -> Rule
domainOf : email option -> string option                   // after last @, ASCII, lower
check    : Rule -> email option -> verified: bool -> Decision
```

`check NoRule _ _` is always `Admitted`: an instance with no list behaves as
before (SC-006).

## Session cookie (changed, not user-facing)

| Claim | When | Value |
| --- | --- | --- |
| `email_domain` | the sign-in had a verified email | its domain, as `domainOf` gives it |

Absent on sessions from before this feature; treated as "no email" (research R6).

## Authorization principal (changed, not user-facing)

| Claim | Destination | Value |
| --- | --- | --- |
| `umbral_connector` | none | slug of the connector that established the session |
| `umbral_email_domain` | none | the session's `email_domain`, when present |

Carried by authorization codes and refresh tokens, never by access or identity
tokens (research R7).

## Security events

| Type | Actor | Fields | Detail |
| --- | --- | --- | --- |
| `sign_in_failed` (existing) | user | connector slug, user id (stages `authorize`, `token`), client id (`token`), remote IP | `email_domain_refused;reason=…;rule=…;stage=…;domain=…` |
| `allowed_domains_changed` (new) | operator | connector slug for a connector list, none for the instance list | `count=<n>` |

No event carries a full email address.
