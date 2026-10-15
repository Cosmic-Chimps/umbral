# Feature Specification: Allowed Email Domains, per Instance or per Connector

**Feature Branch**: `006-allowed-email-domains` (built on the worktree branch `alta/sincere-bunting-jgtq`)

**Created**: 2026-10-15

**Status**: Draft

**Input**: User description: "Restrict who can sign in by email domain, globally or per connector." Origin: the task recorded with the project owner on 2026-10-14 (`.alta/tasks/2026-10-14-restrict-who-can-sign-in-by-email-domain-globally-or-per-con-jnf6.md`), including the decision that the rule is set per connector, never per app.

## Context

Today any person with an account at an enabled provider can become an Umbral
user. An operator who adds a Google connector for their company wants only
`@acme.com` accounts to get in, not every Gmail user. Disabling a user (001
FR-021, FR-022) does not help: it can only block someone *after* their first
sign-in has created them.

This feature lets the operator list **allowed email domains** in two places:

- **On the instance**: one list that applies to every connector.
- **On a connector**: a list that applies to that connector only, for example
  `acme.com` on "Google (work)" while GitHub stays open.

A person whose verified email is not in the list that applies is refused at
sign-in and never becomes a user.

### Why this is not an authorization model

001 FR-025 says "Umbral MUST NOT offer a per-app user allowlist or any other
authorization model". Constitution Principle VII lists "RBAC or any
authorization model" as permanently out of scope, and 003 listed "per-app user
allowlists" as out of scope. This feature is read as consistent with all three,
for these reasons:

- The rule is set on a **connector** or on the **instance**, never on an app.
- It answers the question "who can be an Umbral user at all", the same
  question "disabled user" already answers. It does not answer "what may this
  user do in this app", which stays the app's job.
- It is a list of domains, not of people. There are no per-person entries, no
  groups, no roles.
- Dex (`hostedDomains`), oauth2-proxy (`--email-domain`) and Google's own `hd`
  parameter work the same way, and none of them is called an authorization
  model.

This reading is recorded in 001 FR-025 and in a clarification to Principle VII
(FR-002), so the next request is judged against a written line, not against
this argument.

### Per connector, not per app

Decided with the project owner on 2026-10-14. One app can still be limited, by
building it from the parts of 003: create a connector for the app, for example
"Google (Acme)" limited to `acme.com`, and offer only that connector to the app.
003 FR-008 makes this airtight: an app accepts a session only if it was started
through a connector the app offers. Someone signed in through an open connector
still gets the app's sign-in page, where the restricted connector is the only
button. 003 FR-012 and SC-004 keep the same provider account as one Umbral user
across both connectors.

Rules by app, or by app *and* connector, were rejected:

- A rule by app is the "per-app user allowlist" FR-025 forbids by name, and the
  first step towards an authorization model (address lists, then groups, then
  roles). Principle VII exists to stop that.
- A rule by app ignores where the email came from. Any connector with a
  verified `@acme.com` address would pass, including a generic OpenID Connect
  provider the operator trusts less. A rule by connector lets the operator
  trust a domain on Google, where the company owns it, and not on GitHub, where
  anyone can verify any address they control.
- The app already receives the `email` claim and can refuse the person itself.
  001 says that decision belongs to the app.
- Having both doubles what the operator must understand (two places, plus which
  one wins), makes a refusal harder to debug, and adds nothing a connector made
  for the app cannot already do.

## Clarifications

### Decisions taken from the task (2026-10-14) and recorded here (2026-10-15)

The task offered a recommendation for each open question. Each is adopted, and
each is cheap to reverse before the plan is built.

- Q: Which emails may match? → A: Only an email the provider asserted as
  **verified** for this sign-in. While a rule applies, an unverified or missing
  email is refused. Otherwise a generic OpenID Connect provider, or GitHub with
  an unverified address, gets round the rule.
- Q: Do subdomains match? → A: No. `a.acme.com` matches only if it is listed.
  Wildcards are not accepted; each domain is listed on its own.
- Q: How do the instance list and a connector list combine? → A: A connector's
  own list, when it has one, **replaces** the instance list for that
  connector. An empty connector list means "use the instance list". There is
  no per-connector "anyone" choice: an operator who wants one connector open
  next to restricted ones sets lists on the restricted connectors and leaves
  the instance list empty.
