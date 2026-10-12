# Contract: the secret store seam (internal)

**Feature**: `005-external-secret-stores` | FR-007 to FR-015, FR-025

Not an operator surface. Written down because the four adapters, the cache,
the move command and the tests all code against it, and because tests replace
it.

## `ISecretStore` (in `Infrastructure/Secrets.fs`)

```text
Id     : string                                    // "local" | "aws" | "gcp" | "azure" | "bellabaxter"
Put    : SecretName * plaintext * CancellationToken -> Task<Result<SecretRef, StoreFailure>>
Get    : SecretRef  * CancellationToken            -> Task<Result<string, StoreFailure>>
Delete : SecretRef  * CancellationToken            -> Task<Result<unit, StoreFailure>>
Probe  : CancellationToken                         -> Task<Result<unit, StoreFailure>>
```

Every implementation MUST:

- Never throw for a vendor error: map it to `StoreFailure` (research R3, R4).
  Only a programming error (a reference with another store's id) may throw.
- `Put` with a name that already exists in that store adds a new version and
  returns the same reference. It is safe to repeat.
- `Delete` of something already gone returns `Ok`.
- Never log, format or include the plaintext in an exception or a failure
  reason.
- Honour the token; the caller links it to the 10 s bound.
- Accept a transport seam (`HttpMessageHandler`, SDK transport or endpoint) and
  a static credential, so tests need no account (FR-025).

`local`: `Put` ignores the name; `Delete` and `Probe` are `Ok`; a decryption
failure is `NotFound` with reason "key ring".

## `SecretStores` (registry)

```text
Active     : ISecretStore
For        : storeId -> Result<ISecretStore, StoreFailure>   // NotConfigured when it cannot be built
Read       : SecretRef * CancellationToken -> Task<Result<string, StoreFailure>>   // through the cache
Evict      : SecretRef -> unit
CanRead    : storeId -> bool                                // no network call; for the card warning
```

Registered with `TryAddSingleton`, so a test registers its own (an in-memory
store under any id, one that fails on the Nth call) before `configureServices`
runs (`Fixtures.fs`). External stores are built on first use.

## What callers may rely on

| Caller | Uses | Relies on |
| --- | --- | --- |
| Admin create / replace | `Active.Put`, then `Evict` | `Put` idempotent per name; failure leaves nothing referenced |
| Admin delete | `For(ref.Store).Delete`, `Evict` | `Ok` when already gone |
| Callback | `Read` | Cache semantics of [data-model.md](../data-model.md#cache-entry) |
| Move command | `For(source).Get`, `Active.Put`, `Active.Get`, `Evict`, `For(source).Delete` | All of the above |
| Startup | `Active.Probe` | `NotFound` from a read of the probe name means healthy |
