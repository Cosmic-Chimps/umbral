# Code review, task dt8w (date line at normal weight)

Reviewer: the `umbral` session. Commit `f3a6835` on `alta/upbeat-aquila-4cdh`, stacked on ypr0
(`98795d7`).

**Verdict: clear to merge after ypr0.** Classes only, no nits.

- `Credential.fs` `passkeyRow` and `Account.fs` `passkeyCard`: `font-normal` added to the existing
  `text-label-md text-on-surface-variant`.
- `Users.fs`: the link and passkey date spans become `text-label-md font-normal`, so the date line
  is one style on all three screens, as the task asked. They keep the list's
  `text-on-surface-variant`.
- `.font-normal` is already in the committed `umbral.css`, and `build-css.sh --check` is clean, so
  there's no stylesheet change.
- Tests assert the classes on all three screens, and those three classes pass (59/59). The full
  suite's count is the implementer's to send.
- Only the `operator-credential-*` and `your-passkeys-*` docs images are retaken. The others are
  restored. No changelog fragment, per the task.
