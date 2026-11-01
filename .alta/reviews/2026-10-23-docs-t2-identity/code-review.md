# Code review: docs audit T2, "Integrate an app" and "How people are identified" (task t2id)

Branch `docs/t2-identity`, `3a235da..f11e915`. Reviewer: the `umbral` session, 2026-11-01.

## Verdict

**Clear to merge**, with one merge-time step (M1) and one note (N1) handed to another task. Neither needs a re-check.

## What was checked

- **No `src/` change.** Tests and docs only.
- **`OidcProtocolTests.fs`:**
  - `authorizationRedirectAs` generalises the fake sign-in. The old `authorizationRedirect` keeps its exact fields, so existing tests are unchanged.
  - The new test, "a provider user with an unverified email gets email_verified false, and the fallback name", covers both the id_token and userinfo.
  - It asserts the email, `email_verified` as a JSON `false`, the fallback name, `idp`, and the same `sub` in both.
  - It pins the identity claim set. userinfo's whole shape is pinned, and the id_token's identity claims are pinned with the protocol claims filtered out.
  - So adding, dropping or retyping a promised claim fails, as the audit asked.
- **`PasskeyInvitationTests.fs`:** "a passkey-only user's tokens carry no email" goes through the real passkey sign-in and code exchange, and checks that neither token has `email` or `email_verified` even when `email` is asked for.
- **The docs.** The statements I spot-checked against the code hold:
  - claims and scopes;
  - an unverified email passed on with `email_verified: false`;
  - the first email kept;
  - linking across every connector kind by a verified email;
  - the endpoints, sign-out, and disabled or deleted users.

  `umbral-documentation` reviews the docs in full.
- **Tests run by the reviewer:** a full `dotnet test` at `f11e915`: 1786 passed, 0 failed, 0 skipped.

## Before merging

- **M1. Changelog format.** This branch adds its bullet under `## Unreleased`. If the `changelog.d/` port (`alta/zippy-kenya-3qj9`, task cl6d) is on `main` when this merges, as planned, move the bullet into `changelog.d/docs-t2-identity.md` under `### Fixed` before merging, or CI's `changelog.sh check` fails.

## Note, for another task

- **N1. A security finding, found here and filed separately.** While writing this, you found and reproduced a flaw: `UserStore.resolve` raises `email_verified` for a stored address no provider verified, which allows pre-account hijacking through email linking. It's filed as `2026-11-01-raise-email-verified-only-when-the-provider-verifies-the-sto-h762`. I confirmed it against the code (the linking query reads `users.email` with `users.email_verified`). It's in every release since v0.1.0 and is now the first task in the queue. That task also rewrites this branch's README sentence "`email_verified` can go from `false` to `true`, never back", which describes today's behaviour accurately, so nothing changes here. Thank you for stopping to reproduce it rather than only noting it.
