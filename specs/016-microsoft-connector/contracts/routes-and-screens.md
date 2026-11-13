# Contract: routes and screens of the Microsoft connector

The screens were settled with `umbral-designer` on 2026-11-12 (answers 1 to 6 to
the implementer's questions). The designer did not read Microsoft's pages: the
registration labels below are from Microsoft's "Register an application" page
(<https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-register-app>,
read 2026-11-12), word for word. Wording is open to the design and documentation
reviews.

## Routes

| Route | What it does |
| --- | --- |
| `GET /connect/microsoft/start?returnUrl=…` | The existing start route. Redirects to the authorization endpoint of the chosen audience's discovery document. |
| `GET /connect/microsoft/callback` | The existing callback route. |
| `GET /admin/connectors/microsoft` | The Microsoft pane (the existing detail route). |
| `POST /admin/connectors/microsoft` | New, routed before the generic `{slug}` route. Saves the Microsoft connector. Antiforgery token required. 422 with the form redrawn when a field is refused. |
| `POST /admin/connectors/microsoft/toggle` | The existing toggle route. Turning on runs the Enable check (discovery for the stored audience). |
| `POST /admin/connectors/microsoft/delete` | The existing delete route, unchanged. |

Failure codes shown to the person are the existing ones. A tenant the operator's
choice does not admit gives `account-not-allowed` (designer's answer 6: no new
message).

Security log details (all through `ConnectorError.Detail`):
- `malformed_response: id_token_invalid: <reason>`, with the OIDC connector's
  reasons plus `tenant` (no `tid`, or not a canonical GUID), `key_issuer` (the
  signing key's `issuer` does not give the token's `iss`) and `subject` (no `oid`
  GUID);
- `refused: tenant_not_allowed;tenant=<tid>`, the new `AccountRefused` case;
- at the authorize re-check: `microsoft:tenant_not_allowed;tenant=<tid>`, beside
  the Fediverse and Bluesky words.

## The Connectors screen

**List** (answer 1). An entry "Microsoft", after Apple and before OpenID Connect
providers, "Not set up" until saved, like the other built-in kinds. Not in the
add form's kind list.

**Pane, Setup section** (answer 2). As Apple's: a note, the copy field with the
redirect URI (`<public origin>/connect/microsoft/callback`), the rule line, and
the caveat:

> A Microsoft client secret expires: after 6 months by default, 24 months at
> most. Note the date. Once it has passed, nobody can sign in through Microsoft
> until you replace the secret here.

The naming advice is the generic one: Microsoft's page says of the
registration's Name, "App users can see this name". So: "Give the app
registration a name that names this Umbral instance (for example "Umbral —
home"): Microsoft shows it to people when they first sign in."

**The Set-up rule line** (design review nit 2, 2026-11-13). On this pane, once
a choice is saved, the line leads with it, then the email-domain rule:
"Who can sign in: <choice>, with any email domain." with no domain rule;
"Who can sign in: <choice>, with a verified email at <list> (this connector's
list)" and "… (the instance list)" otherwise. <choice> is the card's title in
lower case after its first word's capital: "personal Microsoft accounts", "work
or school accounts from one organization", "any work or school account",
"personal and any work or school account". Not set up: the other panes' line.

**Pane, Settings form** (answer 3), in this order:

| Field | Name | Notes |
| --- | --- | --- |
| Display name | `displayName` | Default "Microsoft". Hint (answer 5a): "Shown on the sign-in button as "Sign in with …" and on the admin screens." |
| Application (client) ID | `clientId` | Mono. Hint: "On the app's Overview page in the Microsoft Entra admin center." |
| Client secret | `clientSecret` | Hint: "The secret's Value, not its Secret ID." Once saved: masked, then "Replace the client secret" as the other panes. |
| Who may sign in | `audience` | A fieldset of `.um-choice` radio cards, stacked in one column, narrowest first. **None preselected** on a new connector (answer 3a). Values `consumers`, `tenant`, `organizations`, `common`. |
| Directory (tenant) ID | `tenantId` | Under the group, always in the page (as the Fediverse pane's Servers field). Mono, no placeholder. Hint: "The organization's tenant ID, a GUID. Used only with "Work or school accounts from one organization"." Kept when another card is chosen, and ignored then. |
| Shared box, allowed domains | | As the other panes. |

The four cards (answer 3b: each description ends with Microsoft's own
registration label, word for word):

| Value | Title | Description |
| --- | --- | --- |
| `consumers` | Personal Microsoft accounts | Outlook.com, Hotmail, Xbox and Skype accounts. In the registration: Personal accounts only. |
| `tenant` | Work or school accounts from one organization | The people of one Microsoft Entra tenant, given by its tenant ID below. In the registration: Single tenant only. |
| `organizations` | Any work or school account | People from any organization that uses Microsoft Entra. In the registration: Multiple Entra ID tenants. |
| `common` | Personal and any work or school account | Both of the above. In the registration: Any Entra ID Tenant + Personal Microsoft accounts. |

**Refusals** (answer 4) redraw the form with status 422, `aria-invalid` and the
message under the field, every typed value kept except the secret, which is
never drawn back:

| Field | Message |
| --- | --- |
| Application (client) ID | "Enter the Application (client) ID, a GUID like 00000000-0000-0000-0000-000000000000." |
| Client secret (missing on a new connector) | "Enter the client secret's Value." |
| Who may sign in (none chosen) | "Choose who may sign in. It must match the account types chosen when the app was registered." |
| Directory (tenant) ID, with `tenant`, empty | "Enter the Directory (tenant) ID of the organization." |
| Directory (tenant) ID, with `tenant`, not a GUID | "That is not a tenant ID. It is a GUID like 00000000-0000-0000-0000-000000000000." |
| Directory (tenant) ID, with `tenant`, the personal-accounts tenant | "That is the tenant of personal Microsoft accounts: choose Personal Microsoft accounts instead." |
| Display name | The other panes' message. |
| Allowed domains | The existing messages. |

Several fields can be refused at once; each shows its own message.

Notices at the top of the pane, as Apple's, each ending in the next action:

| Code | Text |
| --- | --- |
| `connector-saved-microsoft-unreachable` | "Saved, but not enabled: Umbral could not reach Microsoft (<reason>). Press Enable to try again." |
| `connector-enable-microsoft-unreachable` | "Not enabled: Umbral could not reach Microsoft (<reason>). Try again in a moment." |
| `connector-saved-microsoft-wrong` | "Saved, but not enabled: Microsoft's answer was not usable (<reason>). Check the tenant ID, then press Enable." |
| `connector-enable-microsoft-wrong` | "Not enabled: Microsoft's answer was not usable (<reason>). Check the tenant ID, then press Enable again." |
| `connector-saved-microsoft-unchecked` | "Saved. Umbral could not check the connector with Microsoft (<reason>), so it stays enabled as it was." Only for an enabled connector whose choice did not change; a disabled one gets `connector-saved-microsoft-unreachable` or `-wrong`, which are then true. |

An existing connector whose **choice changed** and whose check fails is not
saved (spec FR-003, documentation review B1): 422, the form redrawn with every
typed value but the secret, and under the "Who may sign in" group (or under the
tenant field when "one organization" is chosen) one of two messages:

- Microsoft's answer was not usable (`Wrong`; a tenant ID that names no tenant is
  answered with HTTP 400 and `"error": "invalid_tenant"`, which discovery reads as
  `Wrong` with the reason "No Microsoft tenant has this ID"). Under the tenant
  field: "Microsoft did not confirm this choice: <reason>. Nothing was saved:
  check the tenant ID, then try again." Under the group (any other choice):
  "Microsoft did not confirm this choice: <reason>. Nothing was saved: check that
  the choice matches the app's registration, then try again." (design review
  nit 1, 2026-11-13)
- Microsoft did not answer (`Unreachable`: a timeout, a 5xx, a network error):
  "Microsoft did not answer (<reason>). Nothing was saved: try again in a moment."

Microsoft's `error_description` is never shown: it carries trace IDs (code
review, 2026-11-13). Nothing is logged for a change that was not saved.

(A tenant ID that names no tenant makes Microsoft's discovery answer an error:
that is the "wrong" case.)

## The sign-in page

Answer 5: the button is an ordinary connector button. Its title is "Sign in with
<display name>" (default "Microsoft"), in token colours, taking an app's accent,
with the link icon. No exception class and no Microsoft logo (research R8).

## Captures for the design review

The list with Microsoft; the pane not set up; set up; each card chosen, including
"one organization" with its tenant field (also at 360, where the field sits under
four stacked cards); the refusal with no choice made; the tenant refusals; the
picker with Microsoft among others. Both themes, 1280 and 360.
