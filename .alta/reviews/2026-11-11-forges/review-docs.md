# Documentation review: GitLab, Codeberg, Forgejo and Gitea through the OpenID Connect connector (task f0rg)

Branch `alta/steady-eiger-d6kj`, one commit ac539d9 on main d4277e3. Reviewed 2026-11-13 by the
documentation reviewer, against `notes.md` in this folder and Umbral's code at d4277e3.

## Verdict

**Not clear yet: two blocking items, three nits.** The shape is right: rows in the issuer table, one
section, no page per forge, the README pointing at the section through the OpenID Connect connector.
I confirm it as the task asked. The evidence is unusually good: three forges walked straight and
through Umbral, raw tokens captured, the source line for Gitea's `email_verified` named, and the
untried cases said in italics. Both blocking items are about one thing: the section is a security
warning. It must not claim more safety than was shown, and an operator on the path through steps 1
to 4 must not be able to miss it. One re-check after the fixes.

## What was checked

- **Against Umbral's code.** The generic connector asks for `openid email profile` (`Connectors.fs`),
  so "tick `openid`, `profile` and `email` and nothing else" on GitLab is right. PKCE is sent when
  the document lists S256 (`Oidc.offersPkce`), and all five documents do. `picture` is read from the
  identity token. The identity is the issuer with `sub`, and `sub` is the account's numeric id on all
  three. `email_verified` must be the JSON `true`, which all three send. Right.
