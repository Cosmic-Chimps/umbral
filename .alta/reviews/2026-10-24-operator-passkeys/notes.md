# Spec 011, operator passkeys: notes for review

- **Branch**: `alta/earthy-orinoco-8bkw` (worktree `~/.alta/worktrees/umbral/earthy-orinoco-8bkw`).
- **Range**: `853bf39..HEAD` (main at `853bf39`). The constitution PATCH is `3233ae7`; the design contract is cherry-picked as `5ee4e40` (same patch as `345deb9`).
- **Spec**: `specs/011-operator-passkeys/` (spec, plan, research, data model, `contracts/routes.md`, tasks).
- **Design contract**: `design/contracts/operator-passkeys-ui.md`, followed as written. Deviations are listed below.

## Captures

Taken 2026-10-27 from a Release build on `http://localhost:18080` with Chrome (Playwright) and Chrome's virtual authenticator. Every state was reached through the real screens and the real ceremonies: passkeys were added through the add form, a passkey sign-in ran from a second browser, password sign-in was turned off and on again with a passkey, and two passkeys were removed while it was off. Each state is at 1280 and 360, in dark and light, unless noted.

| State | What it shows |
| --- | --- |
| `credential-no-passkey` | Fresh instance: "No passkey yet.", the off button disabled with "Add a passkey first.", the Password card |
| `credential-with-passkeys` | Three rows (Laptop, Phone, YubiKey): two "Never used", one "Last used" |
| `add-passkey` | The add form with a name typed, the Current password field, **Add a passkey** and **Use a passkey instead** |
| `remove-confirm` | The first row expanded into its confirm block after a 422 (Current password, **Remove passkey**, **Use a passkey instead**, **Cancel**) |
| `focus-confirm` | 1280 dark only: keyboard focus in the confirm block's field |
| `switch-off-confirm` | The Password sign-in card's off confirm block, focus on **Confirm with a passkey** |
| `password-off` | Password sign-in off: badge Off, no Password card, no password field anywhere |
| `switch-on-confirm` | The on confirm block while off |
| `remove-last-refused` | One passkey left while off: Remove answered with the Warning "This is the only passkey, and password sign-in is off. Turn password sign-in on first, or add another passkey." |
| `signin-passkey-only` | `/admin/signin` while off: the passkey button alone, Primary |
| `signin-no-js` | 1280 only: while off, with JavaScript disabled, showing only the no-JavaScript line |
| `signin-both` | `/admin/signin` while on: the password form, "or", **Sign in with a passkey** (Secondary, Key icon) |
| `signin-no-js-password-on` | 1280 only, extra: while on, with JavaScript disabled, today's form and the no-JavaScript line ending "or sign in with your password" |

In every state the metrics show no horizontal overflow at 360. The page titles are "Credential \| Umbral" and "Operator sign-in \| Umbral".

## Words (Texts.fs)

These are new, as section 4 of the contract asked:

- Credential screen: `CredentialTitle`, `CredentialLead`, `OperatorPasskeysTitle`, `OperatorPasskeysLead`, `OperatorPasskeysEmpty`, `OperatorPasskeysLimit`, `OperatorPasskeyNameLabel`, `OperatorPasskeyNameHint`, `OperatorPasskeyAddButton`, `OperatorPasskeyAddedOn`, `OperatorPasskeyLastUsedOn`, `OperatorPasskeyNeverUsed`, `OperatorPasskeyRemoveButton`, `OperatorPasskeyRemoveQuestion`, `OperatorPasskeyRemoveConfirmButton`.
- Re-check and confirm blocks: `UsePasskeyInstead`, `ConfirmWithPasskey`, `ConfirmCancel`, `CurrentPasswordLabel`.
- Password sign-in card: `PasswordSignInTitle`, `PasswordSignInOn`, `PasswordSignInOff`, `PasswordSignInOnSentence`, `PasswordSignInOffSentence`, `TurnPasswordSignInOff`, `TurnPasswordSignInOn`, `AddPasskeyFirstHint`, `PasswordSignInOffConfirm`, `PasswordSignInOnConfirm`.
- Password card: `PasswordCardTitle`, `PasswordCardLead`, `PasswordReplaceButton`.
- Sign-in page: `SignInLeadBoth`, `SignInLeadPasskeyOnly`, `OperatorPasskeyNeedsJavaScript`, `OperatorPasskeyNeedsJavaScriptOnly`.
- Authenticator: `OperatorPasskeyUserName` ("operator") and `OperatorPasskeyDisplayName` ("Umbral operator").