- Q: Allow only, or deny too? → A: Allow only, by domain. A deny list of
  addresses or domains is out of scope: a list of people is the start of the
  user allowlist FR-025 forbids, and blocking one person is already done by
  disabling them after their first sign-in.
- Q: What happens to the sessions and tokens of people a tightened rule now
  refuses? → A: Nothing is revoked at the moment the rule is saved. Each
  session and each refresh is checked against the current rule the next time
  it is used, and refused if the rule no longer admits it. Access tokens
  already issued run out at their normal lifetime of one hour. See FR-012 to
  FR-014 for why this differs from disabling a user.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Only my company's Google accounts get in (Priority: P1)

An operator has a Google connector for their company. They open it on the
Connectors screen and enter `acme.com` in its allowed email domains. From the
next sign-in on, a person signing in through that connector with a verified
`@acme.com` Google account gets in as before. A person with a Gmail account, or
any other domain, is sent back to the sign-in page with a message that says the
account cannot be used here, and no Umbral user is created for them.

**Why this priority**: This is the feature. It alone serves the request.

**Independent Test**: Set `acme.com` on one connector, sign in through it with
a verified `@acme.com` identity and with a verified `@gmail.com` identity.
The first gets a session; the second gets the refusal page, no user row, and a
security event.

**Acceptance Scenarios**:

1. **Given** a connector limited to `acme.com`, **When** a person signs in with
   a verified `ann@acme.com`, **Then** the sign-in completes as before.
2. **Given** that connector, **When** a person signs in with a verified
   `bob@gmail.com`, **Then** they see the refusal page, no user is created or
   linked, and a security event records the refusal with the connector, the
   reason and the domain `gmail.com`.
3. **Given** that connector, **When** a person signs in with `carol@acme.com`
   that the provider does not assert as verified, or with no email at all,
   **Then** they are refused the same way.
4. **Given** that connector, **When** a person signs in with a verified
   `dave@eu.acme.com` or `ERIN@ACME.COM`, **Then** the first is refused (a
   subdomain is not listed) and the second gets in (case does not matter).
5. **Given** another connector with no list and no instance list, **When** a
   person signs in through it with any email, **Then** it behaves exactly as
   before this feature.
6. **Given** the refusal page, **When** the person reads it, **Then** it names
   no domain and does not say whether the email was unverified or from another
   domain. It suggests another account or another option, and contacting the
   person who runs Umbral.

---

### User Story 2 - One rule for the whole instance (Priority: P2)

An operator runs Umbral for their company only. Rather than set the same domain
on every connector, they set `acme.com` once on the Connectors screen's
instance setting. Every connector without a list of its own now admits only
verified `@acme.com` emails. A connector with its own list keeps its own list.

**Why this priority**: A convenience over Story 1 for the common "this whole
instance is for one company" case, and the setting the removal warning
protects.

**Independent Test**: Set the instance list to `acme.com`, leave connector A
without a list and set connector B to `example.org`. Sign in with verified
`@acme.com` and `@example.org` identities through each.

**Acceptance Scenarios**:

1. **Given** an instance list `acme.com` and connector A with no list, **When**
   a person signs in through A with a verified `@acme.com` email, **Then** they
   get in; with any other domain, they are refused.
2. **Given** the same instance list and connector B limited to `example.org`,
   **When** a person signs in through B with a verified `@example.org` email,
   **Then** they get in; with a verified `@acme.com` email, they are refused,
   because B's list replaces the instance list.
3. **Given** an instance list `acme.com`, **When** the operator empties it and
   saves, **Then** before anything changes they see a warning naming every
   enabled or disabled connector that has no list of its own and would let
   anyone in, and they confirm or cancel.
4. **Given** no connector without a list of its own, **When** the operator
   empties the instance list, **Then** it saves without a warning.
5. **Given** the instance setting, **When** the operator saves it, **Then** it
   takes effect at the next sign-in without a restart, and a security event
   records the change.

---

### User Story 3 - Tightening a rule locks out people already signed in (Priority: P2)

An operator restricts a connector that has been open for months. People from
other domains already have users, sessions and refresh tokens. They must not
keep using Umbral through that connector.

**Why this priority**: Without it a rule protects only against strangers who
have not signed in yet, which is not what an operator means by "only
`@acme.com` gets in".

