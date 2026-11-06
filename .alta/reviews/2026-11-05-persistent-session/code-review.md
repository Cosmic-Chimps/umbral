# Code review: the end-user session cookie is persistent (task c5hn)

Commit `409f97b` on `alta/faithful-clove-gx3r`, stacked on the native-apps page. Reviewer: the `umbral` session, 2026-11-05, at the merge into `main`, since the task reached the maintainer's merge before a review was asked for.

## Verdict

**Clear.** No blocking items, no nits.

## What was checked

- **The change.** `Sessions.signIn` signs in with `AuthenticationProperties(IsPersistent = true)`; nothing else in the sign-in changes. With `SlidingExpiration` and the 14-day `ExpireTimeSpan` the ticket already had, the cookie now carries an expiry 14 days out and is renewed on a request past half of it, which is what the tests read off the raw `Set-Cookie` headers (expiry at sign-in, renewal after 8 days, refusal after 14 unused).
- **What bounds a stolen or shared cookie.** The flags are unchanged and right: `HttpOnly`, `SameSite=Lax` (the provider's redirect back needs it), `Secure` whenever the request is https, which `UseForwardedHeaders` makes true behind a TLS proxy. The ticket's lifetime is what it was; what changes is only that the browser keeps the cookie across a restart.
- **Ending a session early stays where it was.** `Authorize.fs` re-checks the user against the database on every authorization (FR-022) and signs the cookie out for a disabled or deleted user (`:262`, `:277`, `:318`, `:325`), and `/connect/logout` deletes it; both are tested (`a disabled person's persistent session is deleted at the next authorization`, `signing out deletes the persistent session cookie`).
- **Scope.** The operator's admin cookie is another scheme and is untouched.
- **Docs.** `docs/native-apps.md` "Staying signed in" and the README's "Signing out" say the new behaviour, including the shared-computer consequence, and `changelog.d/persistent-session.md` is a `Changed` note that names it and the one-time note that an existing session keeps its old cookie until the next sign-in.
- **Tests:** the reviewer's full run on `409f97b` is recorded below.
