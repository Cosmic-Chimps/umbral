# Design review: discovery reasons in one level of brackets

Task fl4t. Branch `alta/tranquil-grove-gqx6`, `4677442`. Reviewed 2026-11-13 by the design
reviewer from one capture (`oidc-added-unreachable-360-light`) and the implementer's account
of the change. The Microsoft captures were not opened; their wording is the one ruled on
before the task started.

## Verdict

**Clear to merge. No blocking items, no nits.**

- The reason is flat where it is built, and the notice around it holds one level of brackets:
  "Umbral could not reach http://127.0.0.1:9/realm4 (the connection failed: Connection
  refused, 127.0.0.1:9)." reads in one pass and wraps without overflow at 360.
- **The case the task did not foresee**: changing the two generic OpenID Connect notices from
  "could not reach <issuer>: <reason>." to "could not reach <issuer> (<reason>)." is right.
  With a flat reason the old form put two colons in one sentence, and the bracketed form now
  matches Apple's and Microsoft's notices. Updating the spec 002 contract with it keeps that
  contract true.
