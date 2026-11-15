# Task njtn: Your passkeys draws its two successes as errors

Branch `alta/glad-gentian-hpdg`, on main `4958e12`. Task file:
`.alta/tasks/2026-11-15-your-passkeys-your-passkey-is-ready-and-passkey-removed-are-njtn.md`.

## The change

- `src/Umbral/Handlers/Passkeys/Account.fs`: the page has a notice helper of its own, built like the
  Credential page's (`<div data-notice="{code}">` around `Components.alert`). `passkey-added` and
  `passkey-removed` are `Success` (green, check icon, `role="status"`). Every other known code
  (`passkey-not-found`, `passkey-last-cannot-remove`, `passkey-registration-failed`,
  `passkey-limit-reached`) stays `Failure` (red, `role="alert"`).
- `src/Umbral/Handlers/Passkeys/SignIn.fs`: `noticeCode` reads `?msg=` through `Texts.knownNotice`
  (one place for "never any other text from the address"). `noticeAlert` uses it and stays a failure
  for the sign-in and invitation pages, which are only sent back with a failure.
- No wording changes in `Texts.fs`, no CSS change (the success alert already exists).
- `changelog.d/fix-passkey-account-notices.md` (Fixed).

## Tests

`PasskeyAccountTests`: "the page's notice is a success for a passkey added or removed and a failure
for a refusal". It checks the class and role for the two successes and the two refusals, and that an
unknown code draws nothing. The sign-in page's failure notice is already covered in
`PasskeyCeremonyTests` (`msg=passkey-sign-in-failed`).

## Captures

`capture.mjs` (Playwright, Chrome), output in `capture-output.log`. A Release build of the change, a
fresh instance on `http://localhost:18093` (Development, `HTTPS_PROXY=http://127.0.0.1:9`), claimed
through `/admin/setup`. The Passkey connector is enabled with open sign-up through its pane, and the
person signs up with Chrome's virtual authenticator (one authenticator per key). 16 shots,
`<state>-<width>-<theme>.png`, 1280 and 360, dark and light, full page. **None overflows.**

| State | How it got there | On screen |
| --- | --- | --- |
| `notice-added` | the real Add ceremony (redirect `?msg=passkey-added`) | "Your passkey is ready." green, check icon |
| `notice-removed` | a spare key added, then the real Remove | "Passkey removed." green, check icon |
| `notice-last` | `?msg=passkey-last-cannot-remove` | red, as before |
| `notice-not-found` | `?msg=passkey-not-found` | red, as before |

The docs screenshot of Your passkeys (task v6pm, `site/scripts/screenshots/`) is taken after a reload,
so it shows no notice. It needs no retake for this change. The v6pm note "retake after njtn" can go.

## Verification

- Passkey tests (`PasskeyAccountTests`, `PasskeyCeremonyTests`, `PasskeyInvitationTests`): 77 of 77.
- Full suite: running (result below when it lands).
