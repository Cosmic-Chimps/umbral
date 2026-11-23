# Documentation review: Forgejo's fix named (task fx63)

Branch `alta/hopeful-arcturus-tfjh`, docs commits 3ff32bb and 55d0560 on main b0a1472
(`git diff b0a1472 55d0560`). Reviewed 2026-11-23 by the documentation reviewer. I checked it against
Forgejo's advisory itself
(<https://codeberg.org/forgejo/security-announcements/issues/63>, read today), the notes' new
section "Published after Forgejo's fix", and `review-code-fx63.md`.

## Verdict

**Clear, no blocking items, three nits.** This is the follow-up the f0rg review asked for ("once a
Forgejo release names it fixed, the details can come back, with that version named"), and it does
exactly that. It names the versions, restores the Gitea-refuses / Forgejo-accepts contrast, and is
honest about what upgrading does not undo. The advice is no wider than the fix. No re-check is
needed for the nits.

## What was checked

- **Against the advisory.** Fixed in 15.0.12 (v15 LTS) and 17.0.2, released 2026-11-18. v16 reached
  end of life on 2026-10-29 and gets no fix ("Affected users must upgrade to Forgejo v17"). The bug:
  the page refused an unverified address as primary, but "the backend POST endpoint did not validate
  that check". oidc.md, the changelog fragment and the notes say the same. Right.
- **What upgrading does not undo.** The advisory is silent on it. The docs rest it on the walk of
  2026-11-23 on 17.0.2 (an unconfirmed primary set in the database still came out as
  `email_verified: true`), and they say "tried on 17.0.2". That is the right source, and the right
  wording. The three conditions for **Trusted** on a Forgejo you run (a fixed version; closed or
  confirming registration; no unconfirmed primary left) follow from it. "Where to check" names the
  admin page, as walked (**Site administration → Identity & access → User emails**, Primary and
  Activated), and the table and columns. Right.
- **Codeberg: always Not trusted.** The reason given is open registration, affected versions in its
  past, and no way to check from outside. It claims nothing about Codeberg's current build. Right.
- **The Forgejo/Codeberg row** in the issuer table matches the section. The Gitea row is unchanged and
  still right.
- **The reproduction is restored** (`force.mjs`, the end of `primary.mjs`, step 3 of the notes) under
  "Published after Forgejo's fix". That meets the condition the f0rg review set. The advisory now
  describes the mechanism publicly, and the script reproduces it on 14.0.5, a version Forgejo no
  longer supports. Fine.
- **The changelog fragment** (Changed) states the versions, the three conditions, v16 and Codeberg.
  Right.
- `check-links.py`: 474 links resolve. `changelog.sh check`: in order.

## Nits

1. **"every release older than v15".** The section lists as affected "v15 before 15.0.12, v17 before
   17.0.2, every v16 …, and every release older than v15". The advisory names only v15, v16 and v17.
   For older releases the evidence is the walk on 14.0.5. The advice (fixed versions only) is right
   either way. To state only what is known: "…every v16 (end of life 2026-10-29, no fix), and older
   releases, which get no fix (14.0.5 was tried and is affected)".
2. **The intro's present tense.** Line 19 still says "because an account there can claim an address
   it never confirmed". That remains true for a Gitea with open registration, an unfixed Forgejo, or
   an address left from before an upgrade. But read on its own it now sounds as if every Forgejo
   does. Write "because, depending on its version and settings, an account there can claim an
   address it never confirmed".
3. **The notes' quoted fix title.** The notes give the advisory's fix as "check email activation in
   MakeEmailAddressPrimary". The advisory's own words are "validating the email address is validated
   before marking it as a primary email". If the first is the commit title, say "(the commit's
   title)", so a reader comparing it with the advisory does not look for a sentence it does not have.
