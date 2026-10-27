# Code review: spec 011, operator passkeys

Branch `alta/earthy-orinoco-8bkw`, `853bf39..966bbf7`. Reviewer: the `umbral` session, 2026-10-27. I read the diff against `specs/011-operator-passkeys/` (the spec, plan, research R1–R9 and the routes contract) and the constitution 3.0.1. Two independent read-only passes covered the WebAuthn ceremonies and, separately, the switch, password paths, sessions, recovery, rate limits and CSRF. I checked every finding below against the code myself.

## Verdict

**Not yet clear: two blocking items, five nits.** The design is sound, and most of it is done carefully: see "Checked and clean". Both blocking items are small, local fixes that make the code do what the spec already says.

## Blocking

### B1. A sign-in already in progress survives the change that should end it
The spec's edge case says "A passkey removed in one tab while another tab is signing in with it: the sign-in is refused." Two reviewers found independently that it isn't.
- **Passkey:** `Handlers/Admin/OperatorPasskeys.fs` (`verifyOperatorAnswer`, around 76–92) reads the passkey with `findByCredentialId`, verifies, then calls `OperatorPasskeys.recordUse` (`Infrastructure/OperatorPasskeys.fs`, around 157–170). That `UPDATE` ignores how many rows it changed. If a remove lands in between (it deletes the row and rotates the stamp with `endEverySession`), the `UPDATE` changes 0 rows and the sign-in still returns `Ok`.
- **Password:** the same window exists. `verify` reads the switch as on and hashes the password while a switch-off rotates the stamp.
- **Why the session survives either way:** `AdminAuth.signIn` (`Infrastructure/AdminAuth.fs`, around 252–260) reads the session stamp when it writes the cookie, not when the request began. So the cookie carries the **new** stamp and survives the rotation. A stolen device that signs in during the operator's Remove keeps a valid admin session.

**Fix:**
- Read the stamp **before** verifying, and sign in with that value (for example, `signInWithStamp ctx stamp`), so any rotation in between makes the new cookie invalid at the next request.
- Have `recordUse` return the rows changed, and refuse as `UnknownCredential` when it is 0.
- Add a test that removes the passkey (or switches off) between verify and sign-in, through a seam or the store directly, and checks that the resulting cookie is refused on the next admin request.

### B2. A confirmation is not tied to the action it was made for
FR-004: adding, removing or switching each requires "the answer of a passkey ceremony made for it". `recheck` (`Handlers/Admin/OperatorPasskeys.fs`, around 127) accepts any `OperatorConfirm` ceremony, and the ceremony row is created with no action. So a confirm started for "remove passkey X" also authorises "switch password sign-in off", "add a passkey" or "remove passkey Y".
- **Why the impact is small:** the answer is single-use, short-lived, behind antiforgery and the admin cookie, and only produced on the real origin.
- **Why it is blocking:** it is the spec's own requirement, and the fix is small.

**Fix:** store the action on the confirm ceremony when its options are made. Use `add`, `switch-off`, `switch-on` or `remove:<passkey id>`, in an existing column such as `Nickname` or `ReturnUrl`, or a new one. Each handler then accepts only a confirm made for its own action. Add one test per action that a confirm made for another action is refused, and is consumed.

## Nits (take them if small; no re-check needed for these)

- **N1. The counter can move backwards under concurrent uses.** `recordUse` sets `sign_count = @signCount` unconditionally, so two answers verified against the same stored value can store the lower one last. Use `sign_count = MAX(sign_count, @signCount)`. The user connector's `recordUse` has the same pattern from before this branch; fixing both in one place is welcome.
- **N2. `POST /admin/signin/passkey` doesn't check that setup is done.** Its options endpoint does (`isConfigured`), and the routes contract says all four sign-in routes refuse before first-run setup. It's harmless today, since there are no passkeys before setup, but not for a restored or edited database.
- **N3. A passkey can confirm its own removal.** US3 says "confirming with the password or *another* passkey". The UI only offers the button when other passkeys exist, but `OperatorPasskeyRemove.fs` doesn't check. B2's action binding (`remove:<id>`) is the natural place: refuse a confirm answered by the passkey being removed.
- **N4. `reset-credential --forget-passkeys` rotates the stamp before it removes the passkeys and turns password sign-in on.** `replaceCredential`, then `removeAll`, then `turnPasswordSignInOn`, as separate statements. A passkey sign-in landing between the rotation and `removeAll` keeps its session. Rotate the stamp last, or once more at the end.
- **N5. No test runs two requests at once.** "Exactly one succeeds" and "never off with no passkey" are tested one after the other. The SQL is right: single statements inside `BEGIN IMMEDIATE`, so SQLite serialises the writes. A concurrent test (two tasks against the store) would keep it right.

## Checked and clean

