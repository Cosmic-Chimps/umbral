# Data model: Several redirect URIs per app

No change. An app is an OpenIddict application (research R1):

| Field (OpenIddict) | Before | After |
| --- | --- | --- |
| `RedirectUris` | one item, set at creation | 1 to 10 items, editable |
| `PostLogoutRedirectUris` | 0 or 1 item, set at creation | 0 to 10 items, editable |
| `ClientId`, `ClientSecret` | set at creation (secret rotated by Regenerate) | unchanged by any URI edit |

`SchemaVersion` stays 9. Existing apps are read as they are.
