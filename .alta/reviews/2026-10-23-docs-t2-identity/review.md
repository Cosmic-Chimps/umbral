# Review: T2, "Integrate an app" and "How people are identified" match the code

Task: `.alta/tasks/2026-10-23-docs-t2-integrate-an-app-matches-the-code-t2id.md`,
audit `docs/audit-2026-10.md` T2 (W1, W2, W3, M4) plus the README link to the
vision found during T9.
Branch: `docs/t2-identity`, commit `f11e915` on `main` at `3a235da`.
Reviewed 2026-11-01 by the documentation reviewer session, from the diff, the
implementer's `notes.md`, the code each statement cites, and my own runs.

## Verdict

**Clear to merge.** No blocking item. Two nits, neither needs a re-check, and
one note for the maintainer.

## What was checked

Each statement in the changed text against the code on the branch:

- **Claims.** `setProfileClaims` (`Handlers/Connect/Authorize.fs:96-128`):
  `sub`, `name`, `idp` always; `email` and `email_verified` together when the
  user has an address, the flag as a JSON boolean; `picture`,
  `preferred_username`, `profile` when set. Only the fediverse adapter sets a
  username or a profile URL. The placeholder name is the kind's display name
  plus "user" (`UserStore.fs:131`, `Domains/Connectors.fs:50-58`: "OpenID
  Connect user" is right). GitHub yields no email when `/user/emails` has no
  verified primary address, and also when the call fails (`GitHub.fs:178-195`;
  see nit 1).
- **Email first-seen, flag only upwards.** `UserStore.fs:155-159`:
  `email = COALESCE(email, @email)` and `email_verified = CASE WHEN @verified =
  1 THEN 1 ELSE email_verified END`. The README states exactly that.
- **Linking.** `Domains/Linking.fs:48-101`: a verified email on the provider's
  side, exactly one existing user with that address verified, any connector
  kind; `AmbiguousMatch` creates a new user. The OIDC account key is
  `issuer#sub` (`Connectors.fs:329`). The README and `oidc.md` now say the same
  thing, and the README names the trust warning on the add form.
- **Endpoints.** Paths from `OpenIddictSetup.fs:25-40`; the app permissions
  (`AppClients.fs:18-27`) carry authorization, token, end-session and
  revocation, not introspection, while the endpoint is set, so it is in the
  discovery document and answers `unauthorized_client`. The implementer's
  throwaway run shows the exact answer. A refresh re-reads the person
  (`Token.fs:61-77`): deleted or disabled gets `invalid_grant`.
- **Signing out.** `EndSession.fs`: Umbral's session only, and the redirect is
  delegated to OpenIddict's sign-out with `/` as the fallback, so an
  unregistered `post_logout_redirect_uri` is not refused: the person is signed
  out and lands on the home page. The audit's "accepts only the registered
  post-logout URI" was the stricter reading; the code's is what the README now
  says, and the throwaway run confirms it (`LOGOUT-BAD 302 loc=/`).
- **Disabled or deleted.** `UserToggle.fs` revokes every authorization;
  `Authorize.fs:315-340` answers `login_required` for a deleted person and
  `access_denied` for a disabled one, with the texts the README quotes
  (`Texts.fs`, "This account has been disabled. Contact whoever runs this
  Umbral.").
- **The tests.** `OidcProtocolTests.fs:526` (unverified email: both tokens
  carry exactly `sub`, `name` as the placeholder, `email`, `email_verified`
  `false`, `idp`) and `PasskeyInvitationTests.fs:292` (passkey-only: exactly
  `sub`, `name`, `idp`). I ran the filter in the worktree: 10 tests matched, 0
  failed. The README's claim list now has a test behind each bullet, which is
  what T2's acceptance asked for.
- **Vision link.** Contributing has "The vision is in
  [docs/vision.md](docs/vision.md)." as T2's task file specified.
- **CHANGELOG.** One entry under `## Unreleased`, `### Fixed`. A doc-only
  correction, but one an integrator codes against, so a line is right.
- **Links.** `check-links.py`: 187 links in 14 files, all resolve, the new
  `#signing-out` and `#endpoints` anchors included.

## Nits

1. **README, the `email` bullet: "a GitHub account with no verified primary
   address".** The code also sends no email when GitHub's `/user/emails` call
   fails or the scope is refused (`GitHub.fs:183-189`), so a person can be
   emailless on one sign-in and not the next. Audit M14 has T5 saying this in
   the GitHub walkthrough; here one clause ("or whose email GitHub did not
   return") keeps the README's list of reasons complete. Optional.
2. **README, "When a person is disabled or deleted": the two error codes.**
   `error=access_denied` and `error=login_required` are right, and worth one
   more sentence for the integrator: both come back to the app's redirect URI,
   so the app should treat them as "sign in again" rather than as a bug. Yours
   to take or leave.

## For the maintainer (outside this task)

The implementer found that the flag is raised by whichever address the
provider verifies today while the stored email stays the first one seen
(`UserStore.fs:158-159`), filed as task h762. The README now states the
current behaviour ("can go from `false` to `true`, never back"), which is the
right thing for T2 to do, and it means the "Trust the address only when the
flag is `true`" advice is only as good as that fix. When h762 lands, its task
should include the one-sentence change to this bullet; I will check it then.

## notes.md

Complete under section 6: files, each finding and its change, every
behavioural statement with file and line, the three places the code won over
the audit with the runs that show it, the two tests and the full-suite result,
the link check, and the reason for the CHANGELOG entry. The throwaway-run
transcript is the most useful piece of evidence a notes file in this series has
carried. Agreed on all of it.

---

## Addendum, 2026-11-01, at `ad824f6`

**Still clear.** Both nits taken. The GitHub clause is in the email bullet. The
error-code sentence is better than the one I proposed: Umbral ends the session
before answering either error (`Authorize.fs:318,325`, `Sessions.signOut`), so
the app treats both as "sign in again", and the README adds that a disabled
person who does is told why on Umbral's page, which closes the reading that
they could get back in. Link check: 187 links in 14 files, all resolve.
