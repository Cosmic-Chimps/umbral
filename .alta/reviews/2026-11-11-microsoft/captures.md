# Captures for the design review of spec 016 (Microsoft connector), task T008

Taken 2026-11-13 against the Release build of `alta/warm-monsoon-8gdd` at `07b350e` (`dotnet build src/Umbral -c Release`,
0 warnings; the working tree had uncommitted edits to `PRODUCT.md`, `docs/`, `specs/` and `changelog.d/` that are not
part of the build). Script: `capture.mjs` (Playwright, Chrome); its output is in `capture-output.log`. Files are
`<state>-<width>-<theme>.png`, widths 1280 and 360, themes dark and light, unless the table says otherwise. No product
code was changed. Overflow is `scrollWidth > innerWidth`, read on the page right before each shot:
**no capture overflows horizontally** (64 shots, every one `scrollWidth == innerWidth`).

## Instance

| Instance | Origin | Flags | Used for |
| --- | --- | --- | --- |
| A | `http://127.0.0.1:18096` | `ASPNETCORE_ENVIRONMENT=Development`, `UMBRAL__INSECURE_ALLOW_HTTP=true`, `UMBRAL__PUBLIC_ORIGIN`, `UMBRAL__DATA_DIRECTORY=/tmp/ms-a` (fresh), started with `dotnet Umbral.dll` so `launchSettings` did not apply | everything below |

First-run setup went through `/admin/setup` with the token from the start banner (`Token` line, not the EF SQL lines).
The instance is stopped (no `Umbral.dll` or `capture.mjs` process is left). It was started twice: the first walk had
a bug in my script (the `notice-tenant-unknown` shots were taken without reloading after the theme change, so the
"dark" ones were light); I fixed the script, wiped the data directory and the images and ran the whole walk again.
Every image here is from the second run.

**Microsoft's real discovery document was fetched** on each save (the machine has network). With a made-up client ID
and secret every save for `consumers`, `tenant` (the GUID `72f988bf-86f1-41af-91ab-2d7cd011db47`), `organizations` and
`common` came back `302 … ?msg=connector-saved` and the pane showed **Enabled**. A GUID that names no tenant
(`11111111-2222-4333-8444-555555555555`) came back `…?msg=connector-saved-microsoft-unreachable` (see "What looked off", 1).

## Captures

| State | What it shows | Overflow |
| --- | --- | --- |
| `connectors-list` | The Connectors list on a fresh instance: Discord, GitHub, Google, Apple, **Microsoft** (Not set up), Fediverse, Bluesky, Passkey. Master-detail: Microsoft's pane is the detail | no |
| `pane-not-set-up` | The pane before saving: Not set up pill, Set-up note, redirect URI with Copy (`http://127.0.0.1:18096/connect/microsoft/callback`), the secret-expiry caveat, "Who can sign in: anyone with an account", Settings: display name "Microsoft", empty client ID, empty client secret, four stacked cards with **none checked** (0 of 4), empty tenant field with hint, shared box ticked, allowed domains, Save connector | no |
| `card-focus` (1280 only, dark and light, viewport shot not full page) | Tab from the secret field lands on the first card: the card gets a visible accent outline (yellow in dark). Radio value `consumers` was the focused element | no |
| `refused-no-choice` | 422, valid client ID and secret, no card: one message under the group, all four radios `aria-invalid=true` and `aria-describedby="audience-error"` | no |
| `refused-tenant-missing` | `tenant` chosen, field empty: "Enter the Directory (tenant) ID of the organization." under the tenant field | no |
| `refused-tenant-not-guid` | `contoso.onmicrosoft.com`: "That is not a tenant ID. It is a GUID like 00000000-0000-0000-0000-000000000000." | no |
| `refused-tenant-personal` | `9188040d-6c67-4c5b-b112-36a304b66dad`: "That is the tenant of personal Microsoft accounts: choose Personal Microsoft accounts instead." The card stays on "one organization", the typed value is kept | no |
| `refused-several` | Client ID `not-a-guid` and no card, at once: a message under the client ID (field border red) and one under the group. The typed client ID is kept, the secret box is empty | no |
| `pane-set-up` | Saved with Personal Microsoft accounts: Shared, "Kept in this instance (encrypted)" and Enabled pills, the `microsoft` slug, masked secret `••••••••6789`, "Replace the client secret" field with its hint, Disable and Delete. No banner (the shot is a fresh load of the pane) | no |
| `card-personal` | Same saved pane, Personal Microsoft accounts checked | no |
| `card-one` | Work or school accounts from one organization checked, tenant field holding `72f988bf-86f1-41af-91ab-2d7cd011db47` in mono. At 360 the field sits under four stacked cards and its value is clipped at the right edge of the field (`72f988bf-86f1-41af-91ab-2d7cd011db4…`, last characters out of sight) | no |
| `card-any` | Any work or school account checked, tenant field empty | no |
| `card-both` | Personal and any work or school account checked, tenant field empty | no |
| `notice-tenant-unknown` | Saved with `tenant` and a GUID that names no tenant: the warning banner "Saved, but not enabled: Umbral could not reach Microsoft (it answered with an error instead of a discovery document). Press Enable to try again." over the pane; extra to the list, see 1 | no |
| `connectors-list-set-up` | The list after the connector is saved and enabled (extra): Microsoft first with an Enabled pill and its slug, the others below it | no |
| `picker` | An app (Wiki) offering Fediverse, GitHub and **Sign in with Microsoft**, in that order; all three buttons 278x44 at 360 | no |
| `picker-branded` (1280) | Acme Notes with accent `#b4532a` and a logo: all three buttons take the accent | no |