**Independent Test**: Sign in through an open connector with a verified
`@gmail.com` identity and obtain a refresh token at an app. Limit the connector
to `acme.com`. Ask the app to authorize again with the existing session, and
redeem the refresh token.

**Acceptance Scenarios**:

1. **Given** a person with a session through a connector that has just been
   limited to a domain they are not in, **When** any app asks Umbral to
   authorize them, **Then** the session is ended and they reach the sign-in
   page as if they had never signed in; a security event records the refusal.
2. **Given** the same person, **When** an app redeems a refresh token issued
   through that connector, **Then** the refresh is refused and a security
   event records it.
3. **Given** the same person, **When** they sign in again through that
   connector, **Then** they see the refusal page.
4. **Given** the same person, **When** they sign in through another connector
   whose rule admits them, **Then** they get in, as the same user if the
   linking rule (001 FR-012) links them.
5. **Given** the same person's user record, **When** the operator looks at the
   Users screen, **Then** the user is still there, enabled, unchanged. A rule
   does not delete or disable anyone.

---

### User Story 4 - Limit one app with a connector made for it (Priority: P3)

An operator has an open GitHub connector and an open Google connector offered
to every app. One app, the company wiki, must admit only `@acme.com` people.
The form's note tells them how: they create a second Google connector, "Google
(Acme)", limited to `acme.com`, and set the wiki to offer only that connector.
If they instead set the wiki to offer "Google (Acme)" *and* the open GitHub
connector, the Apps screen warns them that GitHub would let other people in.

**Why this priority**: It is how the per-app need is met without a per-app
rule, so it must be discoverable. It needs no new mechanism, only the note and
the warning.

**Independent Test**: Follow the recipe in the docs alone, then try to reach
the wiki through an existing session from the open GitHub connector.

**Acceptance Scenarios**:

1. **Given** the domain field on the connector form, **When** the operator
   reads its help, **Then** it says: "To limit one app, create a connector for
   it with the domain rule and offer only that connector to the app."
2. **Given** the wiki offers only "Google (Acme)", **When** a person already
   signed in through the open GitHub connector opens the wiki, **Then** they
   get the wiki's sign-in page with "Google (Acme)" as the only button (003
   FR-008), and a non-`@acme.com` account is refused there.
3. **Given** an app that offers at least one connector with a rule and at least
   one connector without, **When** the operator views the Apps screen or saves
   that app's connector choice, **Then** a warning names the connectors with no
   rule and says they let anyone in. The choice is saved; the warning does not
   block.
4. **Given** an instance list is set, **When** the operator views the Apps
   screen, **Then** no app shows this warning, because every connector then has
   a rule.

---

### User Story 5 - Google shows only company accounts in its chooser (Priority: P3)

A person with both a personal and a work Google account signs in through a
Google connector limited to `acme.com`. Google's account chooser shows the work
account first, so they are less likely to pick the personal one and be refused.

**Why this priority**: A comfort. The server-side check is the rule; this only
saves a refusal.

**Independent Test**: Start a sign-in through a Google connector limited to
exactly one domain and inspect the redirect to Google.

**Acceptance Scenarios**:

1. **Given** a Google connector whose rule is exactly one domain, own or from
   the instance, **When** a sign-in starts, **Then** the request to Google
   carries that domain as the hosted-domain hint.
2. **Given** a Google connector whose rule has two or more domains, or none,
   **When** a sign-in starts, **Then** no hint is sent.
3. **Given** a connector of any other kind, **When** a sign-in starts, **Then**
   no hint is sent.
4. **Given** the hint was sent, **When** Google returns an account outside the
   domain anyway, **Then** the server-side check refuses it.

### Edge Cases

- **Email with more than one `@`, or none**: the domain is the part after the
  last `@`. An email with nothing after it, or no `@`, counts as no email and is
  refused while a rule applies.
- **Case and internationalised domains**: comparison ignores case, and a
  domain written in Unicode matches its ASCII (`xn--`) form both in the list
  and in the email.
- **Trailing dot or leading `@` in what the operator types**: removed before
  saving. `@acme.com` and `acme.com.` are saved as `acme.com`.
- **The rule changes between the redirect to the provider and the return**:
  the rule in force at the return applies.
- **A provider sends several addresses**: the one email the connector already
  takes from the provider for this sign-in (for GitHub, the primary verified
  address) is the one checked. No other address is tried.
