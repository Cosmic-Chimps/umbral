# Task dt8w: the date line at normal weight on all three screens

Branch `alta/upbeat-aquila-4cdh`, **stacked on ypr0** (`alta/hopeful-etna-g28k`, 98795d7, fast-forwarded):
the Your passkeys line this task changes only exists there. Merge after ypr0. Task file:
`.alta/tasks/2026-11-16-date-lines-normal-weight-dt8w.md`.

## The change (classes only)

| Screen | Where | Before | After |
| --- | --- | --- | --- |
| Credential | `Credential.fs` `passkeyRow` | `text-label-md text-on-surface-variant` | `text-label-md font-normal text-on-surface-variant` |
| Your passkeys | `Account.fs` `passkeyCard` | `text-label-md text-on-surface-variant` | `text-label-md font-normal text-on-surface-variant` |
| Users card, link row | `Users.fs` `linksList` | `span()` (inherits `text-body-md`) | `span(class' = "text-label-md font-normal")` |
| Users card, passkey row | `Users.fs` `passkeysList` | `span()` (inherits `text-body-md`) | `span(class' = "text-label-md font-normal")` |

The Users spans keep the colour of their `ul` (`text-on-surface-variant`). `umbral.css` already held both
classes: `build-css.sh --check` is clean.

## Tests

- `PasskeyAccountTests` (ypr0's assertions): `font-normal` added.
- `CredentialTests` "passkeys are listed by name…": the date `<p>` with the three classes.
- `UserLinkAdminTests` "the list is drawn…": the link and passkey date spans with `text-label-md font-normal`.

## Captures

`capture.mjs` (Playwright, Chrome), output in `capture-output.log`. Release build of the change, fresh
instance on `http://localhost:18097` (Development, `HTTPS_PROXY=http://127.0.0.1:9`), claimed through
`/admin/setup`. The operator adds "Laptop" on Credential with a virtual authenticator; a person signs up
with "MacBook", adds "Pixel 9" and signs in with it; one user, Alice, with a GitHub link and a passkey is
written into the SQLite file (a link needs a real provider). Three shots at 1280, dark:
`credential-1280-dark.png`, `your-passkeys-1280-dark.png`, `users-card-1280-dark.png`. None overflows.
Every date line measures `font-weight 400`, `font-size 13px`.

For the designer: on the Users card the dates are now one step smaller than the name beside them
(13 px against 14 px), which is what the other two screens do with the name above the line.

## Docs images

`site/scripts/screenshots/run.sh` retook all 16; only `operator-credential-{dark,light}.png` and
`your-passkeys-{dark,light}.png` are kept, the rest restored.

No `changelog.d/` fragment: a weight change nobody acts on.

## Verification

- `CredentialTests`, `UserLinkAdminTests`, `PasskeyAccountTests`: 59 of 59.
- Full suite on 367b0cd: **3013 of 3013** passed, 0 skipped (Debug, 42 min 36 s). Same count as ypr0: the
  change adds assertions, not tests.
- Code review clear (24325c7), design review clear, no re-check (f7f4e9a).