- **Against the notes.** Issuers without a trailing slash, RS256, exact redirect URI comparison, the
  menu paths (walked in the running products' UI), GitLab refusing an unconfirmed account at the
  authorize page with its confirmation setting off, Forgejo and Gitea marking a self-registered
  unconfirmed address verified, and a deactivated account refused before the callback. All match.
- **The email warning** at the top of the page ("Umbral trusts the emails your provider calls
  verified … there is no per-provider switch") is the right thing to point at, and the section does.
- **README** (intro and the list entry), **the changelog fragment**: right. No new page, so the
  sidebar stays as it is. Right.
- `check-links.py`: 390 links in 38 files, all resolve. `changelog.sh check`: in order.

## Blocking

### B1. "Codeberg … is in the safe group" says more than was shown

The section says Codeberg "asks every new account to confirm its address before it can be used, so
it is in the safe group". The source is Codeberg's own documentation, not a walk, and it covers only
**new** accounts. `email_verified` on Forgejo is `IsActive`, so the question for Codeberg is not
"was the address confirmed at sign-up". It is "can an active account ever carry an address it never
confirmed". Your notes leave exactly that open ("Not tried: whether an active Forgejo or Gitea
account can make an unconfirmed address its primary one while confirmation is on (the claim would
still be `true`)"). The same doubt applies to a Forgejo or Gitea of your own with
`REGISTER_EMAIL_CONFIRM = true`, which the bullet offers as one of the safe settings.

Say what is known and no more. For example:

> Codeberg says every new account must confirm its address before it can be used (its own
> documentation; not tried with an account here). That covers sign-up. Whether an account can
> later make an unconfirmed address its primary one was not tried, on Codeberg or elsewhere, and
> the claim would still say `true`.

Keep the three settings as they are, but make the confirmation one honest the same way: closed
registration is the only one of the three that does not depend on that open question. Either walk
the change-primary-address case on the local Forgejo and Gitea (then state the result), or leave
the claim out.

### B2. The issuer rows for Forgejo and Gitea must point at the warning

The intro says "The steps below apply as written", and step 2's table now hands out the Forgejo and
Gitea issuers. An operator who follows steps 1 to 4 can copy the issuer and add the connector
without reaching the forge section. That section says that an open Forgejo or Gitea, which is their
default when no mailer is set up, lets anyone take over an Umbral account by typing its address.
That must be on the path. Add to the **Forgejo, Codeberg** and **Gitea** rows: "Before adding one
you run, read [what its `email_verified` means](#a-public-forge-gitlab-codeberg-or-one-you-run):
open registration without email confirmation lets people claim any address." The GitLab row needs
nothing.

## Nits

1. **"You do not need this if you only want Discord, GitHub or Google."** This predates the branch,
   but the page is being edited. Apple and Microsoft have their own walkthroughs now: "Discord,
   GitHub, Google, Apple or Microsoft".
2. **Give the warning a heading.** The bold paragraph label **What `email_verified` means there.**
   is the part to link to (B2, and from the email warning at the top). As a `###` heading it gets
   its own anchor, and B2's link can go straight to it.
3. **The README entry** lists Forgejo and Gitea beside providers that need no thought. It is fine,
   because it links into the section, but four words would help an adopter skimming the front door:
   "… (through the OpenID Connect connector; read its email note)". Optional.

## For the maintainer (not a condition of this review)

The finding is bigger than the documentation. With the defaults of a fresh Forgejo or Gitea (open
registration, no mailer), connecting it lets anyone who registers there claim any address, and so
be linked to that person's Umbral account. That holds even for someone who signed in through
Google. Umbral's answer today is the operator's judgement: the page says "there is no per-provider
switch". Principle III ("secure by default, not by configuration") argues for a follow-up. One
option: a per-connector choice to treat a provider's `email_verified` as unverified. It would be off
by default for most providers, and the add form would suggest it for a forge. The documentation is
right to describe today's behaviour. Whether Umbral should change is the maintainer's call.

## Re-check at 24d362b (2026-11-13)

**Not clear to merge, for one new reason: disclosure.** B1, B2 and the three nits are fixed, and
B1 was settled by a walk instead of a hedge, which was the better choice. The section now says what
was tried and no more. The Forgejo/Codeberg and Gitea rows point at the new heading
(`#what-email_verified-means-on-a-forge`), and the intro does too. `check-links.py`: 393 links
resolve. `changelog.sh check`: in order.

### B3 (new). Hold back how to reproduce a Forgejo finding until the maintainer decides on a report

The B1 walk found that Forgejo 14.0.5 could not be shown to keep an existing account's primary
address confirmed, and that this reached Umbral as `email_verified: true`. It looks unreported. A
related, already-fixed issue is public:
<https://codeberg.org/forgejo/security-announcements/issues/39>. This repository is public, so how
to reproduce the finding stays out of it until the maintainer has decided on a private report to
Forgejo. The report is the maintainer's to make, not this review's. The page keeps the conclusion
and the advice: add a Forgejo, Codeberg included, only if you trust everyone with an account there.
Once a Forgejo release names it fixed, the details can come back, with that version named.

One re-check after this.

## Re-check at 7cfc8e1 (2026-11-13)

**The content is clear. The merge has two conditions, both outside the documentation.**

- B3 is fixed in the tracked files at the tip. oidc.md gives the conclusion and the advice ("could
  not be shown to … until a Forgejo release names this fixed"), and no mechanism. The
  Forgejo/Codeberg row says "as of Forgejo 14.0" instead of claiming it for Codeberg. The reproduction
  script is gone from the tip, the other walk script is trimmed, and notes.md step 3 gives the result
  only. This review file was trimmed in the same way in this commit.
- `check-links.py`: 393 links resolve. `changelog.sh check`: in order.

Conditions for the merge:

1. **History.** Removing a file in a later commit leaves it in the earlier ones. Commit 24d362b still
   carries the reproduction script, the full notes step and the mechanism in oidc.md. Commit e61d939
   (this review's earlier text) names them. None of those commits may reach the public repository.
   Merge this branch only as one squashed commit of its final tree, or rewrite the branch before it
   is ever pushed. Do not push the branch as it is.
2. **The report decision** stays with the maintainer, as said in B3. Merging the conclusion is fine
   once the maintainer has decided, because it gives no method.

A note for whoever makes the report: the held-back copies are under `/tmp/f0rg/`, which does not
survive a reboot. Move them somewhere private and durable, outside the repository, first.