- **A user linked through two connectors**: each sign-in is judged by the rule
  of the connector used for it, against the email that provider asserted for
  it, never against the email stored on the user or asserted by another
  provider. The same person can pass one connector and fail another.
- **A generic OpenID Connect provider that never asserts `email_verified`**:
  everyone is refused through it while a rule applies. The walkthrough says so.
- **Disabled connector**: the rule is kept and applies again when it is
  enabled.
- **A refused person who is also a disabled user**: the domain refusal comes
  first, so the page is the same for both and reveals nothing more.
- **Sessions and refresh tokens from before this feature**: they do not record
  the email that was checked or, for refresh tokens, the connector. While a
  rule applies to the connector (sessions) or any rule is set at all (refresh
  tokens), they are not honoured, and the person or app signs in again. With
  no rule set, they work as before.
- **Deleting a connector**: its list goes with it.

## Requirements *(mandatory)*

### Functional Requirements

#### Scope and governance

- **FR-001**: The rule MUST be a property of a connector or of the instance and
  MUST NOT be settable per app. It MUST be a list of domains only: no list of
  addresses, allowed or denied, no groups, no roles.
- **FR-002**: The same change set MUST amend 001 FR-025 to state that an
  operator may limit, per connector or per instance, which verified email
  domains can sign in, and that this decides who can be a user, not what a user
  may do in an app. It MUST add a clarification to constitution Principle VII
  saying the same, bump the constitution MINOR (2.0.0 → 2.1.0) with the
  rationale, and update the sync impact report.

#### The rule

- **FR-003**: The instance MUST have a list of allowed email domains. Empty,
  the default, means no instance rule.
- **FR-004**: Each connector MUST have a list of allowed email domains. Empty,
  the default, means the connector uses the instance list.
- **FR-005**: The rule that applies to a sign-in through a connector MUST be the
  connector's own list when it is not empty, otherwise the instance list when
  it is not empty, otherwise no rule. With no rule, sign-in MUST behave exactly
  as before this feature.
- **FR-006**: A sign-in MUST pass a rule only if the provider asserted, for this
  sign-in, an email that it asserted as verified, and the part of that email
  after its last `@` equals one listed domain, ignoring case and comparing
  internationalised domains in one form. A subdomain MUST NOT match a domain it
  belongs to unless it is listed itself.
- **FR-007**: When the operator saves a list, Umbral MUST accept domains
  separated by commas, spaces or new lines, remove a leading `@` and a trailing
  dot, lower-case them, drop duplicates, and refuse anything that is not a
  domain name of at least two labels. A refusal MUST name the value refused and
  say what to enter instead; for a wildcard it MUST say to list each domain
  instead. Nothing is saved when any entry is refused.

#### Enforcement

- **FR-008**: The rule MUST be checked when the person returns from the
  provider, before any user is created, linked or updated. A refused sign-in
  MUST leave no user, no linked account and no change to an existing user.
- **FR-009**: The rule checked MUST be that of the connector used for this
  sign-in, and the email checked MUST be the one the provider asserted for this
  sign-in.
- **FR-010**: A refused person MUST be returned to the app's sign-in page with
  one message, the same for a domain not listed, an unverified email and no
  email. It MUST NOT name a domain or the reason. It MUST say to try another
  account or option, or to contact whoever runs this Umbral.
- **FR-011**: Every refusal MUST be recorded in the security log as a failed
  sign-in, with the connector, the reason (domain not allowed, email not
  verified, no email), whether the connector's own list or the instance list
  applied, the stage (return from the provider, authorization with an existing
  session, refresh), and the email's domain. The full address MUST NOT be
  recorded.
- **FR-012**: A session MUST be honoured for an app only while the current rule
  of the connector that established it admits the email checked when it was
  established. Otherwise Umbral MUST end the session, record the refusal, and
  treat the request as having no session.
- **FR-013**: A refresh MUST be honoured only while the current rule of the
  connector through which the authorization was granted admits the email
  checked at that sign-in. Otherwise it MUST be refused and recorded. A refresh
  token that does not say which connector it came through MUST be refused while
  any rule is set on the instance or on any connector.
