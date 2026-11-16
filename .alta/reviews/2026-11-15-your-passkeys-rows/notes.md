# Task ypr0: Your passkeys rows like the Credential screen's

Branch `alta/hopeful-etna-g28k`, on main `bb3fc37`. Task file:
`.alta/tasks/2026-11-15-your-passkeys-rows-like-the-credential-screen-ypr0.md`.

## The change

- `src/Umbral/Handlers/Passkeys/Account.fs`, `passkeyCard`:
  - The two date lines ("Added …", "Last used …"/"Never") are one line built as
    `Credential.fs` `passkeyRow` builds it: `{t.OperatorPasskeyAddedOn(date …)} · {used}`, `used` being
    `t.OperatorPasskeyLastUsedOn(date …)` or `t.OperatorPasskeyNeverUsed`, in
    `text-label-md text-on-surface-variant`. `date` here is the same `yyyy-MM-dd` (invariant) as
    Credential's `day`.
  - Remove is `btnSm Ghost + " text-error hover:text-error"`, as on Credential and the Apps pane. Its
    form, route, antiforgery input and `confirmSubmit t.PasskeyRemoveConfirm` are unchanged.
- `src/Umbral/Views/Texts.fs`: `PasskeyAddedLabel`, `PasskeyLastUsedLabel` and `PasskeyNeverUsed` were
  read nowhere else, so they go (MetaTests' "every Texts field is read"). "Never" becomes "Never used".
- `changelog.d/your-passkeys-rows.md` (Changed).
- `docs/images/your-passkeys-{dark,light}.png` retaken with `site/scripts/screenshots/run.sh`.

No route, refusal or confirmation changes.

## Tests

`PasskeyAccountTests`, "adding passkeys lists them, signs in with the same sub and removing one revokes
it": after the Laptop signs in, each of Sam's rows reads `Added {today} · Last used {today}` (Laptop)
and `Added {today} · Never used` (Phone) in `text-label-md text-on-surface-variant`, and each Remove
button carries the ghost and error classes, its form `data-confirm`.

## Captures

`capture.mjs` (Playwright, Chrome), output in `capture-output.log`. A Release build of the change, a
fresh instance on `http://localhost:18094` (Development, `HTTPS_PROXY=http://127.0.0.1:9`), claimed
through `/admin/setup`; the Passkey connector enabled with open sign-up. The person signs up with a
virtual authenticator ("MacBook"), adds "Pixel 9" on a second one, signs out and signs in with the
Pixel 9: one key never used, one used. Four shots, `your-passkeys-<width>-<theme>.png`, 1280 and 360,
dark and light, full page. **None overflows.**

For the designer: on Credential the Remove ghost sits at the right of the row; here it stays under
the dates, as the card's Remove did, so its ghost padding sets the word in a little from the text
column. Moving it is not part of this task.

## Docs image

`site/scripts/screenshots/run.sh` retook all 16 images; only `your-passkeys-{dark,light}.png` are kept.
The run also rewrote `app-created-*` and `operator-credential-*`, pages this change does not touch;
they were restored.

## Verification

- `PasskeyAccountTests`: 29 of 29.
- Full suite on the working tree committed with this note: **3013 of 3013** passed, 0 skipped
  (Debug, 24 min 37 s).
