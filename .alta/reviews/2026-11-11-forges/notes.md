# Signing in with GitLab, Forgejo (Codeberg) and Gitea through the OpenID Connect connector (task f0rg)

Run on 2026-11-13 from `alta/steady-eiger-d6kj` at `d4277e3` (main with s1ug merged), Release build of
`src/Umbral`, started as `dotnet Umbral.dll` with `ASPNETCORE_ENVIRONMENT=Development`,
`UMBRAL__INSECURE_ALLOW_HTTP=true`, `UMBRAL__PUBLIC_ORIGIN=http://127.0.0.1:18110` and a fresh data
directory. The three forges ran in Docker on this machine over plain http on loopback, which the
generic connector accepts only in that Development mode; nothing else about the connector was changed.
No code was changed by this task.

## Question

Does "Sign in with GitLab / Codeberg / Forgejo / Gitea" work through the generic OpenID Connect
connector as it is, and what does each forge send as `email` and `email_verified`?

## What ran

| Provider | Version | How | Account walks |
| --- | --- | --- | --- |
| gitlab.com | SaaS | discovery document only (no account on this machine) | none |
| codeberg.org | Forgejo (SaaS) | discovery document only (no account on this machine) | none |
| GitLab CE in Docker | 19.5.5 (`gitlab/gitlab-ce:latest`, arm64) | `external_url 'http://localhost:3003'` | yes |
| Forgejo in Docker | 14.0.5 (`codeberg.org/forgejo/forgejo:14`) | `ROOT_URL=http://localhost:3001/` | yes |
| Gitea in Docker | 1.26.4 (`gitea/gitea:1.26`) | `ROOT_URL=http://localhost:3002/` | yes |

**Not done: a sign-in on gitlab.com or codeberg.org itself.** Both need an account, and there is
none on this machine (no `glab`, no keychain entry). Their discovery documents were read, and the
same software was walked locally (GitLab CE is what gitlab.com runs, Forgejo is what Codeberg runs),
so the gap is the SaaS-only behaviour: registration rules, rate limits, a consent page that may
differ. The maintainer can close it with one sign-in each.

Scripts in this folder (none committed secrets: client credentials come from `/tmp` files, the
setup token from an argument):

- `direct.mjs`: an authorization-code sign-in straight against a forge (PKCE S256 and the client
  secret), printing the `id_token` header and payload and the userinfo answer.
- `signup.mjs`: self-registration through the forge's sign-up page.
- `umbral.mjs`: first-run setup of Umbral, the Forgejo and Gitea connectors added through the real
  add form, a public (`spa`) app with a loopback redirect, then four sign-ins through Umbral and a
  read of Umbral's own `id_token` and userinfo for each.
- `umbral-gitlab.mjs`: the same for the GitLab connector, on the running instance.
- `ui.mjs`: dumps the headings, labels and menu of each forge's application pages.
- `primary.mjs`: the re-check with confirmation on, up to adding an unconfirmed address (the
  Forgejo make-primary reproduction is held locally, outside the repo; see B3 above).

Playwright 1.64.1 with the installed Chrome; Node 22.23.4.

## Discovery documents

| | gitlab.com | codeberg.org | GitLab 19.5 (Docker) | Forgejo 14.0 | Gitea 1.26 |
| --- | --- | --- | --- | --- | --- |
| `issuer` | `https://gitlab.com` | `https://codeberg.org` | `http://localhost:3003` | `http://localhost:3001` | `http://localhost:3002` |
| trailing slash | no | no | no | no (even though `ROOT_URL` has one) | no (same) |
| discovery path | `/.well-known/openid-configuration` at the root | same | same | same | same |
| `id_token_signing_alg_values_supported` | `RS256` | `RS256` | `RS256` | `RS256` | `RS256` |
| `token_endpoint_auth_methods_supported` | `client_secret_basic`, `client_secret_post` | not listed | same as gitlab.com | not listed | not listed |
| `code_challenge_methods_supported` | `plain`, `S256` | `plain`, `S256` | same | same | same |
| `scopes_supported` | GitLab's API scopes plus `openid`, `profile`, `email` | `openid`, `profile`, `email`, `groups` | as gitlab.com | as codeberg.org | as codeberg.org |
| `claims_supported` has `email_verified` | yes | yes | yes | yes | yes |
| `userinfo_endpoint` | `/oauth/userinfo` | `/login/oauth/userinfo` | same | same | same |

Umbral's discovery check passed for all three local forges at Save: each connector was saved
**enabled** on the first try (`/admin/connectors/<slug>?msg=connector-added-oidc`). gitlab.com and
codeberg.org pass the same checks on paper: issuer equal to the address, https endpoints, `code`
among the response types, RS256, and (where listed) a client-secret method.

