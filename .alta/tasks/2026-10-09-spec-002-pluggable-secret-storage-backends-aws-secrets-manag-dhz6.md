---
title: "Spec 002: pluggable secret storage backends (AWS Secrets Manager, Azure Key Vault, BellaBaxter)"
kind: improvement
status: later
created: 2026-10-09
summary: "Let an operator keep connector and app secrets in an external secret manager instead of the local encrypted store, with Umbral writing and reading them at runtime and holding only a reference. Deferred until the MVP runs; the ISecretStore seam lands in T025 so this stays additive."
---

## Why

Decided with the project owner on 2026-10-09, deferred until the MVP is running.

Umbral currently stores every connector client secret encrypted at rest with
ASP.NET Core Data Protection, in the SQLite file (`research.md` D6). That is the
right default for the target audience — one container, `cp` to back up, no
external dependency. It is not what a user with a compliance team, a Vault
install, or a BellaBaxter org wants.

**The shape matters and is easy to get wrong.** This is *not* an
`IConfiguration` provider in the style of `FrunchyBackend`'s `addBellaSecrets`,
which reads once at startup. Umbral drives the store at runtime, read **and**
write: the operator pastes a secret into the Connectors form, Umbral writes it
to the external manager and keeps only a reference in SQLite, then reads it back
on each sign-in.

The Keycloak precedent, from `research.md` D1's survey, is the reason the seam
goes in now rather than later: they shipped secrets-readable-in-database and
bolted a Vault SPI on years afterwards, which is why that feature is awkward and
has had gaps. Retrofitting the abstraction after deployments exist means a data
migration for every one of them.

## What to do

Write `specs/002-.../spec.md` through the spec-kit pipeline. It should cover:

- **Backends**: AWS Secrets Manager, Azure Key Vault, BellaBaxter. The local
  encrypted store stays the default and must remain fully supported.
- **Selection**: per instance or per connector? Config-file and admin-UI parity
  is required by Principle V either way.
- **Migration both ways**: moving an existing instance's secrets into a manager,
  and back out of it. Neither direction can lose a secret.
- **The failure mode that needs deciding rather than discovering**: if the store
  is unreachable, nobody can sign in anywhere. A product pitched as "one
  container on a Pi" would then depend on a network service during every login.
  Caching the decrypted secret is the obvious fix, but it puts the material back
  on the Pi and partly defeats the point. Cache TTL, behaviour on a failed
  fetch, and whether a cached secret survives a restart are all real decisions.
- **Principle II**: this does not breach it as long as the default stays
  self-contained — the constitution forbids a *required* runtime dependency
  outside the container, not an optional one. Say so explicitly in the spec so a
  later reader does not have to re-derive it.
- **Plaintext storage** (in YAML or SQLite) was raised and I recommended against
  it: a literal secret in the config file is currently a validation *error*, and
  that rule is what keeps secrets out of the operator's git repository. `${VAR}`
  already covers the docker-compose and Kubernetes cases ergonomically. If it is
  wanted anyway, it should be a dev-only flag that logs a warning, not a
  supported storage mode. The spec should record whichever way this is settled.

## Where

- Seam: `src/Umbral/Infrastructure/Secrets.fs` — `ISecretStore` (Put/Get/Delete
  over a `SecretRef`), with `LocalSecretStore` as the only implementation in the
  MVP.
- Schema: `connectors.client_secret_store` + `client_secret_locator` in
  `src/Umbral/Infrastructure/Database.fs`, replacing a bare ciphertext column so
  an external locator fits without a migration.
- Existing decisions this builds on: `specs/001-umbral-mvp/research.md` D6,
  `specs/001-umbral-mvp/data-model.md` (the `connectors` table),
  `specs/001-umbral-mvp/contracts/admin-and-config.md` (FR-028, the `${VAR}` rule).

Note: app client secrets are **out of scope** — OpenIddict hashes those with
PBKDF2 and `GetClientSecretAsync` returns the hash, so there is no plaintext for
any store to hold. This is about connector secrets and the operator credential.
