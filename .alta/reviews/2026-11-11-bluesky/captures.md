# Captures for the design review of spec 015 (Bluesky connector)

Taken 2026-11-12 against the Release build of `alta/snowy-eagle-vdg4` at a5eb911, with `capture.mjs`
(Playwright, Chrome). No product code was changed. Files are `<state>-<width>-<theme>.png`, widths 1280
and 360, themes dark and light, unless the table says otherwise. Overflow is `scrollWidth > innerWidth`
read on the page right before each shot: **no capture overflows horizontally** (the numbers are in
`capture.mjs`'s output; every one was `scrollWidth == innerWidth`).

## Instances

| Instance | Origin | Flags | Used for |
| --- | --- | --- | --- |
| A | `http://127.0.0.1:18093` | Development, `INSECURE_ALLOW_HTTP=true` | pane, picker, step. The pane shows the **development note** in place of the metadata address (expected). |
| B | `http://localhost:18094` | same | `connectors-origin-blocked` |
| C | `https://auth.example.org` (self-signed cert on 127.0.0.1:18095; Chrome `--host-resolver-rules="MAP auth.example.org 127.0.0.1:18095"`, `ignoreHTTPSErrors`) | Production, no dev flags | `connectors-any-https`: it worked at the first try, with a real https origin |

All three instances are stopped.

## Captures

| State | What it shows | Overflow |
| --- | --- | --- |
| `connectors-off` | Pane not set up: lead, development note, no-email note, Settings (shared box, scope cards, empty Servers field), **Enable Bluesky**; no closing row | no |
| `connectors-any` | Enabled, Any server; **Save connector**, Disable, Delete | no |
| `connectors-list` | Enabled, Only these servers, `bsky.social, pds.example.org` | no |
| `connectors-list-refused` | The 422 page for `*.bsky.social`: the typed value kept, the refusal under the field, the radio left on Only these servers | no |
| `connectors-origin-blocked` | Instance B: heading, "Not set up", the lead, the Warning alert; no Settings form, no closing row | no |
| `connectors-any-https` (1280 dark only) | Instance C: the client metadata address with Copy, hint, no development note | no |
| `picker` | An app (Wiki) offering Bluesky, Fediverse and GitHub, side by side, identifier order | no |
| `picker-branded` (1280) | Acme Notes with an accent colour (`#b4532a`) and a logo | no |
| `step` | Empty step: title, Handle field with placeholder, hint, Continue, Other ways to sign in | no |
| `step-branded` (1280) | The step in the branded chrome | no |
| `step-not-a-handle` | `not a handle` typed: error (a) under the field, value kept | no |
| `step-not-found` | `nobody-here.invalid-example-domain.com`: error (b) | no |
| `step-refused` | Connector set to Only these servers `pds.example.org`, then `bsky.app` (resolved live against Bluesky's public metadata; the machine has network): error (d) naming `bsky.social` | no |
| `step-focus` (1280 dark) | Keyboard focus on the Handle field, yellow ring visible. The field is autofocused, so it has focus on load | no |

Also checked in the walk (not captured): after any refusal on the step, focus is back on the field
(`focus after refusal on field: true`). On instance B a crafted `POST /admin/connectors/bluesky` returns 422
and creates nothing (the pane still says "Not set up").

## New strings as shown (`Views/Texts.fs`, key: text)

Step
- `BlueskyStepTitle`: What is your Bluesky handle?
- `BlueskyHandleLabel`: Handle (drawn in capitals by the label style: HANDLE)
- `BlueskyHandlePlaceholder`: you.bsky.social
- `BlueskyHandleHint`: Like you.bsky.social, or your own domain if you use one. It is on your Bluesky profile.
- `BlueskyNotAHandle`: Enter a Bluesky handle like you.bsky.social.
- `BlueskyNotFound`: No Bluesky account was found for nobody-here.invalid-example-domain.com. Check the spelling.
- `BlueskyNotAllowed`: Accounts on bsky.social cannot sign in here. Use another way to sign in.
- `BlueskyUnreachable`: {server} did not answer. Try again in a moment. (not captured; no way to make a server fail to answer here)
- `BlueskyUnsupported`: The server for {handle} does not offer the sign-in Umbral uses. Use another way to sign in. (not captured; needs a real PDS that lacks the sign-in)
- Reused from Fediverse: Continue, Other ways to sign in.

Pane
- `BlueskyCardLead`: People sign in with their Bluesky handle. Any AT Protocol account works, wherever it is hosted.
- `BlueskyMetadataLabel`: Client metadata address
- `BlueskyMetadataHint`: Each server fetches this address to learn about Umbral. There is nothing to paste anywhere, but it must be reachable from the internet.
- `BlueskyDevNote`: Development: Umbral signs in as AT Protocol's localhost client, so no address is fetched.
- `BlueskyNoEmailNote`: A Bluesky account has no email, so allowed email domains do not apply to it and it never joins another account by email.
- `BlueskyScopeAnyHint`: Anyone with a Bluesky or other AT Protocol account. (title "Any server" is the Fediverse key `FediverseScopeAnyLabel`)
- `BlueskyScopeListedHint`: Only accounts that sign in at the servers listed below. (title "Only these servers")
- `BlueskyServersLabel`: Servers
- `BlueskyServersPlaceholder`: bsky.social
- `BlueskyServersHint`: The server where people sign in, separated by commas or spaces. Accounts hosted by Bluesky all sign in at bsky.social. Used only with "Only these servers".
- `BlueskyServersEmptyNote`: The list is empty, so nobody can sign in through Bluesky. Add a server, or choose Any server. (not captured: it shows on a saved connector with Only these servers and an empty list; the walk never saved that state)
- `BlueskyEnableButton`: Enable Bluesky
- `BlueskyOriginBlocked`: Bluesky needs Umbral at an https address without a port, reachable from the internet. This instance is at http://localhost:18094, so Bluesky cannot be enabled here.
- Refusal, reused from Fediverse: "*.bsky.social" has a wildcard; list each server.

Notices (not captured, redirect banners)
- `bluesky-connector-saved`: Saved. People can sign in with a Bluesky account from the next page load.
- `bluesky-connector-invalid`: Choose Any server or Only these servers, then save again.
- `connector-enable-bluesky-origin`: Not enabled: Bluesky needs Umbral at an https address without a port, reachable from the internet.

## Mark decision for the picker button

The picker draws the **link icon** for Bluesky (GitHub has its own mark), as in `picker-*`. That matches the
contract's fallback (section 1); I did not check the research or `notes.md` for the reason.

## What looked off, or worth a second look

None of these breaks the page; they are for the reviewer to weigh.

1. **Metadata address is cut off in its field at 1280** (`connectors-any-https`): the input shows
   `https://auth.example.org/connect/bluesky/client-metadata` with the `.json` out of sight. It is a read-only
   copy field, so Copy still gives the full address, but the operator cannot see the end of what they copy.
   I did not compare it with the other copy fields, and instance C was shot at 1280 dark only, as asked.
2. **Servers field in Any server mode**: the placeholder `bsky.social` is nearly the same grey as a typed value
   in dark theme, so an enabled connector with Any server reads as if it had `bsky.social` listed
   (`connectors-any`, `connectors-any-https`). The Fediverse pane may behave the same; I did not check.
3. **The empty-list warning** (`BlueskyServersEmptyNote`) and the not-offered-anywhere alert were not
   captured; no state in the requested list produces them.
4. **Rate limit**: `/signin` and `/connect/*` allow 30 requests a minute per address, GET and POST together.
   A first run of the script without pacing hit a 429 in the last two step-refused captures; the script now
   waits 2.2 s before each request there. Only the capture was affected.
5. The branded picker's logo has empty space on its left: that is my 240x80 canvas, not a product problem.