## Registering the client

Verified in the running products' UI (`ui.mjs`), November 2026:

| Forge | A user's own application | The instance's | Fields |
| --- | --- | --- | --- |
| GitLab 19.5 | **User settings → Applications → Add new application** (`/-/user_settings/applications`) | **Admin area → Applications → Add new application** (`/admin/applications/new`); a group can also own one under its **Settings → Applications** (not walked) | Name; Redirect URI; **Confidential** (ticked by default); Device authorization grant (leave off); Scopes: tick `openid`, `profile`, `email` only. Admin's form adds **Trusted** (skips the consent page; leave off unless wanted). Shows **Application ID** and **Secret**. |
| Forgejo 14.0 | **Settings → Applications → Manage OAuth2 applications** (`/user/settings/applications`) | **Site administration → Applications** (`/admin/applications`; the `/-/admin/...` path is Gitea's and answers 404 here) | Application name; Redirect URIs, one per line; **Confidential client** (tick). No scope list: the application gets the scopes the request asks for. Client ID and Client Secret shown once. |
| Gitea 1.26 | **Settings → Applications → Manage OAuth2 Applications** (`/user/settings/applications`) | **Site Administration → Applications** (`/-/admin/applications`) | Same as Forgejo. |

Through the API, for the walks: Forgejo and Gitea `POST /api/v1/user/applications/oauth2` with
`confidential_client: true`; GitLab `POST /api/v4/applications` (admin, instance-wide) with
`confidential=true` and `scopes=openid profile email`.

## Sign-ins, straight against each forge (`direct.mjs`)

PKCE S256 was sent together with the client secret (what Umbral does), the redirect URI was one of
the registered ones, and the consent page was accepted ("Authorize Application" on Forgejo and
Gitea, "Authorize" on GitLab).

**Forgejo 14.0, user `ana` (created by the admin, active):**

```
header  {"alg":"RS256","kid":"TGt3…","typ":"JWT"}
payload {"iss":"http://localhost:3001","sub":"1","aud":["bd0f5cc6-…"],"exp":…,"iat":…,"nonce":"…",
         "name":"ana","preferred_username":"ana","profile":"http://localhost:3001/ana",
         "picture":"http://localhost:3001/avatars/fcb7…","locale":"en-US","updated_at":…,
         "email":"ana@example.test","email_verified":true}
userinfo {"sub":"1","name":"ana","preferred_username":"ana","email":"ana@example.test","picture":"…"}
```

**Gitea 1.26, user `ana`:** identical shape (`"email_verified":true`, JSON boolean); userinfo adds
`"groups":null`.

**GitLab 19.5, user `ana` (created by the admin with `skip_confirmation=true`):**

```
payload {"sub_legacy":"948f…","name":"Ana","nickname":"ana","preferred_username":"ana","given_name":"Ana",
         "email":"ana@example.test","email_verified":true,"profile":"http://localhost:3003/ana",
         "picture":"https://www.gravatar.com/avatar/…","groups_direct":[],
         "iss":"http://localhost:3003","sub":"3","aud":"853f…","exp":…,"iat":…,"nonce":"…","auth_time":…}
userinfo the same claims plus "groups":[]
```

Observations that matter to Umbral:

- `email_verified` is the JSON literal `true` on all three, never the string `"true"`. Umbral's
  `isJsonTrue` accepts it, and the linking rule applies.
- `sub` is the account's numeric id as a string on all three (GitLab also sends `sub_legacy`, the old
  hashed form, which Umbral ignores). Renaming the account changes nothing.
- `aud` is an array on Forgejo and Gitea and a string on GitLab; the library accepts both.
- `name` is the full name on GitLab; Forgejo and Gitea send the username as `name` when the account
  has no full name (Umbral showed "ana" for them and "Ana" for GitLab).
- `email` and `email_verified` are in the `id_token` itself on all three, so Umbral's userinfo
  fallback was never needed. Note for the code reviewer: Forgejo's and Gitea's **userinfo answer has
  no `email_verified`**, so if a future version dropped the email from the `id_token`, Umbral would
  read the address from userinfo and treat it as unverified (the fallback takes the flag from the
  same source as the address). That is the safe direction.
- `picture` is the forge's avatar URL (gravatar on GitLab by default).

## What `email_verified` means on each forge

This is the finding that decides whether Umbral may pass the address on as verified.

### GitLab: the primary address is confirmed, and an unconfirmed one cannot authorize at all

