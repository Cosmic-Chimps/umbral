# Documentation review: spec 011, passkeys for the operator

Branch: `alta/earthy-orinoco-8bkw`, range `853bf39..8b721d8` (doc commit
`2e6bd2f`), on `main` at `853bf39`.
Reviewed 2026-10-27 by the documentation reviewer session, per section 6 of
`docs/audit-2026-10.md`, from the diff, `docs-notes.md`, the routes contract
and the code on the branch.

## Verdict

**Clear to merge.** No blocking item. Three nits, none needs a re-check.

## What was checked

Every quoted UI string against `Views/Texts.fs`, and every behavioural
statement against the handler or `Program.fs`:

- **Quick start step 3 and the claim-from-a-script paragraph.** The setup
  field is still **Operator credential** (`Texts.fs:900`); **Password** is
  `PasswordCardTitle` (`:942`); "prints a password" holds for an unclaimed
  instance too: `resetCredential` prints "New password" under either headline
  (`Program.fs:644-678`).
- **"Passkeys for the admin area".** **Passkeys**, **Password sign-in**,
  **Password** (`:915, :932, :942`); **Add a passkey** (`:921`); "up to 10"
  (`OperatorPasskeys.fs:36`, and the notice at `:806`); "Added …", "Last used
  …", **Remove** (`:922-925`); **On**, **Turn password sign-in off**, **Turn
  password sign-in on** (`:933, :937-938`). Off needs a passkey: the refusal
  notice `operator-password-off-needs-passkey` (`:802`) and the handler
  (`OperatorPasswordSignIn.fs:29-32`). While off, the Password part is not
  rendered (`Credential.fs:308-309`, `if passwordOn then passwordCard`), the
  sign-in page drops the password form (`SignIn.fs:44-55`), and the
  no-JavaScript line is `OperatorPasskeyNeedsJavaScriptOnly` then and
  `OperatorPasskeyNeedsJavaScript` otherwise (`SignIn.fs:61`). Removing a
  passkey and switching off end other sessions: the notices at `:799-800` say
  so. The last-passkey rule is the notice at `:803-804`, in the README's words.
  **Sign in with a passkey** on `/admin/signin` is `SignInWithPasskey`
  (`SignIn.fs:29`, `Texts.fs:1254`), the one string the notes had not
  confirmed. The RP ID is the host of the public origin (`WebAuthn.fs:102`),
  shared with people's passkeys.
- **The security log.** `operator_passkey_added`, `operator_passkey_removed`,
  `operator_password_signin_changed` (`SecurityEvents.fs:79-81`); the switch
  writes `on` or `off` on success (`OperatorPasswordSignIn.fs:71,75`) and
  `offline_reset` from the command (`Program.fs`, the switch event);
  `operator_signed_in` with `passkey` (`OperatorPasskeys.fs:273`); a refused
  sign-in is `operator_sign_in_failed` with `Rules.refusalDetail`
  (`OperatorPasskeys.fs:206`), which starts `passkey_refused;reason=`.
- **Backup table and Upgrading.** `SchemaVersion = 8` (`Database.fs:287`);
  the `operator_passkeys` table and the `operator_password_signin` setting live
  in `umbral.db`.
- **Forgotten operator credential.** The box matches `Program.fs:644-678` line
  for line, blank lines included: headline, `New password`, blank, `Password
  sign-in is on.`, the passkey line, blank, the two closing lines. The four
  passkey-line variants and "no line with none" are `:649-656`. The flag is
  `--forget-passkeys` (`:744`), and `removeAll` runs under it.
- **CHANGELOG.** Added, Changed and Upgrade under `## Unreleased`, in the
  file's order (Added, Changed, Fixed, Security, Upgrade); `changelog.sh
  released` against `main` is clean; the Upgrade wording follows 0.2.0 and the
  schema-7 entry.
- **Links.** `check-links.py`: 184 links in 14 files, all resolve, the new
  `#passkeys-for-the-admin-area` anchor included.

## Nits

1. **README:1208-1209, Upgrading:** "the operator has no passkey and password
   sign-in stays on until you add one under **Credential**". Adding a passkey
   does not turn password sign-in off; the operator does, separately. "…has no
   passkey, and password sign-in stays on until you turn it off" says what
   happens.
2. **README, "Bound to the address":** "the operator's belong to the address in
   `UMBRAL__PUBLIC_ORIGIN`. If you change it, they stop working." The RP ID is
   the host only (`WebAuthn.fs:102`), so a change of scheme or port keeps them.
   Audit W4 has T4 fixing the same sentence for people's passkeys; writing
   "the host name in `UMBRAL__PUBLIC_ORIGIN`" here now saves T4 a second edit.
3. **README, the security-log paragraph:** a refused attempt to change the
   switch is the same `operator_password_signin_changed` event with a failure
   outcome and the detail `no_passkey` or the ceremony's refusal reason
   (`OperatorPasswordSignIn.fs:31,62`). One clause ("a refused change is the
   same event with a failure outcome") would let an operator reading the log
   place it. Optional.

## docs-notes.md

Complete under section 6: every statement with its source (spec, routes,
Texts.fs or code line), the three items it could not verify listed as such,
which I have now checked, and the one addition of its own marked. Agreed on all
of it.

---

## Addendum, 2026-10-27, at `eb30cdc`

**Still clear.** All three nits taken in the README: "Bound to the host name"
with the scheme-and-port sentence; Upgrading says "until you turn it off under
**Credential**"; the security-log paragraph adds "a refused change is the same
event with a failure outcome". The people's-passkeys sentence (README:701) is
left for T4, as agreed.

The strings changed at `20a81c2` for the code and design reviews
(`operator-passkey-signin-failed` now "…Try again, or use your password." or
"…Try again."; the Password card's **New password** and **Confirm the new
password**; the sign-in page's **Password** field) are quoted nowhere in the
README or the walkthroughs; the README's "asks for the current one and the new
one twice" is a description and still holds. Link check: 184 links in 14 files,
all resolve.
