# Contract: routes, server calls, claims, hooks and copy

**Spec**: [../fediverse-connector.spec.md](../fediverse-connector.spec.md) · **Plan**: [../plan.md](../plan.md) · **Research**: [../research.md](../research.md) · **Design**: `design/contracts/fediverse-connector-ui.md`

This is the interface spec 010 exposes. Identifiers are copied from the spec's
Verbatim Constraints and must not be respelled. Supported software, everywhere
in this file: Mastodon 4.3 and later, and GoToSocial. Akkoma, Pleroma, Misskey
and Sharkey are not supported.

Fixed identifiers:

- Connector slug and kind: `fediverse`.
- Requested scope on a server: `read:accounts`, and nothing else.
- Claims an app gets: `name`, `preferred_username`, `picture`, `profile`. No `email`.

## 1. Routes

Literal routes outrank the generic `{slug}` connect routes and the generic
connector admin routes (research R13).

### Sign-in routes (rate limit `sign-in`, 30 a minute per address)

| Method and path | Input | Responses |
| --- | --- | --- |
| `GET /connect/fediverse/start` | query `returnUrl` | 200 the account step in the picker's chrome (plain, or the app's branding), field empty, `returnUrl` carried as a hidden field. 404 (the not-found page) when the connector does not exist or is disabled, as `/connect/passkey/start` does; a disabled connector is not discoverable by probing. The POST answers the same way. |
| `POST /connect/fediverse/start` | form `returnUrl`, `account`, the antiforgery token | 302 to the server's authorize URL (see section 2) with the single-use `state` cookie set. 422 the account step again, with the typed `account` kept, `aria-invalid`, the message under the field and `autofocus`, for every problem the person can fix (section 5). 400 on a missing or invalid antiforgery token, as the other forms. |
| `GET /connect/fediverse/callback` | query `code`, `state`, or `error` | 302 to the `returnUrl` held in the correlation cookie when the sign-in succeeds. Otherwise 302 to `/signin?msg=<code>` where `<code>` is one of `access-denied` (the person refused at the server), `sign-in-failed` (wrong or reused state, exchange failed, account unreadable, registration gone, connector disabled) or `account-not-allowed` (the server is no longer on the list, FR-019). Codes come from `Picker.redirectWithFailure`; no new page and no new code. |

The `account` field accepts `@ana@mastodon.social`, `ana@mastodon.social`,
`mastodon.social` and `https://mastodon.social` (spec Clarifications). It is
normalised as research R5 says. A server on the account step is never contacted
before the checks of R3 (scope) and the address check of R6.

### Admin routes

| Method and path | Input | Responses |
| --- | --- | --- |
| `GET /admin/connectors/fediverse` | none | 200 the Connectors screen with the Fediverse entry selected and its pane. The pane exists before and after set-up. Operator only. |
| `POST /admin/connectors/fediverse` | form `shared` (checkbox, `true` when ticked), `scope` (`any` or `listed`), `servers` (text), antiforgery | 302 to `/admin/connectors/fediverse` with notice `fediverse-connector-saved` (created or updated) or `connector-strands-apps` (unsharing would strand an app, answered with `?confirm=yes`, as the Passkey save). 302 with `fediverse-connector-invalid` when `scope` is neither word. 422 the Connectors screen again, pane selected, `servers` as typed and the field error under it, when an entry is refused; nothing is saved. When the connector does not exist the save creates it enabled, slug `fediverse`, display name `Fediverse`, empty client id and secret reference. |
| `POST /admin/connectors/fediverse/toggle` | antiforgery, optional `?confirm=yes` | The reused generic `POST /admin/connectors/{slug}/toggle`. Same notices and last-connector confirmation as every connector. |
| `POST /admin/connectors/fediverse/delete` | antiforgery, optional `?confirm=yes` | The reused generic `POST /admin/connectors/{slug}/delete`. Deleting keeps server registrations and fediverse users (spec Clarifications). Redirects to `/admin/connectors`. |

