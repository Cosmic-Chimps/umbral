# Microsoft accounts connector: step 1, the premise (task m5ft)

Run on 2026-11-12 from `alta/warm-monsoon-8gdd` at `a82d8ec` (main with spec 015
merged).

## Question

Can the generic OpenID Connect connector (specs/002) already sign in personal
Microsoft accounts and "any work or school account"? If so, the task shrinks to a
walkthrough and stops for the maintainer's call.

## How it was tried

Umbral's own discovery check, `OidcDiscovery.Check`, the same call the admin
form's Save and Enable make, was run from an F# script against the built
`Umbral.dll` (`/tmp/msprobe/probe.fsx`, not committed), with the four issuer
addresses an operator would type:

```fsharp
let d = OidcDiscovery(new HttpClient(), false)
for t in [ "consumers"; "common"; "organizations"; "9188040d-6c67-4c5b-b112-36a304b66dad" ] do
    d.Check($"https://login.microsoftonline.com/{t}/v2.0").Result
```

No app registration was needed to reach the verdict: the refusal comes at Save,
before any sign-in.

## What happened

| Issuer typed | Umbral's answer |
| --- | --- |
| `https://login.microsoftonline.com/consumers/v2.0` | **Refused**: "That address describes itself as `https://login.microsoftonline.com/9188040d-6c67-4c5b-b112-36a304b66dad/v2.0`, not `…/consumers/v2.0`." |
| `https://login.microsoftonline.com/common/v2.0` | **Refused**: "That address describes itself as `https://login.microsoftonline.com/{tenantid}/v2.0`, not `…/common/v2.0`." |
| `https://login.microsoftonline.com/organizations/v2.0` | **Refused**: the same template issuer. |
| `https://login.microsoftonline.com/9188040d-6c67-4c5b-b112-36a304b66dad/v2.0` | Accepted at Save (the document names itself). |

Microsoft's documents, fetched the same day with `curl`:

| Endpoint | `issuer` in its discovery document | Keys at its `jwks_uri` (`issuer` property of each key) |
| --- | --- | --- |
| `common` | `https://login.microsoftonline.com/{tenantid}/v2.0` | 6 × template, 2 × `…/9188040d-…/v2.0` |
| `organizations` | template | 6 × template |
| `consumers` | `…/9188040d-6c67-4c5b-b112-36a304b66dad/v2.0` | 6 × template, 2 × `…/9188040d-…/v2.0` |
| one tenant (a GUID) | `…/<that GUID>/v2.0` | 6 × `…/<that GUID>/v2.0` |

None of the documents lists `code_challenge_methods_supported`; all list
`client_secret_basic` and `client_secret_post`; `claims_supported` has `tid` and
`email` but no `email_verified`.

The personal-accounts tenant ID `9188040d-6c67-4c5b-b112-36a304b66dad` is
current: Microsoft's ID token claims reference (`iss` and `tid`, read
2026-11-12) gives it, and the `consumers` document names it as its issuer.

## Verdict: the generic connector refuses them. Continue.

- **Personal accounts through `consumers`, and "both" through `common`: refused.**
  Discovery's mix-up defence (OIDC Discovery §4.3, `OidcDiscovery.fs` line 102)
  requires the document's issuer to equal the address typed. That is correct
  behaviour for a generic client and must not be loosened.
- **Any work or school account (`organizations`): refused**, for the same reason.
  There is no address an operator could type that makes it work: the template
  issuer is not a real issuer, and no token carries it.
- **One tenant: works today**, as `docs/connectors/oidc.md` says. Stays on the
  generic connector.

### The one way round it, and why it is not the answer

Typing the personal-accounts tenant's own issuer,
`https://login.microsoftonline.com/9188040d-6c67-4c5b-b112-36a304b66dad/v2.0`,
passes Save. Whether Microsoft then lets a personal account sign in at that
tenant's authorize endpoint was **not** tried: it needs an app registration and a
personal account, which this unattended run does not have. Microsoft's access
token page calls `consumers` "a nickname for the tenant 9188040d-…", so it may
well work. Even if it does, it is not this task's answer:

- it covers personal accounts only, never "any work or school account" or "both";
- nothing in Umbral would explain it to an operator, who would type `consumers`
  as Microsoft's own pages say and be refused;
- the generic connector keys people on `sub`, which Microsoft makes pairwise per
  app registration: replacing the registration (a lost secret, a move to another
  tenant) would quietly turn every person into a new user;
- the generic connector never raises `email_verified` for Microsoft (it is not in
  Microsoft's tokens), which is safe, but has no way to use the signal Microsoft
  does document (`xms_edov`).

A live sign-in with a real registration is owed to the maintainer, for the new
connector rather than for this workaround (see the spec's "Still open").