GitLab's claim is `user.primary_email_verified?` when the `email` scope is granted
(`config/initializers/doorkeeper_openid_connect.rb`, read 2026-11-13). Tried: `mallory`, created
through the admin API with `skip_confirmation=false` (`confirmed_at: null`), on an instance whose
**Email confirmation settings** were `off` (the fresh 19.5 default, read from
`/api/v4/application/settings`). She could sign in to GitLab, but the authorization page answered
**"An error has occurred. Verify the email address in your account profile before you sign in."**
and no code was issued, neither in the direct walk nor through Umbral. So a token from GitLab 19.5
carries an address its holder confirmed, whatever the instance's confirmation setting. gitlab.com
runs the same code, so the refusal should hold there too (not tried; GitLab's profile docs mention a
three-day window in which an unconfirmed account can still sign in to GitLab itself, which the
refusal at the authorization page makes irrelevant to Umbral).

### Forgejo and Gitea: "the account is active", not "the address was confirmed"

In Gitea's source (`services/oauth2_provider/token.go`, read 2026-11-13) the claim is
`idToken.EmailVerified = user.IsActive`, marked `omitempty`, so it is `true` for every active account
and simply absent for an inactive one. Forgejo behaves the same (its file could not be fetched from
codeberg.org's raw endpoint, but every walk matched Gitea's output exactly).

Tried on both, with the images' defaults (registration open, `REGISTER_EMAIL_CONFIRM` off because
no mailer is configured):

1. `mallory` self-registered through the sign-up page with `someone-else@example.test`, an address
   nobody confirmed ("Account was successfully created. Welcome!"). Her `id_token` carried
   `"email":"someone-else@example.test","email_verified":true` on **both** forges, and Umbral
   created a user with that address marked verified (Users screen: "mallory someone-else@example.test").
   Had a user with that verified address existed, Umbral would have linked her to it: that is the
   rule, and the page's email warning is about exactly this.
2. `bob` deactivated by the admin (`PATCH /api/v1/admin/users/bob {"active":false}`): Forgejo shows
   **"Account is suspended"** and Gitea **"Sign-In Prohibited"** at the authorization page; no code is
   issued, Umbral sees nothing. So an inactive account never reaches Umbral and the absent claim is
   never observed in practice.

Consequence for the documentation, before the re-check below: a Forgejo or Gitea whose registration
is open **and** does not confirm addresses hands out `email_verified: true` for any address anyone
types. Codeberg's own docs say a new account must confirm its address by clicking the link in the
email before it is usable (not tried with an account; see the gap above).

### Re-check for the documentation review (B1): confirmation on, and an existing account

The review asked whether an **active** account can carry an address it never confirmed, which is
the question that matters when `email_verified` is "the account is active". Both forges were
re-created with `[service] REGISTER_EMAIL_CONFIRM = true` and a dummy `[mailer]` (checked in the
effective `app.ini`):

1. **Self-registration** (`mallory`): the account is created inactive; at the authorization page
   both forges show **"Activate your account"** and issue nothing. Confirmation on means sign-up no
   longer lets anyone claim an address.
2. **An active account adds an address** (`ana` adds a second address on **Settings → Account**):
   the list shows it as **Requires activation**, and neither forge's UI offers to make an
   unactivated address primary. Ana's token still carried her original address.
3. **Gitea 1.26.4** kept the primary address confirmed under that setting. **Forgejo 14.0.5** could
   not be shown to: an existing account was able to end up with a primary address it never
   confirmed, after which Forgejo's own API reported that address as primary and unverified and the
   next `id_token` and userinfo presented it as `email_verified: true`.

So on Forgejo 14.0.5, confirmation being on is not enough: an existing account can present Umbral an
address it never proved it can read. Gitea did not show that.

> **Held out of the repository on the documentation reviewer's advice (B3).** The exact way the
> Forgejo result was produced, and the script that does it, are **not** in this branch: this
> repository is public, and this looks like an unreported Forgejo bug. A search on 2026-11-13 found
> only a related, already-fixed Forgejo issue (primary-email validation bypass, v11.0.4 / v12.0.2,
> forgejo/security-announcements #39), not this one. The reproduction is kept locally (outside the
> repo) for a private report to Forgejo's security contact, which is **the maintainer's to make**;
> it was not reported from here, and the method should come back into the notes and the walkthrough
> only once a Forgejo release names it fixed.

## Through Umbral (`umbral.mjs`, `umbral-gitlab.mjs`)

