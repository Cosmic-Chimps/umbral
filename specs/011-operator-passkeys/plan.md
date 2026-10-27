# Implementation Plan: Operator passkeys

**Branch**: `alta/earthy-orinoco-8bkw` | **Date**: 2026-10-26 | **Spec**: [operator-passkeys.spec.md](operator-passkeys.spec.md)

## Summary

The operator credential gains passkeys and a switch for password sign-in. A new
table holds operator passkeys apart from users' (schema 8); the switch and the
operator's user handle are two `settings` rows. The connector's WebAuthn
verifier and ceremony store are reused: the verifier stops naming the user
passkey type, and the ceremony store gets three operator purposes. The Credential
screen gains the passkey list, add, remove and the switch, each re-checking the
current credential in the same request; `/admin/signin` gains the passkey
sign-in. `AdminAuth.verify` refuses every password while the switch is off, and
`reset-credential` turns it back on and, with `--forget-passkeys`, removes the
passkeys. Decisions are in [research.md](research.md); storage in
[data-model.md](data-model.md); routes in [contracts/routes.md](contracts/routes.md).

## Project Structure

```text
src/Umbral/
├── Infrastructure/
│   ├── Database.fs              # schema 8: operator_passkeys (schema + UpgradeToVersion8)
│   ├── Passkeys.fs              # three operator ceremony purposes
│   ├── OperatorPasskeys.fs      # NEW: the table, the switch, the user handle (before WebAuthn.fs)
│   ├── WebAuthn.fs              # verifier takes keys, not user Passkey records
│   ├── AdminAuth.fs             # verify refuses while off; rotate the stamp
│   └── SecurityEvents.fs        # three event types
├── Handlers/
│   ├── Passkeys/Ceremonies.fs   # call sites of the verifier; JSON helpers made public
│   └── Admin/
│       ├── Credential.fs        # the screen (list, add, switch, replace password)
│       ├── SignIn.fs            # the page: passkey button, password form while on
│       ├── OperatorPasskeys.fs  # NEW: the JSON ceremonies and the re-check reader (after Ceremonies.fs)
│       ├── OperatorPasswordSignIn.fs  # NEW: the switch (like ConnectorToggle.fs)
│       └── OperatorPasskeyRemove.fs   # NEW: remove (like ConnectorDelete.fs)
├── Views/Texts.fs               # strings and notice codes (designer's wording)
├── Assets/passkeys.js           # the four operator hooks
├── Assets/umbral.input.css, umbral.css  # only if a class is new
├── Umbral.fsproj                # two Compile entries
└── Program.fs                   # routes; reset-credential [--forget-passkeys]
tests/Umbral.Tests/
├── OperatorPasskeyTests.fs, OperatorPasswordSwitchTests.fs, OperatorPasskeyRemoveTests.fs  # NEW
├── Infrastructure/OperatorFlows.fs  # NEW: helpers (add, sign in, confirm)
└── existing: CredentialTests, SubcommandTests, DatabaseTests, CsrfTests, PasskeyCeremonyTests
README.md, CHANGELOG.md
```

**Structure Decision**: Operator storage gets its own infrastructure module and
its own handler module, so the operator credential stays one concept apart from
users (Principle I) while the WebAuthn and ceremony code is shared.

## Constitution Check

| Principle | Assessment |
| --- | --- |
| I. Three concepts | PASS. Operator passkeys belong to the operator credential (3.0.1, Principle VII bullet); no new noun on any screen, nothing on Users. |
| II. One artifact, modest hardware | PASS. No dependency; one small table; idle memory re-measured by `IdleMemoryTests`. |
| III. Standards over hand-rolled code | PASS. fido2-net-lib verifies everything through the existing `WebAuthn.Verifier`; no signature code added. |
| IV. Secure by default | PASS. Single-use challenges, RP ID and origin from the public origin, counter checked, UV required, re-check in the action, rate limits, security-log events, stamp rotation. Password sign-in stays on until the operator proves a passkey. |
| VI. Documentation | PASS. README (Forgotten operator credential, backup, schema 8, security log, admin sign-in), CHANGELOG under Unreleased; docs review by `umbral-documentation`. |
| VII. Default answer is no | PASS. A passkey is a way to sign in, never a second step (no MFA); no operator accounts or roles. Clarified in 3.0.1. |

Re-checked after design: unchanged.

## Testing

Real endpoints with `SoftwareAuthenticator` (the connector's): add, sign in,
remove; a reused challenge, a wrong origin, a regressed counter, missing UV; the
switch refused with no passkey and off after a ceremony; the password refused
on every route while off; the last passkey refused while off; `reset-credential`
with and without `--forget-passkeys`; the rate limits; an operator passkey
refused at `/passkey/assertion` and a user passkey at the operator sign-in; no
operator passkey on the Users screen. `CsrfTests` walks the new POSTs.