Also read in the walk (not captured): at 1280 the first `Tab` from the secret field focuses the first card, and the
radios are `INPUT audience=consumers`; on `refused-no-choice` all 4 radios point at the one `#audience-error`
paragraph, whose text is the message (so the refusal under the group is announced with the group); the tenant
refusals set `aria-describedby="tenantId-hint tenantId-error"` on the tenant input, and the client ID refusal
`clientId-hint clientId-error`. The secret box was empty in every 422 and after every save.

## Strings as shown (all as in the contract)

The four card titles and descriptions, the three tenant messages, the group message, the client ID message, the
secret-expiry caveat, the naming advice, the display name hint, the client ID hint, the secret hint, the replace-secret
hint and the tenant hint came out word for word as in `contracts/routes-and-screens.md`. The unreachable notice has
this reason text: "it answered with an error instead of a discovery document".

## Not captured

- `connector-saved-microsoft-wrong`, `connector-enable-microsoft-wrong` and `connector-enable-microsoft-unreachable`
  notices: the only failure I could produce was the unknown-tenant one, and it came back as the **unreachable** kind
  (1 below). I did not try to make Microsoft's answer unusable another way, and I did not press Enable on a failed
  state.
- The "Saved, but not enabled" notice on a **new** connector (first save with an unknown tenant). The notice was
  shot over a connector that was already enabled.
- The refusal of the display name, the secret (missing on a new connector) and the allowed domains; the secret and
  display-name messages were not asked for, and the domain messages are the other panes'.
- The sign-in page after pressing Microsoft (a real Microsoft sign-in needs a registered app).

## What looked off, or worth a second look

None breaks the page; they are for the reviewers to weigh. Nothing was fixed.

1. **A failed check leaves the pane saying Enabled.** After the unknown-tenant save the banner says "Saved, but not
   enabled … Press Enable to try again", but the header pill, the list entry and the list's "1 connector" all say
   **Enabled**, and the buttons at the bottom are **Disable** and Delete: there is no Enable button to press
   (`notice-tenant-unknown-*`; the button list in `capture-output.log` is `Save connector, Disable, Delete`). The
   connector was already enabled from the earlier saves, so this may be the state of an already-enabled connector
   whose new setting could not be checked, and not what a first save does. I did not try the first-save case. Either
   way the banner and the pill contradict each other on screen.
2. **Unknown tenant reads as "unreachable".** A GUID that names no tenant makes Microsoft answer with an error, and
   the contract lists that as the **wrong** case ("Check the tenant ID, then press Enable"). The page showed the
   **unreachable** wording ("could not reach Microsoft … Press Enable to try again"), whose advice is to retry, not to
   check the tenant ID. (The reason in parentheses is "it answered with an error instead of a discovery document".)
