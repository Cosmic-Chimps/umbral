# Code review: free text in a security-log detail can't add fields (task d3tl)

Branch `alta/stoic-sinai-nsfx`, `8e263ae..9c8b1f5`. Reviewer: the `umbral` session, 2026-11-13.

## Verdict

**Clear to merge.** No blocking items; one optional nit.

## What was checked

- **`SecurityEvents.safeText`.** `;` becomes `,`, `=` becomes `:`, and control characters become a space. Past 300 characters it keeps 299 plus `…`, and 298 when position 298 holds the first half of a surrogate pair, so a pair is never split. It is a pure function with its cases in `SecurityEventsTests`.
- **Where it is used.** Every place that puts free text in a detail:
  - `ConnectorError.Detail` (`declined`, `rejected`, `unreachable`, `malformed_response`), the one place both Start and Callback go through;
  - the three `saved_disabled: <reason>` details in `Connectors.fs` (OIDC, Apple, Microsoft).

  `StartResolveTests` proves a cause with `;x=y` lands as one field.
- **The inventory is complete.** I searched the source myself for details that interpolate a reason, an error, a message, a detail or a cause without `safeText`, `safeDomain` or `cleanErrorWord`. The search found two, both in your "left alone" list and both safe:
  - `AppRedirectUris.fs` `uri=…`, through `Uri.EscapeDataString`;
  - `Callback.fs` `cause=…`, which is `StoreFailure.Cause`: five fixed words, with `Rejected`'s free-text reason kept out of the event by design.

  Leaving `AccountRefused` alone is right, because Umbral writes `tenant_not_allowed;tenant=<guid>` itself, with a canonical GUID.
- **The rule is written down** as point 3 of the comment at the top of `SecurityEvents.fs`, so the next detail follows it.
- **Tests run by the reviewer**, from a git checkout of `9c8b1f5`'s files: `dotnet build -c Release` with 0 warnings, `SecurityEventsTests` 24 of 24, `StartResolveTests` 4 of 4, `ProviderAdapterTests` 24 of 24, `SecurityEventCoverageTests` 8 of 8, and `changelog.sh check` in order. Your full run was 2753.

## Nit, optional

- **N1. The admin-save path has no test with `;x=y`** (the gap you named: the discovery stub's Down mode can't script the transport message). If you want it covered without changing the stub, build the three `saved_disabled` details with one small function (say `savedDisabledDetail store audience reason`) and test that function directly. Otherwise leave it: the three call sites are one-line uses of the tested `safeText`.
