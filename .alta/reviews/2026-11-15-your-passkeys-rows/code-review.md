# Code review, task ypr0 (Your passkeys rows like the Credential screen's)

Reviewer: the `umbral` session. Commit `8f5ec87` on `alta/hopeful-etna-g28k` (base main `bb3fc37`).

**Verdict: clear to merge.** Nothing blocking, no nits.

- **View only, as the task required.** `passkeyCard`:
  - The two date lines become one: `{t.OperatorPasskeyAddedOn(date …)} · {used}` in
    `text-label-md text-on-surface-variant`, built as `Credential.fs`'s `passkeyRow` builds it.
    This file's `date` is the same invariant `yyyy-MM-dd`.
  - Remove is `btnSm Ghost + " text-error hover:text-error"`.
  - Unchanged: the form, the route, the antiforgery input and `confirmSubmit
    t.PasskeyRemoveConfirm`, so the server-side refusal (the last passkey, the owner) is untouched.
- **Texts:** `PasskeyAddedLabel`, `PasskeyLastUsedLabel` and `PasskeyNeverUsed` are removed. Nothing
  else reads them, and MetaTests require every field to be read. The never-used wording moves from
  "Never" to the Credential screen's "Never used", which is the point.
- **Tests:**
  - Each of Sam's rows is asserted after a sign-in: one used, one never used.
  - Each Remove's classes and its `data-confirm` are asserted.
  - Rows are looked up by user id, so Ana's own "Phone" can't satisfy the assertion.
- **Docs image:** I opened `your-passkeys-light.png`. It shows "Added 2026-11-16 · Never used" and
  the red ghost Remove. The run's other rewrites were restored, as for k3yi.
- The implementer reports the full suite at 3013/3013.

No re-check needed.