3. **The tenant field is emptied when another card is saved.** The contract says the tenant ID is "kept when another
   card is chosen, and ignored then". On a refused redraw it is kept (`refused-tenant-personal`). After a successful
   save with `organizations` or `common` the field is empty again (`card-any`, `card-both`), because the stored
   connector holds no tenant. Switching back to "one organization" means typing the GUID again.
4. **360, card-one:** the tenant GUID (36 characters, mono) is cut off at the right of its field; the client ID field
   cuts its last characters the same way. The value is complete in the input; only the last characters are out of sight.
5. **Link icon for Microsoft, as for Fediverse** (`picker-*`): two of the three buttons carry the same link icon, so
   the icon does not tell them apart; the title does. This is the contract's answer 5, noted only because the picker
   now shows it next to GitHub's own mark.
6. **Saved connector moves to the top of the list** (`connectors-list-set-up`), above Discord and the others. That
   is how the list already behaves for every kind, so the Microsoft entry "after Apple" holds only while it is
   Not set up.
7. The branded picker's logo is dark text on a dark card: that is my 240x80 canvas, as in the Bluesky walk, not a
   product problem.
8. **Keyboard and screen reader:** focus on a card is visible (`card-focus`). The refusal under the group is
   announced through `aria-describedby` on every card and `aria-invalid` on all four; I read it from the DOM, I did
   not listen to it with a screen reader.

## Retake (2026-11-13, after `1572212`)

Retake of the states that changed in `84cdaed` and `1572212` (save rule for a failed check, `invalid_tenant`, the two
endings for an unconfirmed choice, the rule line naming the choice). No product code changed; nothing was fixed.

**Build.** `dotnet build src/Umbral -c Release` at `1572212`, 0 warnings, 0 errors (clean tree). Script: `retake.mjs`
(Playwright, Chrome; helpers copied from `capture.mjs`); output in `retake-output-a.log` and `retake-output-b.log`.

**Instances.** Both are `dotnet Umbral.dll` from `bin/Release/net10.0` with `ASPNETCORE_ENVIRONMENT=Development`,
`UMBRAL__INSECURE_ALLOW_HTTP=true`, `UMBRAL__PUBLIC_ORIGIN=http://127.0.0.1:18097` and the **same fresh data directory**
`/tmp/ms-b`.

| Instance | Network | Walk |
| --- | --- | --- |
| A | normal | first-run setup, `rule-line-saved`, `rule-line-list`, `refused-tenant-unknown`; ends with a saved "one organization" connector (tenant `72f988bf-86f1-41af-91ab-2d7cd011db47`, Microsoft's own, confirmed by Microsoft) |
| B | `HTTPS_PROXY=https_proxy=http://127.0.0.1:9`, restarted on the same data, sign in again | `unchanged-unchecked`, `refused-unanswered` |

**The proxy works.** The Microsoft adapter's `HttpClientHandler` leaves `UseProxy` on (only the Fediverse client turns
it off), so .NET reads `HTTPS_PROXY`; with port 9 closed every check to `login.microsoftonline.com` fails at once with
"the connection failed (Connection refused (127.0.0.1:9))". The proxy variable was set on the Umbral process only, not on
Chrome (the browser talks to `127.0.0.1`).

### States

