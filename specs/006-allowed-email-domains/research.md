# Research: Allowed Email Domains

Decisions for the plan, each with what was rejected. The spec's Clarifications
fix the behaviour; these fix how it is built.

## R1. The list that applies

**Decision**: `applying (connectorList) (instanceList)` returns the connector's
list when it is not empty, else the instance list when it is not empty, else
`NoRule`. The result also says which level it came from (`Connector` or
`Instance`), for the security event.

**Rationale**: One small total function that the callback, the authorize
endpoint, the token endpoint, the `hd` hint, the connector card label and the
Apps warning all call, so they cannot disagree (the same reason
`Connectors.offeredTo` exists).

**Rejected**: intersection (both must pass) — a connector list could then only
narrow, and an operator who wants a contractor domain on one connector would
have to remove the instance list. A per-connector "anyone" override — a third
state on every connector for a case the operator covers by leaving the instance
list empty; Principle VII.

## R2. Matching

**Decision**: refuse with `NoEmail` when the asserted email is absent, blank,
has no `@`, or has nothing after its last `@`; refuse with `EmailNotVerified`
when the provider did not assert it verified; take the part after the last `@`,
trim one trailing dot, convert with `IdnMapping.GetAscii` and lower-case it
(invariant); refuse with `DomainNotAllowed` unless it equals a listed domain
exactly. A domain the mapping refuses counts as not allowed. The checked order
is: no email, then not verified, then domain.

**Rationale**: verified-only is what makes the rule hold against a generic
provider or an unverified GitHub address (spec Clarifications). Comparing in
the ASCII form makes `münchen.de` and `xn--mnchen-3ya.de` the same, in the list
and in the email. Exact equality keeps `a.acme.com` out unless listed.

**Rejected**: suffix matching (`*.acme.com`): an operator rarely means every
subdomain, and listing them is explicit. Matching on the user's stored email or
on another linked account's: the email of *this* sign-in is the only one the
provider has just vouched for (FR-009).

## R3. What the operator types

**Decision**: split on commas, spaces, tabs and new lines; drop empties; strip a
leading `@` and a trailing `.`; `IdnMapping.GetAscii`; lower-case; drop
duplicates keeping first order. Each entry must then be 1–253 characters, at
least two labels, each label 1–63 characters of `a-z 0-9 -` not starting or
ending with `-`, and a top-level label that is not all digits (so an IP address
is refused). An entry containing `*` gets its own message: list each domain
instead. The first refused entry is named in the message and nothing is saved.
Stored as the ASCII forms joined by one space; shown in the Unicode form
(`IdnMapping.GetUnicode`), joined by `, `.

**Rationale**: forgiving on separators (operators paste lists), strict on what
a domain is, explicit about wildcards because that is the likely mistake.

**Rejected**: a JSON column or a child table — a list of a handful of plain
tokens needs neither, and a space never appears in a domain.

## R4. Storage

**Decision**: schema version 4,
`ALTER TABLE connectors ADD COLUMN allowed_email_domains TEXT NOT NULL DEFAULT '';`
appended to `upgradeSteps` (and the column added to the `CREATE TABLE` so a
fresh database gets it directly). The instance list is the `settings` row with
key `allowed_email_domains`; no row and an empty value both mean no instance
list. Read through a new `Infrastructure/EmailDomainRules.fs`
(`instanceList`, `setInstanceList`, `ruleFor connector`).

**Rationale**: the established upgrade mechanism (append-only steps); the
`settings` table already holds instance-wide values (credential, leases).
`DEFAULT ''` makes every existing connector "use the instance list", which is
"no rule" on an upgraded instance: SC-006.

## R5. Where the callback checks

**Decision**: in `Callback.fs`, after `ExchangeAndFetch` returns a profile and
before `UserStore.resolve`. A refusal records the event and redirects with the
failure code `account-not-allowed`; `resolve` is never called, so no user, link,
`last_seen_utc` or profile field changes (FR-008). The disabled-user check
stays after `resolve`, so a disabled user from a refused domain sees the domain
refusal, which reveals nothing more.

**Rejected**: checking inside `UserStore.resolve` — it would mix a policy into
the one function whose only job is identity, and `resolve` writes in a
transaction the check does not need.

## R6. Sessions (FR-012)

**Decision**: `Sessions.signIn` gains the domain of the verified email checked
at this sign-in, written as claim `email_domain` when there is one (absent for
no email or an unverified one). In `Authorize.fs`, when a session is honoured
for the app (003 FR-008), the connector is looked up by the session's slug and
the rule that applies is checked against the claim, with "verified" taken as
true when the claim exists and "no email" when it does not. A refusal signs the
session out, records the event (stage `authorize`) and continues down the
no-session branch: `login_required` under `prompt=none`, otherwise the
sign-in page.

