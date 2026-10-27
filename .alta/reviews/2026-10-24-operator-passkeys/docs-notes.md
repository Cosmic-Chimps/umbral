# Docs notes: spec 011, T021

Sources: SPEC = specs/011-operator-passkeys/operator-passkeys.spec.md, ROUTES = specs/011-operator-passkeys/contracts/routes.md, DM = specs/011-operator-passkeys/data-model.md, TEXTS = src/Umbral/Views/Texts.fs.
The "pending code" marks were checked by the parent against `src/Umbral/Program.fs` `resetCredential` (lines ~570-685) and `main` on 2026-10-27, and replaced with code references.

## README.md

### Quick start step 3
- "It is a password" / field is the **operator credential**: TEXTS CredentialLabel = "Operator credential" (setup field, unchanged); SPEC "First run is unchanged".
- Under **Password** the change form asks for current and new twice: existing text, TEXTS PasswordCardTitle = "Password".
- Add a passkey under **Credential**, sign in with **Sign in with a passkey**: TEXTS NavCredential, OperatorPasskeyAddButton; "Sign in with a passkey" = TEXTS SignInWithPasskey (the sign-in button text: re-check the operator sign-in page reuses it); SPEC FR-005.
- Turn password sign-in off once you have a passkey: SPEC FR-007.
- Claim from a script: "prints a password", "replaces the password": `src/Umbral/Program.fs:644` ("The operator password has been replaced."), `:671` ("New password").

### New `### Passkeys for the admin area` (under `## Passkeys`)
- Needs no connector, never on Users, never signs anyone in to an app: SPEC Decisions "Not a new concept", FR-015, Assumptions.
- Three parts **Passkeys**, **Password sign-in**, **Password**: TEXTS OperatorPasskeysTitle, PasswordSignInTitle, PasswordCardTitle.
- Optional name, **Add a passkey**, up to 10: TEXTS OperatorPasskeyNameLabel, OperatorPasskeyAddButton; SPEC FR-001, FR-002.
- Listed with added, last used, **Remove**: TEXTS OperatorPasskeyAddedOn, OperatorPasskeyLastUsedOn, OperatorPasskeyRemoveButton; SPEC FR-003.
- **On** until turned off; **Turn password sign-in off**; **Turn password sign-in on**: TEXTS PasswordSignInOn, TurnPasswordSignInOff, TurnPasswordSignInOn.
- Off needs a passkey and a passkey confirmation: SPEC FR-007, Clarifications Q2; ROUTES "The switch takes only a passkey ceremony in both directions".
- While off: no password accepted anywhere, sign-in shows only the passkey, Password part hidden: SPEC FR-008, FR-009; TEXTS SignInLeadPasskeyOnly, PasswordSignInOffSentence.
- Password kept, comes back unchanged: SPEC FR-010; TEXTS PasswordSignInOffSentence.
- Sign in at `/admin/signin`, no name typed, password form beside it while on: SPEC FR-005; ROUTES sign-in table.
- Without JavaScript: password form, or says a passkey needs JavaScript while off; adding needs JavaScript: SPEC FR-019; TEXTS OperatorPasskeyNeedsJavaScriptOnly; ROUTES "Script hooks".
- Adding and removing ask for the password (while on) or a passkey; the switch asks for a passkey: SPEC FR-004, Clarifications Q1, Q2; ROUTES re-check fields.
- Removing a passkey and switching off sign out other admin sessions: SPEC FR-012, Clarifications Q4; TEXTS notices "operator-passkey-removed", "operator-password-off".
- Cannot remove last passkey while off; "turn password sign-in on first, or add another passkey": SPEC FR-011; TEXTS notice "operator-passkey-last".
- Bound to `UMBRAL__PUBLIC_ORIGIN`: SPEC FR-016 (RP ID = issuer host, origin check). Not checked in code by me: parent to confirm.
- Security log event names and details: `src/Umbral/Infrastructure/SecurityEvents.fs:79-81` (the three types); DM "Security events" for details `on`/`off`/`offline_reset`, `operator_signed_in` detail `passkey`, `passkey_refused;reason=` from `src/Umbral/Domains/Passkeys.fs:164-167`. `offline_reset` for the switch: `src/Umbral/Program.fs` `resetCredential` (switch on and `offline_reset` event, `removeAll` with `--forget-passkeys`) and `main` (`--forget-passkeys` argument); tests `tests/Umbral.Tests/SubcommandTests.fs`.
- No challenge, credential id, key or password logged: SPEC FR-018; DM.

