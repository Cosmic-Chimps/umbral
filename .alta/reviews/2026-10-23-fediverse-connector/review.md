# Review: the Fediverse connector (spec 010), the UI

Task: `.alta/tasks/2026-10-23-fediverse-connector-sign-in-with-a-fediverse-account-f3dv.md`
Design note: `design/contracts/fediverse-connector-ui.md` (PR #5 branch, corrected 2026-10-24
to name Mastodon and GoToSocial only). Frame: `design/contracts/apps-connectors-master-detail.md`.
Branch: `alta/earnest-cetus-9pd3` at `67eced4`, on the Connectors master-detail branch at
`fb2156e`. Reviewed 2026-10-24 by the design reviewer from the diff, the detector, a test run
and the 39 captures. This review covers what the operator and the person signing in see; the
protocol, storage and security are the spec's and the maintainer's to check, and `notes.md`
maps each statement to its code.

## Verdict

**Clear to merge. No blocking items; one nit on a shared component, optional.** The three
copy choices are approved as written.

## The three choices

1. **Callback field: "Callback URL" with the hint** "Umbral registers this address on each
   server. There is nothing to paste anywhere; a server's admin sees it in their list of apps."
   Approved. The providers' label would tell the operator to do something they must not do; this
   one says what the field is for and why it is shown.
2. **Empty-list warning** when Only these servers is saved with no server: "The list is empty,
   so nobody can sign in through Fediverse. Add a server, or choose Any server." Approved: a
   warning alert under the field, saved anyway, the next action in the last sentence. It is the
   same shape as the low-contrast accent warning on Apps.
3. **After a refused save the radio and the shared box show what was submitted; the heading's
   badges show what is stored.** Approved. The form is the operator's draft, the heading is the
   truth; that is how every refused form on the screen already behaves.

## What was checked

- **Connectors list and pane** against the design note §1 and the frame contract: the entry
  "Fediverse" / `fediverse` sits after the kinds not set up and before Passkey; the state
  badge, or "Not set up" with no closing row. The pane is Set-up (the lead, Callback URL with
  copy and its hint, the no-email info note), Settings (the shared check, the scope as
  `.um-choice` radio cards side by side from 768px, the Servers field with its placeholder and
  hint, the empty-list warning, the full-size primary Save connector or Enable Fediverse), the
  closing row (Disable, Delete). A refused server entry is drawn under the field in the
  operator's words as text (`connectors-list-refused`, 422). Data hooks as the note listed.
- **Sign-in page**: an ordinary "Sign in with Fediverse" button among the others, in both the
  plain and the branded chrome, Last used badge able.
- **The account step** against §2: `/connect/fediverse/start` without a handle renders one
  `max-w-sm` card in the picker's chrome (plain, or branded with the accent and "Secured by
  Umbral"); `h1` "Where is your account?"; one field "Your account or server" with the
  placeholder, the hint naming Mastodon and GoToSocial, `autocomplete="username"`,
  `inputmode="email"`, autofocus; a full-width primary Continue; a ghost "Other ways to sign
  in" with the chevron, only when the app offers more than one connector (`otherWays`,
  checked in code). The form posts with the return URL and the antiforgery token; it works
  without JavaScript.
- **Errors** against §3: not a handle, unsupported (a real Akkoma server, named as text),
  refused (neutral, "cannot sign in here"), each redrawn in place with the value kept,
  `aria-invalid` and `um-field-error`, focus back on the field. The wording is the corrected
  list: Mastodon and GoToSocial work; Akkoma, Pleroma, Misskey and Sharkey do not yet.
- **No new stylesheet rule**: `build-css.sh --check` clean; every part is an existing
  component.
- **Captures**: no overflow at 360 (the pane's callback field and the step card); one red
  fill per pane; the branded step takes the accent on the field's focus border and the
  Continue button only, the Gnomon unchanged.
- Tests, stylesheet, detector: see the last section.

## Nit (shared component, pre-existing, optional)

1. **A long field error centres its icon.** `.um-field-error` is `flex items-center`, so on
   the step the four-line unsupported message has its icon floating beside line two
   (`step-unsupported-360-dark`). `items-start` with the icon nudged down to the first line's
   centre (`mt-0.5`) reads better for every multi-line error on every form, and is one rule in
   `umbral.input.css`. Not this task's to own; take it here if you like (regenerate
   `umbral.css`), or leave it for a queued one-liner.

## For the documentation session

The copy the person reads on the step and in its errors is the product's statement of which
servers work. `docs/connectors/fediverse.md` and the README's connector line should say the
same four words: Mastodon and GoToSocial work; Akkoma, Pleroma, Misskey and Sharkey do not yet.

## Tests, stylesheet, detector

Run by the reviewer on the branch's worktree at `67eced4`, 2026-10-24:
`dotnet test` with the Fediverse, Connector, Picker and Csrf filters: 503 passed, 0 failed.
`build-css.sh --check`: clean, `umbral.css` unchanged by this branch.
Impeccable detector on `umbral.input.css`: one advisory, the pre-existing crop-shade colour;
nothing from this change.