**Rationale**: the domain is all a re-check needs, so the address never has to
sit in a cookie. A session from before this feature has no claim and is
therefore treated as "no email": refused while a rule applies, honoured as
before when none does.

**Rejected**: re-reading the email from `users` or `linked_accounts` — the
user's email may come from another provider (R2), and the linked-account row is
per provider kind, not per connector (003 FR-012).

## R7. Tokens (FR-013)

**Decision**: at authorization, the identity gets two private claims,
`umbral_connector` (the session's slug) and `umbral_email_domain` (the session's
`email_domain`, when present), both with **no destination**, so they never
reach an access or identity token. OpenIddict keeps every claim of the
principal in authorization codes and refresh tokens, which are encrypted, and
`Token.fs` already rebuilds the identity from the principal's own claims, so
they ride along each refresh. `Token.fs` checks them on **both** grants, after
the enabled-user check:

- connector claim present: rule of that connector (a connector deleted since
  then is judged by the instance list alone) against the domain claim;
- connector claim absent (issued before this feature): refused when the
  instance list or any connector's list is set, honoured otherwise.

A refusal answers `invalid_grant` through `Authorize.forbid` and records the
event (stage `token`, with the client id). `destinationsFor` maps the two claim
types to no destination explicitly. A test pins that the claims survive the
code → refresh → refresh chain and never appear in an access or id token.

**Rationale**: the check needs exactly what was checked at sign-in, and the
principal OpenIddict protects is where that already lives. Checking the code
grant too costs nothing and closes the five minutes between authorization and
redemption.

**Rejected**: storing the connector on the OpenIddict authorization entity — it
is not Umbral's table (Database.fs) and the principal already carries it.
Revoking authorizations when a rule is saved — see R8.

## R8. Nothing is revoked when a rule is saved (FR-014)

**Decision**: saving a list changes only the list. Sessions and refresh tokens
are judged at their next use (R6, R7); access tokens live out their lifetime
(OpenIddict's default, one hour; Umbral sets none).

**Rationale**: disabling a user (001 FR-022) revokes at once because it names
one person and their authorizations are found by subject. A rule names a set
that is only known one sign-in at a time: finding it at save time would mean
re-deriving every user's per-connector email, which Umbral does not store (R6).
The long-lived paths, session and refresh, are closed at their next use; the
one-hour access token is the same window FR-022 already accepts for a token
validated without a database lookup.

## R9. Google's `hd`

**Decision**: `ProviderAdapters.AuthorizeRequest` gains
`HostedDomainHint: string option`. `Start.fs` sets it when the connector's kind
is Google and the rule that applies has exactly one domain; the Google adapter
appends `hd=<domain>`, and every other adapter ignores the field. A golden URL
test pins both the presence and the absence.

**Rationale**: `BuildAuthorizeUrl` stays pure (it already takes everything as
input). The hint is advisory; R2 still runs on what Google returns.

## R10. Security events and admin plumbing

**Decision**:

- A refusal is `sign_in_failed` with detail
  `email_domain_refused;reason=<domain_not_allowed|email_not_verified|no_email>;rule=<connector|instance>;stage=<callback|authorize|token>;domain=<ascii domain or ->`.
  The domain is the normalised ASCII form or `-` when there is none or it did
  not normalise; it is built from the rule's own output, never copied raw from
  the provider.
- New event type `allowed_domains_changed` (operator), with `ConnectorSlug`
  set for a connector list and absent for the instance list, and detail
  `count=<n>`. Recorded only when the stored list actually changes. Added to
  `allEventTypes`; the coverage test emits it.
- The instance card posts to `POST /admin/allowed-domains`, not under
  `/admin/connectors/{slug}`, so no slug can collide with it. Without
  `?confirm=yes`, emptying a non-empty list while any connector has no list of
  its own redirects to the Connectors screen with notice
  `allowed-domains-removal` and a warning naming those connectors, whose button
  re-posts the empty list with `?confirm=yes` (the strand-warning pattern).
- The connector forms post `allowedDomains`. On the card save, a form that
  does not carry the field (the unshare confirmation) leaves the list as it is;
  an empty field clears it.
- The Apps warning: for each app, the enabled connectors `offeredTo` it are
  split by whether `applying` gives a rule; when both halves are non-empty, the
  row shows a warning naming the unrestricted ones (`data-warning="unrestricted-connectors"`),
  and saving the app's choice shows it as a notice. Never blocks.
