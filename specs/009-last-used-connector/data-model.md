# Data Model: "Last used" on the sign-in page

No table, no column, no migration. One value in the browser.

## Last-used hint (cookie `umbral_last_connector`)

| Field | Value |
| --- | --- |
| Value | the slug of the connector of the last successful sign-in (`google`, `discord`, `home`, `passkey`, …) |
| Written | by `Sessions.signIn`, i.e. after a successful end-user sign-in only |
| Lifetime | 365 days from the last write |
| Attributes | `Path=/`, `SameSite=Lax`, `HttpOnly`, `Secure` over HTTPS |
| Read | by `GET /signin` only |
| Never | an email, name, user id, domain; never rendered into a page; never logged |

## Ordering rule (`Picker.arrange`)

| Input | Result |
| --- | --- |
| no cookie | page unchanged |
| slug of an offered connector, two or more offered | that connector first with the badge, "or" divider, the others in their usual order |
| slug of an offered connector, only one offered | page unchanged |
| unknown, disabled or not-offered slug, or any other value | page unchanged |
| no connector offered | page unchanged ("no connectors" line) |
