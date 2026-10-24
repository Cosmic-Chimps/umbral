# T9: a vision document that is one — notes for the documentation review

Task: `.alta/tasks/2026-10-23-docs-t9-vision-document-v9sn.md`, audit
`docs/audit-2026-10.md` section 5, T9 (findings W24, S6). Branch
`alta/lithe-camellia-sgxm`, based on `main` at `1f3a89e`.

## Files

- `docs/vision.md`: rewritten as a one-page statement (what Umbral is, who it is
  for, what it aims for, what it will not do, what could go wrong, how the scope
  changed).
- `docs/history/2026-vision-conversation.md` (new, `git mv` of the old
  `docs/vision.md`): the old text unchanged, except a first line saying what it
  is and two relative links fixed for the new folder (`../specs/` to
  `../../specs/`).

The audit's recommendation was taken: the rewrite, with the conversation kept
in history.

## Acceptance

- `grep -n -i "hodor\|yaml\|understand it well\|Do you want" docs/vision.md`
  finds nothing. The old file's "Hodor" stays in the history file, which says
  it was the working title.
- Links to `docs/vision.md`: the README has none (`grep -n vision README.md`
  finds nothing; the task's "Why" says it links to it, which is not so). The
  constitution names the path in its Sync Impact comment (line 64) and cites
  "the vision" in Principle VII's rationale (line 232: "The vision names scope
  creep as the highest-likelihood risk"). The path is kept, and the new text
  names scope creep as the largest risk, so both still hold. `PRODUCT.md:173`
  lists the path; it still exists.
- `.github/scripts/check-links.py docs/vision.md
  docs/history/2026-vision-conversation.md README.md`: 74 links in 3 files,
  all resolve. A full run fails only on `docs/connectors/oidc.md:180`
  (`README.md#configuration-as-code`), the W15 link left for T1, which this
  change does not touch.

## Behavioural statements and what they were checked against

| Statement in `docs/vision.md` | Checked against |
| --- | --- |
| Self-hosted OIDC provider whose only job is passwordless sign-in, social provider or passkey | Constitution preamble, lines 73-77; README lines 1-9 |
| Apps use any standard OIDC library | Constitution III (discovery document, line 127-129); `src/Umbral/Infrastructure/OpenIddictSetup.fs:279` |
| Some people will not use Google, GitHub or Discord; the passkey connector is for them | Constitution Sync Impact, lines 13-23; `src/Umbral/Domains/Connectors.fs:32` (`Passkey` kind) |
| One image, embedded SQLite, no other service | Constitution II; `src/Umbral/Umbral.fsproj:182-183` (EF Core SQLite); `src/Umbral/Infrastructure/DataDirectory.fs:28` (`umbral.db` in the data directory) |
| Runs on a 64-bit Raspberry Pi | `.github/workflows/release.yml:244` (`linux/amd64,linux/arm64`); README "Requirements" |
| Idles under 150 MB (160 MB with an external secret store) | `tests/Umbral.Tests/IdleMemoryTests.fs:31-36`; README lines 59-60 |
| All state is one data directory; backup is a copy | Constitution II, last bullet; `src/Umbral/Infrastructure/DataDirectory.fs:19-28` |
| Three concepts; three admin screens | Constitution I; `src/Umbral/Views/Layout.fs:52-54` (Apps, Connectors, Users) |
| Three values per app: issuer URL, client ID, client secret | Constitution III, line 132-133; README "Integrate an app" (lines 309-310) |
| First sign-in within five minutes, counting pasting credentials | Constitution II, lines 107-108 (an aim, stated as one) |
| Secure without a hardening guide | Constitution IV, lines 145-146 (an aim) |
| A feature is not done until documented; every operator error must say what to do next | Constitution VI, lines 190-195 (stated as a rule, "must", since T6 is still open) |
| No realms, tenants, groups, roles, permissions, SAML, LDAP, MFA, local passwords, user federation, flow builder | Constitution I line 86-87 and VII lines 207-210 |
| A passkey is neither MFA nor a password account; never a second step | Constitution VII lines 211-214 |
| Umbral does not send email; a passkey invitation is a link the operator hands out | Constitution VII lines 227-228; no mail package in `src/Umbral/Umbral.fsproj`; `src/Umbral/Domains/Passkeys.fs:24-25,69` (the invitation link and its lifetime) |
| Email-domain limits on verified addresses, disabling a user, open sign-up versus invitations: in scope; per-app access rules: not | Constitution VII lines 220-228; `src/Umbral/Domains/EmailDomains.fs:166-172` (unverified refused); `src/Umbral/Handlers/Admin/UserToggle.fs:66,104`; `src/Umbral/Domains/Passkeys.fs:18-21` (`invitations`, `open`) |
| Keycloak and Authentik for what is excluded | Constitution VII lines 215-217; README "What Umbral is not" |
| Scope creep is the largest risk; Principle VII makes "no" the default | Constitution VII rationale, line 232; old vision's risk table ("High") |
| Each connector has a walkthrough and is tested without real credentials | Constitution VI line 192 and testing discipline lines 278-279; `src/Umbral/Domains/Connectors.fs:22` (`Fake` provider); `docs/connectors/` |
| 1.0.0 (2026-10-09): social login only, admin area or an equivalent configuration file | `git show b269ea9:.specify/memory/constitution.md` lines 122-125 (Principle V "Config-as-Code Parity") |
| 2.0.0 (2026-10-10): file dropped, spec 004 | Constitution V (removed), lines 172-180; commit `34b86df` |
| 2.1.0 (2026-10-15): email-domain limits in scope, spec 006 | `git show 4f017a1` Sync Impact (2.0.0 → 2.1.0); constitution VII lines 220-224 |
| 3.0.0 (2026-10-19): passkeys, spec 008; MFA and passwords stay out | Constitution Sync Impact lines 4-10; version line 308 |

The "who it is for" paragraph (homelab, Raspberry Pi and five hobby apps,
indie developers, small communities) comes from the constitution's rationale
for Principle II (line 116) and the old vision's "User" row; it describes an
audience, not behaviour.

## How it was verified

The link check above, the acceptance grep, and a read of each statement against
the file and line in the table. No code changed; no tests run.

## CHANGELOG

None: no behaviour changes (as the task says).