Notices: `operator-passkey-added`, `operator-passkey-removed`, `operator-password-off`, `operator-password-on`, `operator-password-off-needs-passkey`, `operator-passkey-last`, `operator-passkey-not-confirmed` and `operator-passkey-limit` use the contract's wording.

These are mine, because the contract gives no wording for them:

- `operator-passkey-signin-failed`: "That passkey did not sign you in. Try again, or use your password if it is on."
- `operator-passkey-registration-failed`: "The passkey was not added. Try again." The browser's walk shows this when a device already holds an operator passkey.

These replace existing wording:

- `credential-current-incorrect` now reads "That's not the current password. Check it and try again." It used to say "credential".
- `credential-replaced` now reads "Password replaced. Use it next time you sign in."

These were removed because nothing reads them any more: `SignInLead`, `CredentialReplaceTitle`, `CredentialReplaceLead`, `CredentialReplaceButton`, `CurrentCredentialLabel` and `CredentialReplaced`.

## Open for your call

1. **Password card labels.** "New credential", "Confirm credential" and the hint about connectors are unchanged. Section 1 says "fields as today", and the first-run setup screen shares two of those labels and must stay unchanged. "New password" and "Confirm the new password" would match the card. Say if you want them.
2. **The sign-in field label.** It is still "Operator credential" (`CredentialLabel`), which setup shares. The same question applies.

## Deviations and decisions

- **Ceremony buttons without script.** Each one is rendered `hidden` and `disabled`, and `passkeys.js` enables it as it reveals it. Without script, pressing Enter in the add form's name field therefore does nothing; otherwise it would post the password to the wrong handler. The sign-in page's "or" divider is also hidden until scripted.
- **Focus.** The browser ignores `autofocus` on a button that was hidden at load. `passkeys.js` focuses a revealed control that carries `autofocus`, so the switch's confirm block opens with focus on **Confirm with a passkey**.
- **The last passkey while off.** **Remove** is refused at once with the Warning, before any confirm block or ceremony. The contract lists the refusal, not when it comes. I chose this because a ceremony whose answer is refused anyway wastes the operator's time.
- **Rate limit.** Completing an add now uses the ceremony limit instead of the admin limit; see research R6. The first walk showed a first setup spending 9 of the 10 admin permits.

## Checks

- `build-css.sh --check`: up to date. No new class was needed, so `umbral.css` is unchanged.
- Full `dotnet test`: 1770 of 1770 pass (2026-10-27).
- Idle memory, Linux arm64 (Docker, `mcr.microsoft.com/dotnet/sdk:10.0`), Release, `--filter-class Umbral.Tests.IdleMemoryTests`, 2026-10-27:

  | Run | Idle resident set | Ceiling |
  | --- | --- | --- |
  | Operator passkey in use (new) | 147.6 MiB | 150 MiB |
  | Local store | 140.1 MiB | 150 MiB |
  | Passkey connector in use | 147.2 MiB | 160 MiB |
  | Fediverse connector | 141.9 MiB | 150 MiB |
  | AWS store | 149.9 MiB | 160 MiB |

  macOS measures about 171 MiB for both passkey runs. The test applies only a sanity ceiling there, as its module comment explains. CI's linux/amd64 figure comes after the first push.
- Impeccable detector: I could not run it, because the CLI is not installed in this session.
- New test files: `OperatorPasskeyStoreTests.fs`, `OperatorPasskeyTests.fs`, `OperatorPasswordSwitchTests.fs`, `OperatorPasskeyRemoveTests.fs`. Additions went to `CredentialTests.fs`, `SubcommandTests.fs`, `CsrfTests.fs`, `SecurityEventCoverageTests.fs` and `IdleMemoryTests.fs`.
- CHANGELOG under `## Unreleased`: Added (operator passkeys, turning password sign-in off, `--forget-passkeys`), Changed (the Credential page in three parts, `reset-credential` wording), and Upgrade (schema 8).
