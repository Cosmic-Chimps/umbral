# Code review: the FastAPI sample (task f4pi)

Branch `alta/lush-dolphin-6rrb`, `dad2b15..94ee708` (the sample is `533903b`). Reviewer: the `umbral` session, 2026-11-07. I read every file and ran the harness myself.

## Verdict

**Clear to merge.** No blocking items; one nit, and a judgement on the two things the implementer left as is.

## What was checked, against the task's care points

- **PKCE.** `code_challenge_method: "S256"` in `client_kwargs` (`main.py`), which is what makes Authlib send a challenge: it sends none unless the method is named. Umbral refuses an authorization request without PKCE, so the harness walk passing is itself the proof that it is sent.
- **`state` and `nonce`.** Left to Authlib and never turned off: `authorize_redirect` stores both (with the verifier) in the session, and `authorize_access_token` checks `state` and the `id_token`'s `nonce`. A failure is caught as `OAuthError` and drawn as a 400 page with the error code escaped.
- **The session cookie.** Starlette's `SessionMiddleware`: `SameSite=Lax`, `HttpOnly` (Starlette's own), `Secure` always except for an `http` issuer on a loopback host (`plain_http_on_loopback`, one condition computed once), its key from `SESSION_SECRET` or random per start, no fixed default. It is signed, not encrypted, and the README says what that means: it holds only the person's own claims, their `id_token`, and during a sign-in Authlib's `state`, `nonce` and verifier, which are theirs to see.
- **No open redirect.** `back_to` is set only from the app's own paths (`"/"`, `"/private"`), never from the query; the session is cleared at the callback, so nothing of a previous sign-in survives.
- **Markup.** Every claim value goes through `html.escape` (non-strings through `json.dumps` first); titles are fixed strings.
- **Sign-out.** `id_token_hint` and the post-logout URI built from `url_for`, through the `end_session_endpoint` from discovery; a POST, as the contract asks.
- **No secret in the repo or the logs.** The client secret comes from the environment only; no default key; nothing logs the token or the secret.
- **Container and pins.** `python:3.13-slim`, a system user with no home (`USER sample`), nothing written at run time, so no bind-mount ownership trap like the one fixed in `dad2b15`. `requirements.txt` pins every package, transitive ones included, made from a clean `pip freeze`.
- **Behind a proxy.** `proxy_headers=True`, and the README's note on `FORWARDED_ALLOW_IPS` is what keeps `url_for` producing the registered `https` redirect URI behind a TLS-terminating proxy.
- **The harness.** `samples/test/run.sh fastapi` at `94ee708`, with `umbral:samples` rebuilt from that commit: setup and the walk both passed (`/`, `/public`, `/private` to Umbral, the fake connector, the claims with no `email` row, Sign out, `/private` back to Umbral).

## The two things left as is

- **uvicorn's access log shows the callback's `code` and `state`.** Acceptable. `state` is not a secret, and the code is single-use, redeemed within the same request, and worthless without both the PKCE verifier (in the person's own cookie) and the client secret. Turning the access log off would cost a reader the one log line that shows the flow working. Say so in one line (N1), so nobody reads it as an oversight.
- **Authlib 1.9.5's deprecation warning about httpx at start.** Acceptable: it is the library's, not the sample's, and pinning an older httpx to silence it would be worse. No change.

## Nit

- **N1.** Add one sentence to the README's "How it works" (or a comment beside `uvicorn.run`): the access log shows the callback's `code` and `state`; the code is single-use, already redeemed, and useless without the verifier and the secret, so it is left in.

## Taken (2026-11-07)

The implementer's session had ended, so the reviewer took N1 and committed this file: the README's "How it works" gains **The access log** bullet. Clear to merge.
