# T1: remove what no longer exists, fix the contradictions — notes for the documentation review

Task: `.alta/tasks/2026-10-23-docs-t1-remove-what-no-longer-exists-t1rm.md`,
audit `docs/audit-2026-10.md` section 5, T1 (findings W5, W6, W7, W13, W15,
W16, W17, W18, W23, W25). Branch `alta/festive-marble-n7qj`, based on `main`
at `1f3a89e` (T8 merged, `b4a07c6` is an ancestor).

## Files

- `docs/connectors/oidc.md`, `docs/connectors/github.md`, `docs/connectors/google.md`
- `README.md`, `PRODUCT.md`, `docker-compose.yml`
- `.github/workflows/ci.yml` (job `links`)

No CHANGELOG entry: doc-only fixes, no statement people rely on changed
meaning (the removed text described features that do not exist).

## Each finding and what changed

| Finding | Change |
| --- | --- |
| W5, three screens or four | One wording in all three places: "three screens, Apps, Connectors and Users, and the Credential page". README:167, README:415, PRODUCT.md:103, and also PRODUCT.md:14, which the audit did not list but said "four operator screens". |
| W6, Credential "in the top bar" | README:159: "in the side rail". |
| W7, schema warning twice | Both paragraphs removed from "Where it is kept, and upgrading" (branding). One paragraph, version 6, in a new `### Upgrading` at the end of "Your data: back it up" (README:1128). The branding section now says an Umbral from before branding upgrades by itself with no page change, and links to `#upgrading`. "schema version" occurs once in the README. |
| W13, `specs/001-umbral-mvp/` | README:1301: "its specifications are in [`specs/`](specs/), one folder per feature." |
| W15, "Keeping it in the configuration file" | Subsection deleted whole (YAML, `extra_scopes`, `GET /admin/config`, the `#configuration-as-code` link). "The issuer and the identifier cannot be changed" follows "If the provider cannot be reached" directly. |
| W16, Extra scopes | The **Extra scopes** row of the add-form table deleted; an **Allowed email domains** row added after **Shared with every app**, in form order. The step-3 Scopes row no longer says "add it as an extra scope in step 4": it says these three are the only scopes and there is no setting to ask for more. |
| W17, reserved identifiers | oidc.md:49: "the name of a kind of connector (`discord`, `github`, `google`, `passkey`, `oidc`, or `fake`, the test connector)". |
| W18, "of the three providers" | github.md:3-4: "the easiest provider to register an app with" (passkeys need no app at a provider, so plain "easiest" would not hold). google.md:3-4: "the most demanding provider to set up". |
| W23, "unreleased" | PRODUCT.md:107: the Last used badge "(since 0.3.0)". |
| W25, "No image is published yet" | docker-compose.yml:28: "Compose builds the image from this checkout." |

## Behavioural statements kept or written, and the code

