# Captures for the design review of task p1ch (Servers field: the example in the hint)

Taken 2026-11-12 against the Release build of `alta/sable-ararat-4mbw` (main a82d8ec plus this change),
with `capture.mjs` (Playwright, Chrome). The instance ran at `http://127.0.0.1:18096`, Development with
`UMBRAL__INSECURE_ALLOW_HTTP=true`, on a fresh data directory; it is stopped. Overflow is
`scrollWidth > innerWidth` read right before each shot: **no capture overflows** (every one was
`scrollWidth == innerWidth`).

## The change

| | Before | After |
| --- | --- | --- |
| Fediverse Servers field | placeholder `mastodon.social, fosstodon.org` | no placeholder |
| Fediverse hint (`FediverseServersHint`) | Server addresses, separated by commas or spaces. Used only with "Only these servers". | Server addresses, separated by commas or spaces, for example mastodon.social. Used only with "Only these servers". |
| Bluesky Servers field | (placeholder already removed in a4bd928, on main) | unchanged: no placeholder |
| Bluesky hint | unchanged, your ruling of 2026-11-12 | unchanged |
| `BlueskyHandlePlaceholder` (`you.bsky.social`) on the sign-in step | unchanged, as ruled | unchanged |

`FediversePlaceholder` is gone from `Views/Texts.fs`. The design contracts
(`design/contracts/fediverse-connector-ui.md`, `design/contracts/bluesky-connector-ui.md`) now say
"no placeholder" for the Servers field, with the Fediverse hint in its new wording (the Bluesky contract
still named the `bsky.social` placeholder removed in a4bd928).

## Captures

Both connectors enabled through their own forms with **Any server** chosen and the Servers field empty.

| File | Width | Theme |
| --- | --- | --- |
| `fediverse-connectors-any-1280-dark.png` | 1280 | dark |
| `fediverse-connectors-any-1280-light.png` | 1280 | light |
| `fediverse-connectors-any-360-dark.png` | 360 | dark |
| `fediverse-connectors-any-360-light.png` | 360 | light |
| `bluesky-connectors-any-1280-dark.png` | 1280 | dark |
| `bluesky-connectors-any-1280-light.png` | 1280 | light |
| `bluesky-connectors-any-360-dark.png` | 360 | dark |
| `bluesky-connectors-any-360-light.png` | 360 | light |

Read from the live page during the walk:

- Fediverse: Servers value `""`, `placeholder` attribute absent, hint as above, Any server checked.
- Bluesky: Servers value `""`, `placeholder` attribute absent, hint "The server where people sign in,
  separated by commas or spaces. Accounts hosted by Bluesky all sign in at bsky.social. Used only with
  "Only these servers".", Any server checked.

The Bluesky pane shows the development note in place of the client metadata address (expected on an
http development instance).

## Tests

`FediverseAdminTests` and `BlueskyAdminTests` now assert that the Servers input has no `placeholder`
attribute and that the hint is shown (Fediverse: it names mastodon.social).
