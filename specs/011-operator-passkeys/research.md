# Research: Operator passkeys

## R1. Reuse of the WebAuthn verifier

**Decision**: `WebAuthn.Verifier` stops naming the user `Passkeys.Passkey` type.
`RegistrationOptions` takes the credentials to exclude as `(credentialId,
transports)` pairs, `VerifyRegistration` takes the uniqueness check as a
function, and `VerifyAssertion` takes the stored public key, counter and user
handle. The connector's three call sites map their `Passkey` records to those.

**Rationale**: The task says reuse, don't copy. These are the only three places
the verifier knows whose passkey it is; the RP ID, origin, challenge size,
timeout and error mapping stay in one place for both kinds.

**Alternatives considered**: A second verifier for the operator (a copy of the
library calls, which the task forbids); an interface over both passkey records
(one more abstraction for two callers).

## R2. Where ceremonies live

**Decision**: Operator ceremonies use the connector's `passkey_ceremonies` table
and `Passkeys.startCeremony` / `consumeCeremony`, with three new purposes:
`operator-signin`, `operator-add` (the passkey name in `Nickname`) and
`operator-confirm` (a re-check). Every handler filters by its own purposes, so
an operator ceremony can never complete a user flow, or the reverse.

**Rationale**: Single-use by a `DELETE … RETURNING`, the expiry and the pruning
already exist with the connector's lifetime (FR-016). A ceremony is transient
state, not a passkey, so sharing the table does not mix operator and user
passkeys (Principle I).

**Alternatives considered**: An `operator_ceremonies` table (a copy of tested
code for no gain).

## R3. Telling an operator passkey from a user's

**Decision**: Operator passkeys are registered with one user handle,
`operator-` followed by 128 random bits, minted on the first registration and
kept in `settings` (`operator_user_handle`). The operator sign-in accepts only a
credential id found in `operator_passkeys` whose answer carries that handle.

**Rationale**: Users' ids are opaque subjects that never start with
`operator-`, so the connector's sign-in finds no user for that handle and
refuses (FR-015). The authenticator groups operator passkeys under one entry.

## R4. Re-check carried by the action

**Decision**: Every Credential-screen action reads either `current` (the
password, refused while password sign-in is off) or `ceremony` plus
`credential` (the answer to an `operator-confirm` ceremony started from that
form a moment before). Turning password sign-in off reads only the ceremony.

**Rationale**: It is what replacing the password already does: the proof is in
the same request as the change. No window of elevated rights exists to steal.

**Alternatives considered**: A "sudo" window after one re-check (a stolen
cookie inside the window acts freely); a separate confirmation token (one more
thing to store and expire).

## R5. One place refuses the password while it is off

**Decision**: `AdminAuth.verify` returns false while password sign-in is off,
before hashing anything. Sign-in, re-check and replacement all call it.

**Rationale**: FR-008 then holds for every present and future caller; the code
review looks hardest at "no path signs in with a password while off".

## R6. Rate limits

**Decision**: The admin limit (`RateLimits.AdminAuth`, 10 per 10 minutes per
address) on every request that checks a password or a passkey answer: password
sign-in, passkey sign-in, add options (it carries the re-check), remove, the
switch, and replacing the password. The connector's ceremony limit
(`RateLimits.SignIn`) on the requests that check nothing guessable: the sign-in
and confirm options (they only start a ceremony) and the add completion (it
verifies a registration inside an admin session against a single-use ceremony
that an admin-limited request started).

**Rationale**: Starting a ceremony checks nothing, and putting it under the
admin limit would halve the passkey sign-ins it allows. The add completion was
first under the admin limit; the design review walk (2026-10-27) showed a first
setup (sign in, three passkeys, password sign-in off) spending 9 of the 10
permits, one click from the "Too many attempts" page. Remove and the switch
still spend two (the confirm redraw and the confirmed post).

## R7. Sessions after a change

**Decision**: Removing a passkey and turning password sign-in off rotate the
session stamp (every other admin session ends) and re-sign the current one, as
replacing the password does. `reset-credential` already rotates it.

## R8. Races

**Decision**: The rules are single SQL statements. Remove deletes only when
password sign-in is on or another passkey remains; switching off writes only
when a passkey exists; adding inserts only while fewer than 10 exist.

**Rationale**: A check made a moment before the write could be outrun by a
second request; the connector's `removeOwnIfNotLast` does the same.

## R9. JavaScript off

**Decision**: The passkey button on `/admin/signin` is rendered `hidden` and
`passkeys.js` reveals it; while password sign-in is off a `<noscript>` line says
a passkey needs JavaScript. The page never shows a control that cannot work.