- **Add-form table = the form.** `Handlers/Admin/Connectors.fs:544-599`, in
  order: `kind` select (label `ConnectorAddKindLabel` "Provider", option
  `ConnectorAddOidcOption` "OpenID Connect provider", `Texts.fs:962,966`),
  `slug` ("Identifier", `:963`), `displayName` ("Button label", `:924`),
  `issuer` ("Issuer URL", `:967`), `clientId` ("Client ID", `:901`),
  `clientSecret` ("Client secret", `:902`), `sharedBox` ("Shared with every
  app", `:926`), `connectorDomainsField` ("Allowed email domains", `:935`).
  Every row of the table is one of these; no other field is named.
- **Allowed email domains row.** Placeholder `acme.com, example.org`
  (`Texts.fs:936`); "only people whose verified email is at one of these
  domains can sign in through this connector. Leave empty to use the instance
  list" is `ConnectorDomainsHelp` (`Texts.fs:944-946`); "separated by commas"
  as in `AllowedDomainsNotADomain` (`Texts.fs:941`). Links to the page's own
  `## Allowed email domains`.
- **Scopes are fixed.** `Domains/Connectors.fs:139` (`Oidc` endpoints,
  `openid email profile`), `ScopeString` at `:210-211` reads only those; the
  add handler (`Connectors.fs:1062-1067`) reads no scope field.
- **Reserved identifiers.** `Connectors.fs:1081`, `reserved = (parseKind
  state.Slug).IsSome`; `parseKind` (`Domains/Connectors.fs:57-65`) knows
  `discord`, `github`, `google`, `fake`, `oidc`, `passkey`. The refusal is
  `ConnectorAddOidcSlugTaken` (`Texts.fs:984-985`).
- **Side rail.** `Views/Layout.fs:52-55`: Apps, Connectors, Users,
  Credential, then Sign out.
- **Upgrading paragraph.** `Infrastructure/Database.fs:246` `SchemaVersion =
  6`; upgrade steps run by themselves at start (`:349-354`, `:392-397`);
  version 4 adds `app_branding` only (`:265`), version 6 adds a column with a
  default and the passkey tables, no connector row (`:294`), so the passkey
  connector stays off until enabled. The refusal on a newer database and its
  quoted end are `Database.fs:385-390`, verbatim.
- **Last used since 0.3.0.** `CHANGELOG.md:35-39`.
- **specs/.** Ten folders, `001-umbral-mvp` to `009-last-used-connector`
  (two numbered 006), so "one folder per feature" and no count.

## The audit's acceptance

```
$ grep -rn "admin/config\|extra_scopes\|Extra scopes\|configuration-as-code" docs README.md | cut -d: -f1,2
docs/audit-2026-10.md:40
docs/audit-2026-10.md:164
docs/audit-2026-10.md:165
docs/audit-2026-10.md:166
docs/audit-2026-10.md:169
docs/audit-2026-10.md:537
docs/audit-2026-10.md:550
```

Every hit is the audit itself quoting the findings (it landed under `docs/`
after it was written); none is in a walkthrough or the README. I left the
audit untouched: it is the reviewer's file.

- `grep -c "schema version" README.md` → `1`.
- "four screens" / "four operator" appear nowhere in README.md or PRODUCT.md;
  "three screens" at README:167, :415 and PRODUCT.md:103.

## Link check, before and after

Before, on `main` at `1f3a89e`:

```
$ python3 .github/scripts/check-links.py; echo exit=$?
docs/connectors/oidc.md:180: ../../README.md#configuration-as-code: no heading for #configuration-as-code in README.md
exit=1
```

After, on this branch:

```
$ python3 .github/scripts/check-links.py; echo exit=$?
check-links: 155 links in 12 files, all resolve
exit=0
```

## `continue-on-error` removed, and the step can fail

`.github/workflows/ci.yml`, job `links`: the `continue-on-error: true` line
and the three comment lines above it ("T8 lands before T1 ... T1 fixes it and
removes this line") are deleted. `grep -n continue-on-error
.github/workflows/ci.yml` prints nothing. `actionlint` (via `uvx --from
actionlint-py actionlint`) on `ci.yml` and `release.yml`: clean.

Failing-run proof: a throwaway copy of this working tree (`rsync` without
`.git`, in a `mktemp -d` folder, deleted afterwards) with the W15 dead anchor
added back at the end of `docs/connectors/oidc.md`:

```
$ printf '\nSee the [configuration file](../../README.md#configuration-as-code).\n' >> docs/connectors/oidc.md
$ python3 .github/scripts/check-links.py; echo "exit=$?"
docs/connectors/oidc.md:381: ../../README.md#configuration-as-code: no heading for #configuration-as-code in README.md
exit=1
```

With the step no longer marked `continue-on-error`, that exit 1 fails the
`links` job and the run.

**Not done: a CI run on the branch.** The branch is local; pushing it is the
maintainer's call. The job is one `run:` of the script verified above, so the
first push or PR run shows it green.

## After the review (bafa55f: clear, three nits)

All three nits taken, no re-check asked for:

- oidc.md:49-51: `fake` is now "which Umbral's own tests use".
- README:531: the branding heading is now `### Where it is kept`. No link
  pointed at its old anchor (`check-links.py`: 155 links in 12 files, all
  resolve).
- README:1131: the Upgrading paragraph's two cases read "for example", so the
  sentence still holds after the next schema step.