- **Challenges.** Each is consumed by one `DELETE … WHERE expires_utc > now RETURNING` before anything else, and then filtered by purpose, so a wrong-purpose id is burned too. Sign-in, confirm, add and the user connector each accept only their own purposes. Replay is tested.
- **Operator and user passkeys never cross.**
  - The operator sign-in reads only `operator_passkeys`, and checks the stored `operator-…` user handle with a fixed-time compare.
  - The user connector resolves the handle through `Users.findById`, which never finds the operator handle, and reads only `passkeys`.
  - Both directions are refused, logged and tested.
- **Origin and RP ID** come from the configured `PublicOrigin`, never from request headers, and are the same as the connector's. A wrong origin is tested.
- **Registration and assertion.**
  - User verification and resident key are both `Required`. Attestation is `None` and the algorithms are the library defaults. Options are stored server-side.
  - `credProps.rk=false` is refused, and the add checks `registered.UserId = handle`.
  - `excludeCredentials` lists the operator's passkeys. The limit of 10 is checked in the options and again inside the `INSERT`, and `credential_id` is `UNIQUE`.
- **Counter regression** is refused (`SignCountRegressed`) and logged, and that's tested.
- **The switch and the last passkey.**
  - Switching off is one `INSERT … SELECT … WHERE EXISTS (passkeys) … ON CONFLICT`.
  - Removing is one guarded `DELETE` inside `BEGIN IMMEDIATE`, so the invariant holds under concurrency.
  - Both switching off and on take a passkey ceremony only (`allowPassword=false`).
- **No password while off (FR-008).** `AdminAuth.verify` is called in three places (the sign-in POST, the re-check, replacing the password), and all three get false while off. Setup refuses once the instance is configured. There's no other password path. The Credential screen draws no password field while off.
- **Sessions.** Remove and switch-off end every session and sign the current browser in again; add and switch-on don't, as the spec says. Reset rotates the stamp, and every admin request checks it (`OnValidatePrincipal`). Session fixation doesn't apply: the cookie is fresh and stateless.
- **`reset-credential`.**
  - The schema is migrated first.
  - The password goes only to stdout, never to an event or log.
  - It turns the switch on and keeps the passkeys unless `--forget-passkeys`.
  - It still claims an unclaimed instance, and running it twice is safe.
- **Rate limits** are per address, IPv6 per /64, after trusted proxies.
  - `AdminAuth` covers everything that checks a password or a passkey answer.
  - The add completion on `SignIn` (research R6, `90d1cbb`) is safe. It sits behind `requireOperator` and needs a single-use `operator-add` ceremony, which only the admin-limited, re-checked add options request creates.
- **CSRF and auth.** Every new POST checks antiforgery first (`readValidated` and `withBody`). The CSRF walk reads routes off the running app. `requireOperator` guards all of `/admin/credential/*`, and only the passkey sign-in and its options are open.
- **The `WebAuthn.fs` refactor** changes nothing for the user connector: same key, counter, user id, exclude list and purposes.
- **`passkeys.js`** has no `innerHTML`, filters notices to known codes, redirects only to same-origin paths, puts assertions in hidden fields and clears the password field before submitting.
- **Schema 8.** The step runs inside `initializeSchema`'s transaction, with the version guard and `IF NOT EXISTS`. No switch row is added, so password sign-in stays on after the upgrade. The README backup table and the schema version are updated.
- **Security events.** Add, remove, switch and sign-in each log success and failure, with fixed words and no credential id, key, name or password.
- **Constitution 3.0.1** is a single clarifying bullet, the version line and the Sync Impact Report. MFA stays excluded, and nothing is added to or removed from the exclusion list.
- **Idle memory** (notes): the operator passkey run is 147.6 MiB on linux/arm64 against a 150 MiB ceiling. Watch CI's linux/amd64 figure on the first push; if it goes over, take it back to the design rather than raising the ceiling.

## The other reviews' open items, for whoever picks the branch up

The implementer's session ended before these arrived. Do them in the same round:
- **Design (`review.md`, uncommitted):** three `Texts.fs` nits.
  1. The Password card labels become "New password" and "Confirm the new password", as the card's own strings; setup is unchanged.
  2. The sign-in page's field label becomes "Password", as its own string; setup keeps "Operator credential".
  3. The failed passkey sign-in notice splits by state: "…Try again, or use your password." while password sign-in is on, and "…Try again." while it is off.

  Then message `umbral-designer` the commit, with captures `credential-with-passkeys` and `signin-both` at 1280 dark.
- **Documentation (`review-docs.md`, committed at `d708b2f`):** the README sentence on operator passkeys "bound to the address in `UMBRAL__PUBLIC_ORIGIN`" should say the **host name**, because the RP ID is the host only.

## Tests

A full `dotnet test` at `966bbf7`, run by the reviewer (macOS): **1770 passed, 0 failed, 0 skipped**, and `build-css.sh --check` is clean. That agrees with the implementer's 1770/1770. No test exercises B1's window or B2's cross-action confirm, which is why each fix needs one.

## What happens next

Fix B1 and B2 with tests, take the nits you can, and do the design and documentation items above. Commit this file, `review.md` and your answers in `notes.md`, then message the `umbral` session for the one re-check. No merge until all three reviews say clear. The maintainer merges.
