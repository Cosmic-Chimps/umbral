# Contract: the declaration

## Variables

| Variable | Holds | Secret |
| --- | --- | --- |
| `UMBRAL__DECLARATION` | The declaration, a JSON document (below). Unset or empty: no declaration (FR-001). | No |
| `UMBRAL__APP_SECRETS__<key>` | The client secret of the confidential app `<key>`, at least 32 characters. Exactly one per `web` app, none for a `spa` app. | Yes |

A variable `UMBRAL__APP_SECRETS__<key>` whose key is not a declared `web` app is
refused (a leftover secret for a removed or renamed app is a mistake worth
naming). The configuration keys are case-insensitive, as all of Umbral's; the
key itself is lowercase by its rule.

## Document, version 1

```json
{
  "version": 1,
  "passkeys": { "signUp": "open" },
  "apps": [
    {
      "key": "web",
      "name": "Sample web app",
      "type": "web",
      "redirectUris": ["http://localhost:5000/signin-oidc"],
      "postLogoutRedirectUris": ["http://localhost:5000/signout-callback-oidc"],
      "connectors": "all-shared"
    }
  ]
}
```

| Property | Required | Rule |
| --- | --- | --- |
| `version` | yes | `1`. Any other value is refused with "this Umbral reads declaration version 1". |
| `passkeys` | no | Absent: the passkey connector is not declared (an existing declared one is un-declared). Present: the connector exists, is enabled and shared. |
| `passkeys.signUp` | no | `invitations` (default, Umbral's own default) or `open`. |
| `apps` | no | A list; absent is the empty list. Keys unique within it. |
| `apps[].key` | yes | `[a-z0-9-]{1,32}` (`Ids.isValidSlug`). Becomes the client ID. |
| `apps[].name` | no | The display name; default the key. Not empty after trimming, at most 100 characters. |
| `apps[].type` | yes | `web` (confidential, holds a secret) or `spa` (public, PKCE only), the admin area's two types. |
| `apps[].redirectUris` | yes | 1 to 10, each accepted by `RedirectUris.check`, no duplicate after normalisation. |
| `apps[].postLogoutRedirectUris` | no | 0 to 10, same rules. |
| `apps[].connectors` | no | `"all-shared"` (default) or a list of connector slugs (`Only`). A slug no connector has is accepted with a warning line. |

Any other property, at any level, is refused.

## Refusal lines

Umbral checks everything, logs one Error line per problem, then one line
"The declaration in UMBRAL__DECLARATION was refused; nothing was changed. Fix
the problems above and start again." and exits with a non-zero code. Each
problem line names the item (`apps[1] (key "web")`), the property and what to
do, never a secret value. Examples:

- `apps[0] (key "web"): the client ID "web" belongs to an app created in the admin area. Give the declared app another key, or delete that app on the Apps screen.`
- `apps[0] (key "web"): UMBRAL__APP_SECRETS__web is not set. A web app needs a secret of at least 32 characters.`
- `apps[1] (key "spa"): UMBRAL__APP_SECRETS__spa is set, but a spa app is public and holds no secret. Remove the variable.`
- `apps[0] (key "web"): UMBRAL__APP_SECRETS__web is shorter than 32 characters.`
- `apps[0] (key "web"): redirectUris[0] "https://*.example.com/cb" contains a wildcard. Redirect URIs are matched exactly.`
- `passkeys: the passkey connector was set up in the admin area. Remove "passkeys" from the declaration; the connector stays as it is.`
- `UMBRAL__APP_SECRETS__old: no declared web app has the key "old". Remove the variable.`

## The start line

One Information line per start with a declaration:
`Declaration applied: 1 app created, 0 updated, 0 unchanged, 0 no longer declared; passkey connector created.`
With no variable and nothing to un-declare, no line.

## Application order (one transaction)

1. Passkey connector (create, enable, set sign-up and sharing), so an app's
   `connectors` naming `passkey` finds it.
2. Each app in the declaration's order: create, or update the fields that
   differ, then the secret check (D7).
3. Un-declare what is marked declared and not named.
4. The security events, in the same transaction (`SecurityEvents.recordIn`).

OpenIddict's application manager writes through its own EF Core context, not
through the raw connection of the transaction. The plan says how the two are
made one unit (plan.md, R2).
