# Captures for the design review of spec 017 (the trust setting on the OpenID Connect screens)

Taken 2026-11-13 against the Release build of `alta/chipper-duet-8b42` at eb9b894 (`dotnet build src/Umbral -c Release --no-restore`,
0 warnings, nothing under `src/` or `tests/` touched). Script: `capture.mjs` (Playwright, Chrome, everything through the
browser forms: fill and click, so the 422 pages are the real responses); its output is in `capture-output.log`.
Files are `<state>-<width>-<theme>.png`, widths 1280 and 360, themes dark and light, full page.
**No capture overflows horizontally** (41 shots, every one `scrollWidth == innerWidth`; the list is in `capture-output.log`).

## Instance

`dotnet bin/Release/net10.0/Umbral.dll` from `src/Umbral`, `ASPNETCORE_ENVIRONMENT=Development`,
`UMBRAL__INSECURE_ALLOW_HTTP=true`, `UMBRAL__PUBLIC_ORIGIN=http://127.0.0.1:18080`,
`UMBRAL__DATA_DIRECTORY=<fresh temp dir>`, `ASPNETCORE_URLS=http://127.0.0.1:18080`, and `HTTPS_PROXY=http://127.0.0.1:9` on the
Umbral process only. The setup token comes from the instance's log (the "Token" line of the first-run banner); the script
claims the instance through `/admin/setup` with it. The OpenID Connect issuers are `http://127.0.0.1:9/realm1`, `realm2`,
refused, so each connector is saved disabled (a "Disabled" badge on the list entry and a notice at first load: that is the
unreachable issuer, not part of this review). The instance is stopped and its data directory removed.

Two connectors exist for the pane states: `trusted` (Trusted IdP, "Trusted") and `untrusted` (Untrusted forge,
"Not trusted"). The instance list is `example.com` for `list-instance-off` and `pane-instance-off`, empty for the rest.
The count in `pane-confirm-off` is **0** (a fresh instance has no stored email address), so the n = 0 sentence is the one shown.
Clearing the instance list asks first ("Remove the list anyway"); the script confirms it.

## What is on screen

| State | On screen |
| --- | --- |
| `add-nochoice` | `/admin/connectors/new`, kind "OpenID Connect provider", fields filled; the group (legend, lead, two cards) with **neither card checked**, no error, then Allowed email domains. |
| `add-refused` | The same form submitted with no choice: 422, the form redrawn with the typed values (secret empty, "Type the client secret again" hint), neither card checked, "Choose whether to trust this provider's verified email addresses." under the group, in the error style, above the domains field. |
| `add-on` | The add form, "Trusted" card chosen, before submitting. |
| `add-off` | The add form, "Not trusted" card chosen, before submitting. |
| `add-discord-1280-light` | The add form with **Discord** selected as the kind: the group is drawn (neither checked). See the note below. |
| `pane` | The pane of the trusted connector: "Trusted" checked, the hint under the group ("A change applies from the next sign-in. …"), Set-up rule line "Who can sign in: anyone with an account". |
| `pane-ownlist-refused` | The pane of the trusted connector after typing `example.org`, choosing "Not trusted", saving: 422, typed values kept, "Not trusted" checked, the domains field in the error style with "Allowed email domains need verified addresses, and this provider's are not trusted. Trust them, or clear this list." under it. |
| `list-instance-off` | The Connectors list (at 360 the list alone; at 1280 with the Trusted IdP pane open on the right) with the instance list `example.com`: the "Untrusted forge" entry carries the warning triangle in place of the "Disabled" badge (`role=img`, `aria-label="Needs attention"`). The Trusted IdP entry keeps its badge. |
| `pane-instance-off` | The pane of Untrusted forge with the instance list set: a Warning alert under the heading ("Nobody can sign in through this provider. The email domains allowed for every connector need a verified address, and this provider's are not trusted. Trust them, or clear that list."), the Set-up rule line "Nobody can sign in through this provider: the email domains allowed for every connector need a verified address, and its addresses are not trusted.", "Not trusted" checked, the domains field's hint naming the instance list (example.com). |
| `pane-off-nolist` | The same pane with the instance list cleared: no alert, no marker on the entry, rule line "Who can sign in: anyone with an account at this provider. Each is a separate account, because its addresses are not trusted." |
| `pane-confirm-off` | The trusted connector's pane after choosing "Not trusted" and saving: 422, under the heading a Warning alert "Turning trust off makes no stored email address unverified. Nobody is unlinked.", then a Danger "Turn trust off" button and a "Cancel" link; the form below keeps "Not trusted" checked; the Set-up rule line still reflects the stored state ("anyone with an account"). |

## Notes for the designer

- **The group also draws for Discord, GitHub and Google.** On the add form the kind is a select and the fieldset is always
  there (`add-discord-1280-light.png`: Discord selected, group visible, neither card checked). Please rule whether it should
  hide for those three kinds (their emails are the provider's own verified ones; an unchecked group on a kind that does not
  use it reads as a required choice). Only the OpenID Connect add path asks for it, as far as these captures show: this walk
  did not submit a Discord add.
- At 360 the cards stack (one per row) and the list entry marker has no text, only the icon and its accessible name.
- Nothing looked broken: no clipping, no overflow, errors sit under the field they belong to in both themes.
- Not captured by the contract (so not here): the pane with a count above 0, and the saved-with-lowering notice.
