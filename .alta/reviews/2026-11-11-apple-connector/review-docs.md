# Documentation review: Sign in with Apple (task ap1e, spec 013)

Branch: `alta/indigo-pheasant-5jd9`, range `25f3718..8fd0a41`, the docs commit `e53e44a`.
Reviewed 2026-11-11 by the documentation reviewer session, per section 6 of
`docs/audit-2026-10.md`, from the diff, the "For the documentation review" section of
`notes.md`, the spec, and the code on the branch.

## Verdict

**Clear to merge.** No blocking item. Four nits, none needs a re-check.

## What was checked

- **Every message, label and button the walkthrough quotes is verbatim in `Texts.fs`.** I checked
  them myself with a script: the five field labels (**Display name**, **Services ID**, **Team ID**,
  **Key ID**, **Private key (.p8)**), **Replace the private key**, **Save connector**, **Sign in
  with Apple**, and all ten messages of "What the form says", including the three with a
  `<reason>` hole.
- **Behaviour against the code.** The client secret is signed per sign-in and lives five minutes
  (`Apple.fs`, `clientSecretLifetime = TimeSpan.FromMinutes 5.0`); the button is white on the dark
  theme and black on the light one, with no accent colour and an empty logo slot
  (`umbral.input.css:86-96`); one Apple connector with the identifier `apple` (`saveApple`);
  Save and Enable make the same two checks; a replaced key keeps the connector's state; the stored
  name is kept by `COALESCE`, and a user created without one is "Apple user"; `email_verified` as
  the boolean or the text `"true"` only (`claimIsTrue`); the three log words in Troubleshooting
  exist and are asserted in `AppleSignInTests.fs`.
- **Apple's rules.** The walkthrough says plainly which steps were read on Apple's pages on
  2026-11-11 and which were not checked on the live site (the App ID steps, the Team ID's place),
  links Apple's pages, and quotes guideline 4.8 with its date. That is the right way to document a
  third party's console. Leaving PKCE out (research R5) and making no claim that the button passes
  App Review are right too; the "no logo yet" warning appears twice, at the top and in Gotchas.
- **The other files.** The README's introduction, Connector row ("several per provider, except
  Apple, where there is one per instance") and link list; the six "Other providers" lines; the
  `idp` list in `docs/integrate.md`; `apple` among the reserved identifiers in `oidc.md`; the
  `who-can-sign-in.md` list; the native-apps provider list and its guideline 4.8 paragraph;
  `docs/run/backup.md` at schema version 10 with what it adds; the sidebar row; the changelog
  fragment, Added and Upgrade, in the 0.2.0 wording for a schema step.
- **Checks.** `check-links.py`: 316 links in 36 files, all resolve; `changelog.sh check` in order.

## Nits

1. **"this can happen with Apple's Work and School accounts"** (sections 4 and Allowed email
   domains). It is the one statement in the walkthrough with no source in `notes.md`, and Apple's
   current name for those accounts is "Managed Apple Accounts". Either cite the Apple page that
   says such an account may sign in without an email, or drop the parenthetical and keep "A person
   whose Apple Account sends no email signs in without one", which is what the code guarantees and
   what an app must handle anyway.
2. **"One Apple connector per Umbral instance"** (Before you start). Say why in one sentence, the
   spec's reason, since every other provider page has "Giving one app its own name" and a reader
   will look for it here. If an app wants its own name on Apple's consent sheet, say what to do
   instead, or that it cannot.
3. **The linking gotcha says only the relay half.** "It is not the address of their Google or
   GitHub account, so Umbral does not join the two users" is right for a relay address. Add the
   other half: a person who shares their real address, verified by Apple, *is* joined to an
   existing user with that verified address, from any connector. That is the case an operator is
   more likely to be surprised by.
4. **Troubleshooting's first sentence** links "Someone clicks a provider and sees "Sign-in didn't
   complete"" with nested quotes, and that entry is a bold paragraph, not a heading, so the link
   cannot land on it. Say "the entry on "Sign-in didn't complete" in
   [Troubleshooting](../run/troubleshooting.md)", or give that entry a heading in its own task.

## notes.md

Complete under section 6: every statement with its code line, "quoted from Texts.fs" or Apple's
page; what was not verified on Apple's site said plainly in the notes and on the page; the checks.
The one gap is nit 1.

## Answers (implementer, 2026-11-11)

All four nits taken, in `docs/connectors/apple.md`:

1. The parenthetical now uses Apple's own name and attributes it: "Apple says this can happen
   with Sign in with Apple at Work & School" (both places). The source is in `notes.md` now:
   `research.md` R1, the `id_token` page ("may be empty for Sign in with Apple at Work & School
   users", "The system may not verify email addresses" for them).
2. "Before you start" says why there is one: the other providers let you add a second connector
   to give one app its own name on the button; Apple's rules fix this button's title, so two
   would be two identical buttons. And what to do instead: an app that must have a Services ID
   of its own needs an Umbral instance of its own.
3. The relay gotcha adds the other case: a person who shares their real address is joined as
   with any provider, when Apple reports it as verified and one existing user has that
   verified address.
4. The Troubleshooting sentence no longer nests quotes and no longer points at an anchor that
   does not exist: it names the entry about a person who sees *Sign-in didn't complete*.

Two messages in the table changed with the design review (the display name's refusal ends
"then try again."; the secret-store notice ends with its next action). Both are again
verbatim in `Texts.fs`. The link check passes (316 links).
