# Quickstart: Proving Allowed Email Domains

## Automated

```bash
dotnet test -c Release
```

The feature's own tests:

| Test file | Proves |
| --- | --- |
| `EmailDomainsTests.fs` | parsing, normalisation, IDN, wildcard refusal, `applying`, `check` matrix (SC-001) |
| `AllowedDomainsSignInTests.fs` | Story 1 and 2 through the callback with the fake connector: refusal page text (SC-004), no user row, one event (SC-005); Story 3: authorize re-check, refresh refused, pre-feature session and token |
| `ProviderAdapterTests.fs` | `hd` present for Google with one domain, absent otherwise (Story 5) |
| `AllowedDomainsAdminTests.fs` | instance card save, invalid entry, removal warning and confirm; connector field, card label, unshare keeps the list; Apps warning (Stories 2, 4) |
| `DatabaseTests.fs` | a version 3 database upgrades to 4 with every connector's list empty |
| `SecurityEventCoverageTests.fs` | `allowed_domains_changed` is emitted |

SC-006 is the rest of the suite passing unchanged.

## By hand (development instance)

1. Run with `ASPNETCORE_ENVIRONMENT=Development` and
   `UMBRAL__ALLOW_FAKE_CONNECTOR=true`; add a fake connector and an app.
2. On Connectors, set the fake connector's list to `acme.com`.
3. Sign in to the app as `ann@acme.com`, verified: you get in.
4. Sign in as `bob@gmail.com`, verified: the refusal page, no user on the Users
   screen, a `sign_in_failed` row with `reason=domain_not_allowed` in
   `security_events`.
5. Sign in as `carol@acme.com`, not verified: the same page,
   `reason=email_not_verified`.
6. Clear the connector's list; set the instance card to `acme.com`; repeat 3–4.
7. Empty the instance card: the warning names the fake connector; confirm.
8. Sign in as `bob@gmail.com` (now allowed). Set the connector's list to
   `acme.com` again, then open the app again: the existing session is dropped
   and you are back at the sign-in page.
9. Add a second, open connector; set an app to offer both: the Apps screen
   warns that the open one lets anyone in.
