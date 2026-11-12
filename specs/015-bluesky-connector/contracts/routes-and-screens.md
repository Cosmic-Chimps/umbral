# Routes and screens: the Bluesky connector

The screens are those of `design/contracts/bluesky-connector-ui.md`, the designer's answers of 2026-11-12. Its base is `design/contracts/fediverse-connector-ui.md`. Where the two design notes and this file disagree, the design notes win for looks and wording, and this file wins for routes and behaviour.

## Routes

| Method and path | Rate limit | What it does |
|---|---|---|
| `GET /connect/bluesky/start?returnUrl=` | SignIn | The step page. 404 when the connector is not enabled. The sign-in error page when the origin can't work (FR-004). |
| `POST /connect/bluesky/start` | SignIn, antiforgery | Takes `handle` and `returnUrl`. Resolves, checks, pushes the request, and answers 302 to the authorization endpoint. A fixable problem gets 422 and the step again. |
| `GET /connect/bluesky/callback?code&state&iss` | SignIn | R6, then the shared sign-in tail (`Callback.finishSignIn`). |
| `GET /connect/bluesky/client-metadata.json` | none | The client metadata document (FR-005). 404 when the connector is not set up, or when the origin can't work. |
| `POST /admin/connectors/bluesky` | admin, antiforgery | Saves shared, scope and servers. It creates the connector disabled the first time ("Enable Bluesky" saves and enables, as Fediverse's does). It is refused when the origin can't work. |

Enable, Disable and Delete use the existing connector routes. Enable refuses `bluesky` when `Bluesky.clientIdFor` fails.

## Picker

"Sign in with Bluesky", with the link icon (research R7), the Last used badge, and identifier order. The link goes to `/connect/bluesky/start?returnUrl=…`.

## Step page

| Element | Value |
|---|---|
| `h1` | What is your Bluesky handle? |
| Label | Handle |
| Field name | `handle` |
| Placeholder | `you.bsky.social` |
| Hint | Like you.bsky.social, or your own domain if you use one. It is on your Bluesky profile. |
| Attributes | `autocomplete=username`, `autocapitalize=none`, `spellcheck=false`, `inputmode=email`, autofocus |
| Buttons | Continue; Other ways to sign in, when the app offers more than one connector |

Its errors:

| Case | Text | Security log reason |
|---|---|---|
| Not a handle, a private address, or an unknown DID method | Enter a Bluesky handle like you.bsky.social. | `not_a_handle` |
| No DID, a DID document that doesn't name the handle, or an unreadable document | No Bluesky account was found for {handle}. Check the spelling. | `handle_not_found` |
| Allowlist | Accounts on {server} cannot sign in here. Use another way to sign in. | `server_not_allowed` |
| A timeout or an error from a server on the way | {server} did not answer. Try again in a moment. | `server_unreachable` |
| The metadata fails FR-010, the resource names zero or several servers, the issuer doesn't match, or PAR is refused twice for a nonce | The server for {handle} does not offer the sign-in Umbral uses. Use another way to sign in. | `server_unsupported` |

Every security event's detail is `bluesky:<reason>`, followed by `;server=<host>` when a host is known. Callback failures use `bluesky:<word>` with the R6 words: `iss_mismatch`, `server_not_allowed`, `exchange_failed`, `token_invalid`, `did_mismatch` and `not_authoritative`. They redirect to the existing error page with the same codes the Fediverse callback uses.

## Connectors pane

As design note 4, in this order:
- **Lead.**
- **Set-up:** the client metadata address with copy, under the label "Client metadata address", and its hint; then the no-email note.
- **Settings:** shared; the scope cards "Any server" and "Only these servers"; the Servers field (placeholder `bsky.social`); Save connector, or Enable Bluesky.
- **The closing row.**

On an origin that can't work, the pane shows the lead and the Warning alert only. With the development setting on and a loopback origin, the Set-up section shows an info note in place of the address: "Development: Umbral signs in as AT Protocol's localhost client, so no address is fetched."

Data hooks: `data-connector="bluesky"`, `data-connector-kind="bluesky"`, `data-connector-state`, `data-connector-shared`, `data-instance-scope`, `data-instance-list`.

List order: configured providers, kinds not set up, Fediverse, **Bluesky**, Passkey, then the pinned allowed domains.