Connectors `forgejo`, `gitea` and `gitlab` added through the add form with the local issuers; all three
saved enabled. A public app "Forge test" (`spa`, redirect `http://127.0.0.1:3999/cb`) and an
authorization request with PKCE; the sign-in page offered **Sign in with Forgejo**, **Sign in with
Gitea** and **Sign in with GitLab** (`signin-three-forges-1280-light.png`).

| Walk | Forge account | Umbral's `id_token` | Umbral user |
| --- | --- | --- | --- |
| Forgejo, ana | active, `ana@example.test` | `email`, `"email_verified":true`, `idp: oidc`, `name: ana` | new user `9myGUpiQn43yWmbZnXOwdg` |
| Gitea, ana | active, same address | same | **the same user**: linked by the verified address (Users screen lists Forgejo, Gitea) |
| GitLab, ana | confirmed, same address | same, `name: Ana` | **the same user** (Users screen lists Forgejo, Gitea, GitLab) |
| Gitea, mallory | unconfirmed address | `"email_verified":true` | new user, address stored as verified |
| Forgejo, bob | suspended | none: Forgejo refused before the callback | none |
| GitLab, mallory | unconfirmed | none: GitLab refused before the callback | none |

No `sign_in_failed` rows in Umbral's log for these walks; the refused ones never came back to Umbral.

## Redirect URI and PKCE

Nothing special. All three compare the redirect URI exactly against the registered list (Forgejo and
Gitea take one per line, GitLab one per line too) and all three accepted S256 alongside the client
secret; none required or refused it. The callback is the one Umbral shows,
`<UMBRAL__PUBLIC_ORIGIN>/connect/<identifier>/callback`.

## Verdict

- **All three work through the generic connector unchanged.** No connector, no code change, no
  small fix to propose. The Forgejo make-primary finding (re-check above) is the forge's, not
  Umbral's; the documentation says what it means for an operator.
- **Documentation:** rows in the issuer table, a section "A public forge: GitLab, Codeberg, or one
  you run" in `docs/connectors/oidc.md`, the README's connector list, a `changelog.d` file. No page
  per forge: the steps differ only in menu names and in what `email_verified` means.
- **For the maintainer:** one real sign-in on gitlab.com and one on codeberg.org would close the SaaS
  gap; the local walks make a different outcome unlikely.


## How to re-run

```sh
docker run -d --name f0rg-forgejo -p 3001:3000 -e FORGEJO__security__INSTALL_LOCK=true \
  -e FORGEJO__server__ROOT_URL=http://localhost:3001/ -e FORGEJO__server__DOMAIN=localhost codeberg.org/forgejo/forgejo:14
docker run -d --name f0rg-gitea -p 3002:3000 -e GITEA__security__INSTALL_LOCK=true \
  -e GITEA__server__ROOT_URL=http://localhost:3002/ -e GITEA__server__DOMAIN=localhost gitea/gitea:1.26
docker run -d --name f0rg-gitlab -p 3003:3003 --shm-size 256m -e GITLAB_OMNIBUS_CONFIG="external_url 'http://localhost:3003'; \
  gitlab_rails['initial_root_password']='<password>'; puma['worker_processes']=0; sidekiq['concurrency']=5; \
  prometheus_monitoring['enable']=false; gitlab_rails['gitlab_email_enabled']=false;" gitlab/gitlab-ce:latest   # ~4 min to boot
# users: docker exec -u git f0rg-forgejo forgejo admin user create --admin --username ana --password … --email ana@example.test --must-change-password=false
#        (gitea: the same with "gitea"); GitLab users and the application through /api/v4 with a PAT made by gitlab-rails runner
# applications: POST /api/v1/user/applications/oauth2 (Forgejo, Gitea), POST /api/v4/applications (GitLab), saved as /tmp/f0rg-<forge>-app.json
# Umbral: dotnet build src/Umbral -c Release, then from bin/Release/net10.0:
#   ASPNETCORE_ENVIRONMENT=Development UMBRAL__INSECURE_ALLOW_HTTP=true UMBRAL__PUBLIC_ORIGIN=http://127.0.0.1:18110 \
#   UMBRAL__DATA_DIRECTORY=/tmp/f0rg-data ASPNETCORE_URLS=http://127.0.0.1:18110 dotnet Umbral.dll
# then: node umbral.mjs http://127.0.0.1:18110 <setup token> <out>; node umbral-gitlab.mjs http://127.0.0.1:18110 - <out>
```

## Clean-up

Containers `f0rg-forgejo`, `f0rg-gitea` and `f0rg-gitlab` and the Umbral instance were stopped and
removed at the end of the task; the images (Forgejo 188 MB, Gitea 177 MB, GitLab 4 GB) were left.
