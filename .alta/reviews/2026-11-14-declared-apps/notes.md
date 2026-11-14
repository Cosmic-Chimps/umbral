# Spec 019 walk (T005) and captures (T007)

Branch `alta/twilight-fornax-23bd`, head `1911d94`, 2026-11-14. Nothing under `src/`, `tests/`, `docs/` or `samples/` was edited; nothing committed.

## Bug found: `umbral.css` is stale at 1911d94 (blocks `docker build`)

`docker build -t ghcr.io/cosmic-chimps/umbral:dev .` from the worktree fails at the Dockerfile's
`src/Umbral/Assets/build-css.sh --check`: "umbral.css is stale". Rebuilding the CSS in a scratch copy adds
exactly two rules that the committed file lacks, `.um-link` and `.um-link:hover` (the "How declared apps
work" link of the Declared note, per `contracts/screens.md`). Fix: run `src/Umbral/Assets/build-css.sh` and
commit `umbral.css`. Without it, no image of this branch builds, and the CI image job and the release would fail.

Workaround for the walk only: `rsync` of the worktree (without `.git`, `.alta`, bin/obj) to `/tmp/umbral-build`,
`build-css.sh` there, `docker build` there. Image `ghcr.io/cosmic-chimps/umbral:dev`, id
`sha256:f66b728807aa30a7174cb229986c151e8da1f73dc2c24921be33f68ef99eeb41`. Apart from `umbral.css`, the code is
the worktree's.

## Setup

User secrets of `samples/aspire` before the walk: `Umbral:Port=18080`, `Sample:Port=5001`, plus the **old
manual-steps leftovers `Parameters:umbral-client-id` and `Parameters:umbral-client-secret`** (values not printed;
unused by the new AppHost, which uses `Parameters:umbral-sample-client-secret`, generated at the first run).
Set by me: `Umbral:ImageTag=dev` (still set; `Sample:Port` was changed to 5002 in step 5 and set back to 5001).

## Steps

Every run: `aspire run --non-interactive --nologo` as a CodeAlta job, stopped with `alta job cancel`.
Umbral's database was copied with `docker cp` (db, wal, shm) after each run.

### 2. Adopt (C2) on the old volume

Backup first: volume `umbral-aspire-data-backup-r5hd` (still there). The old volume held the passkey connector
(shared, `enrolment=open`) and the old manual app `7QAdslGc4OoPDRmF`, set up in the 2026-11-14 18:4x walk.
Start line:

> Declaration applied: 1 app created, 0 updated, 0 unchanged, 0 no longer declared; passkey connector adopted.

Events added: `6|connector_updated|system||passkey|declared;adopted`, `7|app_created|system|sample||declared`.
`declared_items`: `connector|passkey|1`, `app|sample|1`. The old app stays an ordinary app, as D5 says.
Sign-up through the sample at `http://localhost:5001/private` with a new virtual passkey ("Adopt Person"):
`/private` shows h1 Private, name, idp=passkey. Capture `captures/step2-private-adopt.png`.
(The adopt run's Umbral log said "No migrations were applied", which is true: the schema tables were
already there from the earlier dev-image walk of the same branch. Not looked into further.)

### 3. Fresh volume

`umbral-aspire-data` removed (backup existed, no container used it), `aspire run`, nothing clicked in the admin
area. Start line:

> Declaration applied: 1 app created, 0 updated, 0 unchanged, 0 no longer declared; passkey connector created.

Events: `1|connector_created|system||passkey|declared;enrolment=open`, `2|app_created|system|sample||declared`;
then the sign-up: `3|user_created`, `4|passkey_registered|via=signup`. The sign-up through the sample ("Fresh
Person") reached `/private`. Capture `captures/step3-private-fresh.png`. Signing in needed no claim (D10).

### 4. Second run, same volume

> Declaration applied: 0 apps created, 0 updated, 1 unchanged, 0 no longer declared; passkey connector unchanged.

No new security event (still ids 1 to 4). `OpenIddictApplications where ClientId='sample'` = 1, all apps = 1,
`declared_items` = 2 rows.

### 5. Changed redirect URI

`Sample:Port=5002`: start line "0 apps created, 1 updated, 0 unchanged, 0 no longer declared; passkey connector
unchanged." Exactly one new event, `5|app_updated|system|sample||declared;fields=redirect_uris,post_logout_redirect_uris`,
`declared_items.app sample` updated_at moved. The sample at `http://localhost:5002` signed a new person in ("Port
Person", `captures/step5-private-5002.png`). Set back to 5001: the final run again logged "1 updated" and
recorded the same `app_updated` event (id 8), as expected.

### 6. Secrets (the generated one, 48 characters, `Parameters:umbral-sample-client-secret`)

| Where | Occurrences |
| --- | --- |
| `docker logs` of Umbral (final run) | 0 |
| Umbral's database copy (db + wal) | 0 |
| Job output of the five `aspire run` jobs | 0 each (the output is only the CLI's short summary, about 640 characters) |
| `docker inspect` of Umbral's container | 1 (the `UMBRAL__APP_SECRETS__sample` env var, expected) |
| `git grep -F` in the repository | 0 files |
| Whole worktree files except bin/obj/.git, so also the captures and scripts | 0 files |

Not checked: the sample project's own console log under `~/.aspire/logs` (1.8 GB, not grepped as instructed).

### 7. Captures

Final run of the fresh instance, claimed through `/admin/setup` with the setup token; an ordinary app "Internal tool"
(`tIfAujjffl9tE4jv`, created through the form) and one invitation on the passkey pane (note "For the review"),
both through the real forms. All files in `captures/`, `<state>-<width>-<theme>.png`, widths 1280 and 360, themes
dark and light (cookie `umbral_theme`), full page. None of the 36 pane and list captures overflows horizontally
(`scrollWidth` = `innerWidth`).

| State | Files |
| --- | --- |
| `apps-list` (declared and ordinary) | 4 |
| `app-declared-pane` | 4 |
| `app-ordinary-pane` (unchanged look, with Save, Remove, Regenerate, Delete) | 4 |
| `passkey-declared-pane` (with the open invitation) | 4 |
| `refusal-app-declared` | 4 |
| `refusal-connector-declared` | 4 |
| `app-undeclared-pane` | 4 |
| `passkey-undeclared-pane` | 4 |
| `apps-list-undeclared` | 4 |
| `step2-…`, `step3-…`, `step5-…` (sample's `/private`) | 3 |

Refusals, real POSTs with the antiforgery token and an Origin header, `maxRedirects: 0`:

- `POST /admin/apps/sample/redirect-uris` -> `/admin/apps/sample?msg=app-declared`
- `POST /admin/connectors/passkey/toggle` -> `/admin/connectors/passkey?msg=connector-declared`
- `POST /admin/apps/sample/delete` -> `/admin/apps/sample?msg=app-declared` (the app is still there)
- `POST /admin/apps/sample/secret` -> `/admin/apps/sample?msg=app-declared`

The notice pages are loaded from those redirect targets. In the declared app pane the note, the two warnings
and the "Web application · Declared" list line are as `contracts/screens.md` says; the pane has no Save,
no Regenerate/Delete row and no secret reveal.

**Pane of an app no longer declared.** `samples/aspire` has no way to leave out the declaration, so the volume was
copied to a scratch volume (`umbral-declared-scratch`) and the same image run with `docker run`, with no
`UMBRAL__DECLARATION` (D3). Log: "0 apps created, 0 updated, 0 unchanged, 1 no longer declared; passkey connector
no longer declared." Events: `app_updated|system|sample||undeclared`, `connector_updated|system||passkey|undeclared`;
`declared_items.declared` = 0 for both. The panes have no pill, no note and the full forms back; the word
"Declared" is nowhere on the pages. The scratch container and volume were removed after the capture, so the
fresh volume `umbral-aspire-data` still has both items declared.

## Not done / loose ends

- A shared user-secrets leftover: `Parameters:umbral-client-id` and `-secret` (old steps) remain; not mine to delete.
- The walk's operator credential is only in the fresh volume (never in a file).
- The sample project's own console log was not scanned for the secret.

## State left behind

Volumes: `umbral-aspire-data` (fresh, claimed, declared), `umbral-aspire-data-backup-r5hd` (the old setup before
adopt), and `umbral-data` (not mine). No job running, no Umbral container running. `/tmp/umbral-build` (the scratch
copy), `/tmp/w` (database copies, hold no secret except password hashes of this walk) remain.
Scripts here: `signup.mjs`, `capture.mjs`, `capture-undeclared.mjs`; Playwright from `/tmp/r3du-pw`.
