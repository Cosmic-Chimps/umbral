# Contract: Sign-in Behaviour with Allowed Email Domains

What a person, an app and a test observe. "Rule" is what `applying` gives for
the connector (research R1). With `NoRule` every row below behaves exactly as
before this feature.

## Return from the provider — `GET /connect/{slug}/callback`

Checked after the provider's profile is fetched, before any user work.

| Profile for this sign-in | Rule | Result |
| --- | --- | --- |
| any | none | unchanged |
| verified email, domain listed (case-insensitive, IDN-equal) | set | unchanged: user resolved, session, redirect to the app |
| verified email, domain not listed (subdomain included) | set | refused |
| email not verified | set | refused |
| no email, blank, or no domain part | set | refused |

**Refused** means:

- `302` to the app's sign-in page with failure code `account-not-allowed`
  (the same mechanism as `account-disabled`);
- no row written to `users` or `linked_accounts`, no existing row changed;
- no session cookie set;
- one `sign_in_failed` event, detail
  `email_domain_refused;reason=<domain_not_allowed|email_not_verified|no_email>;rule=<connector|instance>;stage=callback;domain=<ascii or ->`,
  no user id.

The page shows, for `account-not-allowed`, exactly:

> This account can't be used to sign in here. Try a different account or another option below, or contact whoever runs this Umbral.

It never contains a listed domain, the person's domain, or the reason.

**Admitted** sessions carry claim `email_domain` = the verified email's domain
(absent when the sign-in had no verified email).

## Existing session — `GET|POST /connect/authorize`

When a session is honoured for the app (003 FR-008) and the rule of the
session's connector does not admit the session's `email_domain` (absent counts
as no email):

- the session cookie is removed;
- one `sign_in_failed` event, stage `authorize`, with the user id and the
  client id;
- then the no-session branch: `prompt=none` → `login_required` error to the
  app; otherwise `302 /signin?returnUrl=…`.

When admitted, the issued identity carries `umbral_connector` and
`umbral_email_domain` with no destination.

## Token endpoint — `POST /connect/token`

For `authorization_code` and `refresh_token` grants, after the user checks:

| Principal | Result |
| --- | --- |
| `umbral_connector` present, rule of that connector (instance list if the connector is gone) admits `umbral_email_domain` | tokens issued as before |
| same, not admitted | `400 invalid_grant`, one `sign_in_failed` event, stage `token` |
| `umbral_connector` absent, no list set anywhere | tokens issued as before |
| `umbral_connector` absent, instance list or any connector list set | `400 invalid_grant`, event with `rule=instance` and `reason=no_email` |

Neither claim appears in any access token, id token or userinfo response.

## Google hosted-domain hint — `GET /connect/{slug}/start`

| Connector kind | Domains in the rule | Authorize URL to the provider |
| --- | --- | --- |
| Google | exactly 1 | carries `hd=<that domain>` |
| Google | 0 or ≥ 2 | no `hd` |
| any other | any | no `hd` |
