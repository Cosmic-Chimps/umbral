# Contract: Admin Surface for Allowed Email Domains

All strings live in `Views/Texts.fs`. Markers (`data-*`) are what tests select
on. Exact wording may be polished in review, but every message keeps its
"what to do next" part (Principle VI).

## Input rules (both levels)

Separators: comma, space, tab, new line. A leading `@` and a trailing `.` are
removed; case and Unicode/ASCII form are normalised; duplicates dropped
(research R3). Nothing is saved when an entry is refused:

| Entry | Message |
| --- | --- |
| contains `*` | "'{entry}' is a wildcard. List each domain on its own, like acme.com, eu.acme.com, then save again." |
| not a domain name of two or more labels | "'{entry}' isn't a domain name. Enter domains like acme.com, separated by commas, then save again." |

## Connectors screen — instance card

At the top of `/admin/connectors`, after the banners: `data-card="allowed-domains"`.

- Title: "Who can sign in"
- Lead: "Allowed email domains for every connector that has no list of its own. Only an email the provider has verified counts, and a subdomain must be listed on its own. Leave empty to let anyone with an account at an enabled provider sign in."
- Field `allowedDomains`, label "Allowed email domains", placeholder `acme.com, example.org`, current list shown in Unicode form joined by `, `.
- Button "Save" → `POST /admin/allowed-domains` (antiforgery).

Outcomes:

| Case | Response |
| --- | --- |
| valid, changed | list stored; `allowed_domains_changed` (no slug, `count=n`); redirect, notice `allowed-domains-saved`: "Allowed email domains saved. They apply from the next sign-in." |
| valid, unchanged | redirect with the same notice, no event |
| invalid | nothing stored; the screen shows the input-rule message and keeps what was typed |
| empties a non-empty list, some connector has no list, no `?confirm=yes` | nothing stored; notice `allowed-domains-removal` (warning): "Removing this list lets anyone with an account sign in through these connectors, which have no list of their own:" followed by the connectors' names (`data-opened-connector="<slug>"`) and a button "Remove the list anyway" that posts the empty list with `?confirm=yes` |
| empties it, every connector has its own list | stored without a warning |

## Connector add form and connector cards

- Field `allowedDomains`, label "Allowed email domains", on the add form (all
  kinds, OpenID Connect included) and on each connector card's save form.
- Help: "Only people whose verified email is at one of these domains can sign in through this connector. Leave empty to use the instance list ({instance list, or "none: anyone can sign in"})."
- Note (`data-note="limit-one-app"`): "To limit one app, create a connector for it with the domain rule and offer only that connector to the app."
  On an existing OpenID Connect connector's card the same marker carries instead: "Umbral keeps one connector per issuer, so you can't add a second one for this provider. To limit one app, offer this connector only to that app, or limit the app at your provider."
- Saving: invalid → the input-rule message, nothing saved (the secret is never
  redrawn, as today). A changed list → `allowed_domains_changed` with the slug.
  A card post without the field (the unshare confirmation) leaves the list as
  it is.
- Each card shows `data-rule="connector|instance|anyone"`:
  - connector: "Who can sign in: {list} (this connector's list)"
  - instance: "Who can sign in: {list} (the instance list)"
  - anyone: "Who can sign in: anyone with an account"

## Apps screen

On each app row whose enabled offered connectors include at least one with a
rule and at least one without: `data-warning="unrestricted-connectors"`:

> "{connector names}" {has|have} no domain rule and let anyone with an account sign in to this app. To keep this app limited, offer only connectors with a domain rule.

Saving the app's connector choice with that mix succeeds and shows the same
text as a warning notice. With an instance list set, no app shows it.

## Security events

See [data-model.md](../data-model.md#security-events).