| State | Files | What it shows | Overflow |
| --- | --- | --- | --- |
| `rule-line-saved` | 1280 and 360, dark and light | Saved personal-accounts connector, Enabled; Set-up section line "Who can sign in: personal Microsoft accounts, with any email domain." (read from the DOM too) | no |
| `rule-line-list` | 1280 dark | Same connector with allowed domains `example.org`: "Who can sign in: personal Microsoft accounts, with a verified email at example.org (this connector's list)" wrapping to two lines at 1280 | no |
| `unchanged-unchecked` | 1280 and 360, dark and light | Enabled "one organization" connector saved again unchanged with Microsoft unreachable: 302 `?msg=connector-saved-microsoft-unchecked`, warning banner "Saved. Umbral could not check the connector with Microsoft (the connection failed (Connection refused (127.0.0.1:9))), so it stays enabled as it was." The pill stays Enabled, buttons are Save, Disable, Delete; the rule line reads "work or school accounts from one organization, with any email domain" | no |
| `refused-tenant-unknown` | 1280 and 360, dark and light | Enabled personal connector changed to "one organization" with a random GUID (`42369be3-232f-4fc1-b968-0e92f01d3bc1`), Microsoft answers `invalid_tenant`: **422**, under the tenant field "Microsoft did not confirm this choice: No Microsoft tenant has this ID. Nothing was saved: check the tenant ID, then try again." Only the tenant input is `aria-invalid` (`aria-describedby="tenantId-hint tenantId-error"`), its border is red, the card and the typed GUID are kept. Reloading the pane shows the stored choice unchanged (personal, Enabled) | no |
| `refused-unanswered` | 1280 dark, 360 light | Enabled "one organization" connector changed to personal while Microsoft is unreachable: **422**, under the group "Microsoft did not answer (the connection failed (Connection refused (127.0.0.1:9))). Nothing was saved: try again in a moment." All four radios `aria-invalid` and `aria-describedby="audience-error"`. Reloading the pane shows "one organization" and the GUID still stored | no |

**Overflow.** 15 shots (`rule-line-saved` 4, `rule-line-list` 1, `refused-tenant-unknown` 4, `unchanged-unchecked` 4,
`refused-unanswered` 2), every one `scrollWidth == innerWidth`: **no capture overflows horizontally**.

### What looked off

None breaks the page; for the reviewers to weigh.

1. **Rule line without a final full stop for a list.** "…with a verified email at example.org (this connector's list)"
   ends without a period, while the no-rule line ends with one ("…with any email domain."). It is the same as the old
   plain connector line for lists, but the two sit side by side now (`rule-line-list`, `rule-line-saved`).
2. **Set-up line and the card disagree after a 422.** On `refused-tenant-unknown` the card says "one organization" (the
   typed draft) while the Set-up line above it still says "personal Microsoft accounts" (the stored choice). It is
   correct (nothing was saved, and the message says so) but it is two answers on one screen. Same on
   `refused-unanswered` (card: personal, line: one organization).
3. **Nested parentheses in a transport reason.** "(the connection failed (Connection refused (127.0.0.1:9)))" has three
   levels inside the notice's own parentheses, in the unchecked notice and in the unanswered refusal. It is the
   exception text, so a real outage will read the same way (for example a timeout or a DNS name); only the wording of
   the frame could soften it.
4. **Unanswered refusal is far from the changed card.** At 360 the group message sits under the fourth card, below
   the card that was changed (first one, at the top of the group), with the tenant field after it. It reads as the
   group's message, which it is; no visual link to the card that changed beyond the checked state.
5. **360, tenant field:** the GUID is still cut off at the right edge of the field (as in the first round, item 4),
   including in the red-bordered state of `refused-tenant-unknown-360-*`.
6. The unchecked notice is the same warning style and position as the first round's banners; the pill, the list entry
   and the buttons agree with it this time (Enabled, Disable). The first round's item 1 (banner against pill) no longer
   applies to this state.

### Not taken, and why

- Nothing in the list was skipped. `refused-unanswered` was taken only at 1280 dark and 360 light, as asked.
- The "wrong" kinds (`connector-saved-microsoft-wrong` and the enable notices), the first-save-with-failed-check
  notice and "Saved, but not enabled" on a new connector were not in this retake's list and were not taken.
- A real Microsoft refusal of a *choice* other than the tenant (`MicrosoftNotConfirmedChoice`, "check that the choice
  matches the app's registration") was not taken: it needs a real registration whose audience differs from the choice,
  and the made-up client ID cannot produce it.
- Both instances are stopped (no `Umbral.dll` or `retake.mjs` process left); `/tmp/ms-b` and the two logs
  `/tmp/ms-b-a.log`, `/tmp/ms-b-b.log` remain outside the repository.
