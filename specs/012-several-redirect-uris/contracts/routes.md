# Contract: routes, notices, words

All four routes sit behind `AdminAuth.requireOperator`, read the form through
`WebForms.readValidated` (antiforgery), and answer with a redirect to
`/admin/apps/{clientId}?msg=<code>` (the app's pane), or
`/admin/apps?msg=app-not-found` when the app does not exist.

| Route | Form | Success notice |
| --- | --- | --- |
| `POST /admin/apps/{clientId}/redirect-uris` | `uri` | `app-redirect-uri-added` |
| `POST /admin/apps/{clientId}/redirect-uris/remove` | `uri` (the registered string) | `app-redirect-uri-removed` |
| `POST /admin/apps/{clientId}/post-logout-uris` | `uri` | `app-post-logout-uri-added` |
| `POST /admin/apps/{clientId}/post-logout-uris/remove` | `uri` | `app-post-logout-uri-removed` |

Refusals (all drawn as warnings in the app's pane):

| Code | When |
| --- | --- |
| `app-redirect-uri-invalid` | `RedirectUris.acceptable` refuses the value (either list; today's text) |
| `app-redirect-uri-duplicate` | already in the list (either list) |
| `app-redirect-uri-limit` | the list holds 10 (either list) |
| `app-redirect-uri-last` | removing the last redirect URI |
| `app-redirect-uri-not-registered` | removing a URI the list does not hold (either list) |

## Words (`Views/Texts.fs`)

Proposed; the design review may change them.

- Section labels: "Redirect URIs", "Post-logout redirect URIs".
- Redirect list hint: "Umbral sends people back only to these addresses, matched exactly. Up to 10."
- Post-logout list hint: "Where Umbral may send people after they sign out, matched exactly. Up to 10."
- Post-logout empty: "None. After signing out, people land on Umbral's home page."
- Last redirect URI (instead of its Remove): "An app needs at least one redirect URI. Add the new one before you remove this one."
- Full list (instead of the add form): "This list is full (10). Remove one to add another."
- Add field label: "Add a redirect URI" / "Add a post-logout redirect URI"; button "Add".
- Remove button: "Remove" (visually), with an accessible name "Remove <uri>".
- Confirm, redirect: "Remove <uri> from <app>? Sign-ins that send this address will be refused."
- Confirm, post-logout: "Remove <uri> from <app>? People who sign out with this address will land on Umbral's home page instead."
- `app-redirect-uri-added`: "Redirect URI added."
- `app-redirect-uri-removed`: "Redirect URI removed. Sign-ins that send it are refused from now on."
- `app-post-logout-uri-added`: "Post-logout redirect URI added."
- `app-post-logout-uri-removed`: "Post-logout redirect URI removed."
- `app-redirect-uri-duplicate`: "That address is already registered for this app."
- `app-redirect-uri-limit`: "An app can have at most 10 addresses in this list. Remove one, then add the new one."
- `app-redirect-uri-last`: "An app needs at least one redirect URI, or nobody can sign in to it. Add the new one first, then remove the old one."
- `app-redirect-uri-not-registered`: "That address isn't registered for this app any more."
- Create form, redirect URI hint (amended): "The exact, absolute callback URL the app expects — no wildcards. You can add more on the app's page."

## The pane

The **Connection** section keeps Client ID and Issuer URL as they are. Under
them, two sub-blocks, each: a label, the list (mono, `overflow-wrap:anywhere`,
each row with a small ghost Remove in the error colour and a confirmation), a
hint, and an inline add form (text field and a small secondary Add). This
amends `design/contracts/apps-connectors-master-detail.md` §2 item 3 ("the
redirect URIs as a mono list. No form."). Nothing else in the pane moves.