`servers` is parsed even when `scope` is `any`, so a refused entry is never
silently kept; with `any` the stored list is kept as saved. An empty list with
`listed` is saved, and the pane says nobody can sign in (spec Edge Cases).

`POST /admin/connectors/fediverse` is the only way to create the connector. The
generic empty-card create (`mayCreate`) answers false for this kind.

## 2. Calls Umbral makes to a server

All calls go through the named client `umbral-fediverse` (research R6). Limits
that apply to every call below:

- A 10 second limit per call, and a 5 second connect timeout.
- No redirects are followed.
- Every body is read through a bound of 64 KiB.
- The scheme is `https` and the host is the server's own host. Every endpoint a
  server publishes (authorize, token, registration) must also be `https` on that
  host, otherwise the server is unsupported. Under the development setting
  (`ASPNETCORE_ENVIRONMENT=Development` and `UMBRAL__INSECURE_ALLOW_HTTP=true`)
  `http` and non-public addresses are allowed.
- The connect callback keeps only public addresses (research R6), so a name that
  resolves to a private address is refused even after the earlier check.

Order of calls for one sign-in:

1. **WebFinger, handles only.** `GET https://<handle domain>/.well-known/webfinger?resource=acct:<user>@<domain>`. The host of the `self` link with type `application/activity+json` is the server. If the call fails or has no such link, the handle's domain is the server. The resulting host is normalised and checked like typed input, and against the list when scope is `listed` (research R3).
2. **OAuth server metadata.** `GET https://<server>/.well-known/oauth-authorization-server`. Supported when `code_challenge_methods_supported` contains `S256` and `app_registration_endpoint` is present. `authorization_endpoint` and `token_endpoint` are read from it.
3. **NodeInfo, only when step 2 gives no metadata.** `GET https://<server>/.well-known/nodeinfo`, then the 2.0 or 2.1 document it links. Supported when `software.name` is `gotosocial`. Its endpoints are `/api/v1/apps`, `/oauth/authorize` and `/oauth/token` on the server host.
4. **Registration, first use of a server only.** `POST <app_registration_endpoint>` with `client_name` = `Umbral`, `redirect_uris` = the callback address (`<public origin>/connect/fediverse/callback`), `scopes` = `read:accounts`, `website` = the public origin. The answer's `client_id` and `client_secret` are stored in `fediverse_servers` (secret protected, purpose `Umbral.FediverseServers.v1`). A refusal or an answer without both values is `registration_failed`.
5. **Client check, when the stored registration was not checked in the last 24 hours (research R8).** `POST <token_endpoint>` with `grant_type=client_credentials`, `client_id`, `client_secret`, `scope=read:accounts`. A token answer stamps `checked_utc`. `invalid_client` (400 or 401) means the registration is gone: register again once (step 4) and replace the row, and a second refusal ends the attempt as `registration_failed`. Any other failure is `unreachable`.
6. **Authorize redirect (302 to the person's browser, not a call).** `GET <authorization_endpoint>` with `response_type=code`, `client_id`, `redirect_uri` = the callback, `scope=read:accounts`, `state` (single use, bound to the server in the correlation cookie), `code_challenge`, `code_challenge_method=S256`.
7. **Token exchange, at the callback, at the server the sign-in started with.** `POST <token_endpoint>` with `grant_type=authorization_code`, `code`, `redirect_uri`, `client_id`, `client_secret`, `code_verifier`. `invalid_client` here deletes the stored registration so the next attempt registers again, and the person sees `sign-in-failed`.
8. **Account.** `GET https://<server>/api/v1/accounts/verify_credentials` with `Authorization: Bearer <access token>`. The answer's `id` is the account id; an absent or empty `id` fails the sign-in (`account_unreadable`) and creates no user. `display_name` (or `username` when empty), `avatar`, `username` and `url` feed the profile (research R10). The access token is discarded after this call.

Nothing is sent to a server that is not on the list, for a non-public address,
or that fails the support rule of steps 2 and 3.

## 3. Claims an app gets

Identity: linked account kind `fediverse`, subject `<host>#<account id>`. The
host is the normalised server address, so the same person is the same user on
every sign-in and the display name or handle never matters.

Under the `profile` scope, in the id token, the access token and the userinfo
answer:

| Claim | Value |
| --- | --- |
| `name` | The account's display name, or its username when that is empty. Refreshed at every sign-in. |
| `preferred_username` | `<username>@<server host>`. |
| `picture` | The account's avatar address. |
| `profile` | The account's page address, only when it is `https`. |

No `email` and no `email_verified`, whatever scopes the app asks for. A claim
with no value is absent, as `picture` is today. A fediverse identity is never
linked to another user by email, and no allowed-email-domain rule applies to it.

## 4. Data hooks and form fields

Connectors pane (the same pane carries all of them):

- `data-connector="fediverse"`
- `data-connector-kind="fediverse"`
- `data-connector-state` with `enabled` or `disabled`, present only when the connector exists
- `data-connector-shared` with `shared` or `not-shared`
- `data-instance-scope` on the fieldset of the radio pair
- `data-instance-list` on the Servers field

The radio inputs are named `scope`, with values `any` and `listed`, drawn as
`.um-choice` cards like the Passkey enrolment radios. The Servers input is
named `servers`. The shared check is named `shared`. The existing hooks of the
other panes stay as they are: `data-connector-actions`, `data-offered-nowhere`,
`data-callback-url` on the callback copy field, and `data-connector-entry="fediverse"`
with `data-entry-muted` on the list entry when not set up.

Account step:

- The form posts to `/connect/fediverse/start`, with the hidden field `returnUrl` and the antiforgery input.
- The one visible field is named `account`. Attributes: `autocomplete="username"`, `autocapitalize="none"`, `spellcheck="false"`, `inputmode="email"`, `autofocus`. A refused input adds `aria-invalid="true"` and the message goes under the field as `um-field-error`.
- The "Other ways to sign in" link goes to `/signin?returnUrl=<returnUrl>`, and is drawn only when the app offers more than one connector.

## 5. Catalogue strings

All land in `Views/Texts.fs`. `{server}` and `{entry}` are the normalised server
or the typed entry, drawn as text and never as markup or a link. Existing
catalogue entries are reused where noted (`SignInWith`, `Copy`, `Copied`,
`NotConfigured`, `SaveConnector`, `Enabled`, `Disabled`, `Enable`, `Disable`,
`ConnectorSharedLabel`, `ConnectorSharedHint`, `ConnectorSetupTitle`,
`ConnectorSettingsTitle`, `CallbackUrlLabel`, `ConnectorDeleteButton`,
`ConnectorDeleteConfirm`, `ConnectorDeleteLastWarning`,
`ConnectorDisableLastWarning`, `ConnectorOfferedNowhere`).

### Connectors pane

| Proposed key | String |
| --- | --- |
| `FediverseCardLead` | People sign in with an account on a Mastodon-compatible server. Umbral registers itself with each server the first time someone from it signs in. |
| `FediverseNoEmailNote` | A fediverse account has no verified email, so allowed email domains do not apply to it and it never joins another account by email. |
| `FediverseScopeAnyLabel` | Any server |
| `FediverseScopeAnyHint` | Anyone with an account on a server that works like Mastodon. |
| `FediverseScopeListedLabel` | Only these servers |
| `FediverseScopeListedHint` | Only accounts on the servers listed below. |
| `FediverseServersLabel` | Servers |
| `FediversePlaceholder` | mastodon.social, fosstodon.org |
| `FediverseServersHint` | Server addresses, separated by commas or spaces. Used only with "Only these servers". |
| `FediverseServersEmptyNote` | The list is empty, so nobody can sign in through Fediverse. Add a server, or choose Any server. (Proposed; the spec requires the pane to say so, the design file gives no wording.) |
| `FediverseEnableButton` | Enable Fediverse |
| `FediverseServerNotAddress entry` | "{entry}" is not a server address. |
| `FediverseServerWildcard entry` | "{entry}" has a wildcard; list each server. |

The two entry refusals mirror `AllowedDomainsNotADomain` and
`AllowedDomainsWildcard`. Notices for the Connectors screen (`Texts.knownNotice`):

| Code | String |
| --- | --- |
| `fediverse-connector-saved` | Saved. People can sign in with a Fediverse account from the next page load. |
| `fediverse-connector-invalid` | Choose Any server or Only these servers, then save again. |

The enabled, disabled and deleted notices reuse the existing ones with the
connector's name.

### Sign-in page and account step

| Proposed key | String |
| --- | --- |
| `SignInWith "Fediverse"` | Existing `SignInWith` with the display name `Fediverse`; no new key. |
| `FediverseStepTitle` | Where is your account? |
| `FediverseAccountLabel` | Your account or server |
| `FediverseAccountPlaceholder` | @you@mastodon.social |
| `FediverseAccountHint` | A handle like @you@mastodon.social, or a server like mastodon.social. Mastodon and GoToSocial servers work. |
| `FediverseContinue` | Continue |
| `FediverseOtherWays` | Other ways to sign in |
| `FediverseNotAHandle` | Enter a handle like @you@mastodon.social, or a server address. |
| `FediverseUnsupported server` | {server} does not offer the sign-in Umbral uses. Mastodon and GoToSocial servers do; Akkoma, Pleroma, Misskey and Sharkey do not yet. Use another account, or another way to sign in. |
| `FediverseNotAllowed server` | Accounts on {server} cannot sign in here. Use another way to sign in. |
| `FediverseUnreachable server` | {server} did not answer. Try again in a moment. |

`FediverseNotAHandle` is also the text for a private, loopback or link-local
address outside development. The page title is `FediverseStepTitle`. The
sign-in error page for callback failures is the existing one and needs no string.

## 6. Security log

New event type, written when a server registration is stored (a credential
change, FR-023), including a re-registration:

- `fediverse_server_registered`, connector slug `fediverse`, detail `server=<host>`.

Failures are `sign_in_failed` with connector slug `fediverse` and detail
`fediverse:<reason>;server=<host>` (research R12). The server is named when it
is known; for `not_a_server` there is none and the detail is `fediverse:not_a_server`.
No token, code, client secret or typed text beyond the server name is logged.

| Reason | When |
| --- | --- |
| `not_a_server` | The input is neither a handle nor a server address. |
| `non_public_address` | The server is a literal IP, `localhost`, or resolves to a non-public address, outside development. |
| `server_not_allowed` | Scope is `listed` and the typed domain, or the server WebFinger named, is not on the list; also the callback re-check (the person then sees `account-not-allowed`). |
| `unsupported` | No `S256` support or no app registration (steps 2 and 3 of section 2). |
| `unreachable` | A call timed out, returned an error, or an endpoint broke the limits of section 2. |
| `registration_failed` | Registration was refused or answered without a client id and secret, including the second refusal after a re-registration. |
| `client_rejected` | The client check or the code exchange answered `invalid_client`. |
| `exchange_failed` | The code exchange failed for any other reason, or the state was wrong or reused. |
| `account_unreadable` | The account call failed, or the answer has no usable `id`. |

The account step shows the fixable reasons (`not_a_server`, `non_public_address`,
`server_not_allowed`, `unsupported`, `unreachable`, and `registration_failed`
as the "did not answer" line) as 422 messages. `client_rejected`,
`exchange_failed` and `account_unreadable` occur at the callback and redirect to
`/signin?msg=sign-in-failed`.
