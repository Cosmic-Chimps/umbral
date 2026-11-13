# Documentation review: trusting, or not, an OpenID Connect provider's verified emails (task tr5t, spec 017)

Branch `alta/chipper-duet-8b42` at c971183 on main 8a1d88c, docs commit e0b840c. Reviewed 2026-11-13
by the documentation reviewer, against the code at c971183, `contracts/screens.md` and `notes.md`.

## Verdict

**Not clear yet: one blocking item, two nits.** Every behaviour you asked me to check matches the
code. Every screen string the pages quote is verbatim. Nothing new describes how a Forgejo account
gets an unconfirmed address marked verified. The one blocking item is about what the setting does
**not** undo, which the Security note currently implies it does. One re-check after the fix.

## What was checked

- **The base.** Local main 8a1d88c and this branch do not contain 24d362b, the f0rg history. Right.
- **Not trusted.** `EmailDomainRules.recheck` judges an untrusted connector as if the session had no
  verified domain. The trust setting feeds `email_verified` at sign-in (a5b5e22). So the address is
  stored and sent unverified, never links anyone and never meets a domain rule. Right.
- **Turning trust off.** `ConnectorStore.lowerForIssuer` clears the issuer's links' asserted flag.
  Then it lowers `users.email_verified` only where no other link still backs it
  (`Database.UnbackedEmailVerified`), and asks first with the count (`ConnectorEmailTrustConfirm`, its
  singular and plural, and `…ConfirmNone`). **Turning trust on** raises nothing. Right.
- **Sessions admitted by a domain rule** through this connector are refused at the next authorize or
  refresh (`recheck`'s untrusted branch). Right (N1 for the wording).
- **The upgrade.** Schema 14 sets `email_trust = 1` for every `oidc` connector. Right, and backup.md
  says so.
- **The log.** On update, `email_trust=on` or `email_trust=off;lowered=<n>`, only when the setting
  changed or something was lowered; tests at `OidcEmailTrustAdminTests.fs:374,390`. On create it is
  `email_trust=<on|off>` with no `lowered`. That is close enough (N2).
- **Screen strings**: the legend, **Trusted**, **Not trusted**, the no-choice refusal, the own-list
  refusal, the blocked-by-instance-list warning, the confirm text and **Turn trust off**. All
  verbatim against `Texts.fs:1420-1448`.
- **The held-back method**: nothing in `docs/`, `changelog.d/`, the spec or the review notes
  describes it. The forge section keeps only the conclusion. Right.
- `check-links.py`: 404 links in 39 files, all resolve. `changelog.sh check`: in order.

## Blocking

### B1. Turning trust off does not undo a link already made, and the docs must say so

The Security note says turning trust off means "an account a stranger registered earlier no longer
holds someone else's address as verified". That is true of the **address**. But the harm this
feature exists for is the **link**. With trust on, a stranger who registered at an open forge with
someone's address was linked into that person's Umbral account, the same user and the same `sub`.
Turning trust off "unlinks nobody". That stranger's forge account goes on signing in as that person
in every app. An operator who reads the Security note and turns trust off will think the hole is
closed. For anyone already taken over, it is not.

Say it where an operator acts on it: in "Changing the email setting later" and in the Security
note. For example, after "Turning trust off unlinks nobody who is already linked." in oidc.md:

> That includes a link the provider should never have made. If people could sign up at this
> provider while it was **Trusted**, open the **Users** screen and look at every user who has this
> provider **and** another one among their providers. A link you do not recognise means someone
> may have claimed another person's address. Umbral has no way to remove one link. Disable that
> user, and have the person sign in again through their own provider (a new account), or delete
> the user if it is not a real person's.

Check the remedy wording with the implementer of the Users screen. It must match what the screen
offers (disable, delete). In the changelog's Security note, after "…says how many.", add: "It does
not remove a link already made: see the walkthrough for how to look for one."

## Nits

1. **"People who were admitted through an allowed email domains rule are refused…"** should say
   "who signed in **through this provider** under an allowed email domains rule". Sessions through
   other connectors are judged by those connectors and are not affected.
2. **The created event.** "`connector_created` and `connector_updated` carry `email_trust=on`, or
   `email_trust=off;lowered=<n>`" is exact for an update. A new connector's event carries
   `email_trust=on` or `email_trust=off`, with no `lowered`, since nothing exists to lower. Write it
   that way.

## For the maintainer (not a condition of this review)

B1 shows a gap the documentation can only describe. Umbral cannot remove a single link, so the only
remedies for a wrong link are blunt (disable or delete the whole user). An "unlink this provider"
action on the Users screen would let an operator clean up after a trusted provider turned out to be
untrustworthy, without costing the real person their account.

## Re-check at 02aabcc (2026-11-14)

**Clear to merge.** B1 and both nits are fixed (`0a41312..02aabcc`).

- **B1**: "Changing the email setting later" now says a wrong link made while **Trusted** stays,
  that the person behind it goes on signing in as the other person, how to look for one on the
  **Users** screen, and that Umbral cannot remove one link. The remedy corrects my draft, and the
  correction is right. Disable is per user (`Callback.fs`, `user_disabled`), so it stops the real
  person too. Delete removes every link with the user (`linked_accounts … ON DELETE CASCADE`), after
  which the two sign in as separate accounts. The changelog's Security bullet points to the section.
- **N1**: "signed in through this provider under an allowed email domains rule". Right.
- **N2**: created events carry `email_trust=on|off`, and updates carry `email_trust=on` or
  `email_trust=off;lowered=<n>`. Right.
- `check-links.py`: 404 links resolve. `changelog.sh check`: in order.