- **FR-014**: Saving a rule MUST NOT revoke, disable or delete anything. Access
  tokens already issued MUST stay valid until their normal lifetime (one hour)
  ends. (Disabling a user revokes at once, 001 FR-022, because it targets one
  known person; a rule targets an unknown set, judged one sign-in at a time,
  and FR-012 and FR-013 close the long-lived paths at their next use.)

#### Google hint

- **FR-015**: When a Google connector's rule is exactly one domain, the sign-in
  request to Google MUST carry that domain as the hosted-domain hint. With any
  other number of domains, and for every other kind of connector, no hint is
  sent. The hint MUST NOT replace FR-006.

#### Admin

- **FR-016**: The Connectors screen MUST show and edit the instance list. The
  admin area MUST stay three screens.
- **FR-017**: The connector add and edit forms MUST have a field for the
  connector's list. Its help MUST say that empty means the instance list (and
  what that list is, or that there is none), and MUST carry the note: "To limit
  one app, create a connector for it with the domain rule and offer only that
  connector to the app."
- **FR-018**: Each connector on the Connectors screen MUST show which rule
  applies to it: its own list, the instance list, or "anyone".
- **FR-019**: Saving an empty instance list when it was not empty MUST first
  show a warning naming every connector with no list of its own, which would
  then let anyone in, and save only after the operator confirms. With no such
  connector, it MUST save without a warning.
- **FR-020**: The Apps screen MUST warn, on each app that offers at least one
  enabled connector with a rule and at least one enabled connector without,
  naming the connectors without a rule. Saving such a choice MUST succeed and
  show the same warning. The warning MUST NOT block anything.
- **FR-021**: Every change to the instance list or a connector's list MUST be
  recorded in the security log with the connector (if any) and the domain
  count, and take effect at the next sign-in without a restart.
- **FR-022**: The lists MUST be stored in the data directory with the rest of
  the instance's state. No configuration file is involved (004).

#### Documentation and tests

- **FR-023**: The README MUST describe allowed email domains: the two levels,
  which one applies, verified-only matching, no subdomains, what happens to
  people already signed in, and how to limit one app.
- **FR-024**: Each connector walkthrough under `docs/connectors/` MUST have a
  section on allowed email domains saying how far that provider's verified
  email can be trusted for a domain rule, and repeating the one-app recipe. The
  Google walkthrough MUST mention the hint. The OpenID Connect walkthrough MUST
  say that a provider that does not assert `email_verified` refuses everyone
  while a rule applies.
- **FR-025**: Every behaviour here MUST be testable without real provider
  credentials.

### Key Entities

No entity is added; Principle I's three concepts stand.

- **Connector**: gains its list of allowed email domains, empty by default.
- **Instance setting "allowed email domains"**: one list for the instance, empty
  by default. A setting of the instance, like its secret store, not a new
  concept.
- **Session** (not user-facing): carries the email checked when it was
  established, so FR-012 can re-check it.
- **Authorization** (not user-facing): carries the connector it was granted
  through and the email checked, so FR-013 can re-check it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With a rule set, every sign-in in the test matrix with an email
  outside the list, an unverified email or no email is refused, and none of
  them creates, links or updates a user.
- **SC-002**: An operator limits a connector to one domain from the Connectors
  screen in under two minutes, with no restart, and the next sign-in obeys it.
- **SC-003**: After a rule is tightened, a person it now refuses gets no new
  token through that connector: their next authorization or refresh is
  refused, and the last access token they hold expires within one hour.
- **SC-004**: No text a refused person can see contains a listed domain or the
  reason for the refusal.
- **SC-005**: Every refusal produces exactly one security event, naming the
  connector, the reason and the stage.
- **SC-006**: An instance with no list set behaves exactly as before: the
  existing test suite passes unchanged.
- **SC-007**: A reader following the docs alone limits one app to one domain
  without a per-app setting.

## Assumptions

- The provider adapters already report, for each sign-in, one email and whether
  the provider asserted it as verified (001 FR-012 relies on this). Nothing in
  this feature changes how that is obtained.
- Google, GitHub and Discord set their verified flag only after the person
  proves control of the inbox. A generic OpenID Connect provider is trusted to
  mean the same by `email_verified`, as 002 already assumes for linking.
- The operator account is not a connector user and is never subject to a rule.
- Access tokens keep the one-hour lifetime they have today; this feature does
  not change any token lifetime.
- Lists are short (a handful of domains). No limit is set beyond what the form
  accepts comfortably.