### Your data: back it up
- `umbral.db` also holds the operator's passkeys (public keys) and whether password sign-in is on: DM `operator_passkeys` table and settings key `operator_password_signin`; `src/Umbral/Infrastructure/Database.fs:260`.

### Upgrading
- Schema version 8: `src/Umbral/Infrastructure/Database.fs:287` (`SchemaVersion = 8`).
- Upgraded instance has no passkey and password sign-in on: DM "Upgrade"; SPEC FR-021.

### Forgotten operator credential
- Command forms, `--forget-passkeys`: SPEC FR-013, FR-014; ROUTES "Command". `src/Umbral/Program.fs` `resetCredential` (switch on and `offline_reset` event, `removeAll` with `--forget-passkeys`) and `main` (`--forget-passkeys` argument); tests `tests/Umbral.Tests/SubcommandTests.fs`
- Replaces password, prints once, turns password sign-in on, signs out every admin session, keeps passkeys: SPEC FR-013. `src/Umbral/Program.fs` `resetCredential` (switch on and `offline_reset` event, `removeAll` with `--forget-passkeys`) and `main` (`--forget-passkeys` argument); tests `tests/Umbral.Tests/SubcommandTests.fs`
- Output box, wording verbatim: from the parent's brief. `src/Umbral/Program.fs:644-678` (headline, `New password`, `Password sign-in is on.`, passkey line built at `:649-658`, closing lines)
- Variants `2 operator passkeys were removed.`, `1 operator passkey is kept.`, `1 operator passkey was removed.`, no line with none: from the parent's brief. `src/Umbral/Program.fs:644-678` (headline, `New password`, `Password sign-in is on.`, passkey line built at `:649-658`, closing lines)
- When to use (forgot password while off, lost every device; stolen device -> flag): SPEC User Story 4.
- "Add a passkey again under **Credential** afterwards": my addition, follows from the passkeys being removed.
- No "forgot your credential" page: existing text kept.

## CHANGELOG.md (Unreleased)

- Added: operator passkeys, up to 10, **Credential**, **Sign in with a passkey**, no connector needed: SPEC FR-001, FR-002, FR-005, FR-015.
- Added: password sign-in off once a passkey exists, stored password comes back: SPEC FR-007, FR-010.
- Added: `reset-credential --forget-passkeys`: SPEC FR-014. `src/Umbral/Program.fs` `resetCredential` (switch on and `offline_reset` event, `removeAll` with `--forget-passkeys`) and `main` (`--forget-passkeys` argument); tests `tests/Umbral.Tests/SubcommandTests.fs`
- Changed: Credential page three parts: TEXTS OperatorPasskeysTitle, PasswordSignInTitle, PasswordCardTitle.
- Changed: `reset-credential` turns password sign-in on, keeps passkeys, output says "password": SPEC FR-013 `src/Umbral/Program.fs:644-678` (headline, `New password`, `Password sign-in is on.`, passkey line built at `:649-658`, closing lines). Signing out every admin session is not listed as new: it already did.
- Upgrade: schema version 8, older image refuses it, backup first: `Database.fs:287`, `Database.fs:471-474` (refusal message), existing README Upgrading text; nothing changes until a passkey is added: SPEC FR-021.

## Not verified

- That the operator sign-in page's passkey button reads exactly "Sign in with a passkey" (TEXTS SignInWithPasskey is the connector's button; SPEC Verbatim Constraints and FR-005 give the words).
- Whether `reset-credential` on an unclaimed instance prints "password" (README "claim from a script" says so).
- The README's passkey-connector JavaScript paragraph ("Removing a passkey and every operator action work without it") was left as is: it is about the Users screen.
