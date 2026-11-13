# Who can sign in

By default, anyone with an account at an enabled provider can sign in and
become a user. To let in only some people, list the email domains you allow.
You can do it in two places:

- **For the whole instance:** on the **Connectors** screen, open **Allowed email
  domains** (the last entry of the list), fill in the field, for example
  `acme.com, example.org`, and click **Save**.
- **For one connector:** open the connector's page (or the add form) and fill in
  its own **Allowed email domains** field.

Separate domains with commas, spaces or new lines. Umbral takes care of a
leading `@`, a trailing dot and capital letters. If an entry is not a domain
name, Umbral saves nothing and tells you which one to fix. Write each domain on
its own: `*.acme.com` is refused.

**Which list applies.** A connector's own list replaces the instance list for
that connector. A connector with an empty list uses the instance list. If
neither has a list, anyone can sign in, as before. Each connector's page shows
which rule applies to it.

**What counts.**

- Only an email the provider has **verified** counts. An unverified email, or
  no email at all, is refused while a rule applies.
- The domain must match **exactly**, ignoring capital letters. `eu.acme.com` is
  not `acme.com`: if you want both, list both.
- The email checked is the one the provider gives for that sign-in, through
  that connector. It is never the email stored for the user from another
  provider.

A refused person goes back to the app's sign-in page with one message: "This
account can't be used to sign in here. Try a different account or another
option below, or contact whoever runs this Umbral." It does not name a domain
and does not say whether the email was unverified or from another domain. No
user is created.

**People who are already signed in.** Saving a rule does not sign anyone out,
disable anyone or delete anyone. Instead, the rule is checked the next time a
person comes back: at their next sign-in, the next time an app asks Umbral to
authorize them with their existing session, or the next time an app refreshes
their token. If the rule no longer admits them, they are refused there. Access
tokens already issued keep working until they run out, within an hour. Their
user stays on the **Users** screen, enabled and unchanged.

**To limit one app.** There is no rule per app. Instead, create a connector for
the app with the domain rule and offer only that connector to the app: for
example a second Google connector, "Google (Acme)", limited to `acme.com`, and
on **Apps** set the app's **Sign-in methods** to **Only the connectors I
choose** with only "Google (Acme)" ticked. This holds because an app only
accepts a session that was started through a connector it offers. Someone
signed in through another connector still gets the app's sign-in page, where
"Google (Acme)" is the only button. If you offer that app a connector that has
no domain rule as well, the **Apps** screen warns you, and names the connector
that would let anyone in. The warning does not block saving. It does not appear
when the instance list is set, because then every connector has a rule.

**Taking a list off.** If you empty the instance list while some connector has
no list of its own, Umbral first names those connectors, which would then let
anyone in, and saves only after you click **Remove the list anyway**.

**Where to look.** Every refusal is a `sign_in_failed` event in
`security_events`, with a detail that starts `email_domain_refused`. It gives
the connector, the reason (`domain_not_allowed`, `email_not_verified` or
`no_email`), which list applied, the stage and the email's domain, but never the
full address. Every change to a list is an `allowed_domains_changed` event.

How far a provider's "verified" can be trusted differs, so each walkthrough has
an **Allowed email domains** section:
[Discord](connectors/discord.md#allowed-email-domains) ·
[GitHub](connectors/github.md#allowed-email-domains) ·
[Google](connectors/google.md#allowed-email-domains) ·
[Apple](connectors/apple.md#allowed-email-domains) ·
[Microsoft](connectors/microsoft.md#allowed-email-domains) ·
[Your own OpenID Connect provider](connectors/oidc.md#allowed-email-domains).

These rules do not govern the passkey connector: passkeys carry no email, so
there is nothing to judge. Who can create an account through it is its
**enrolment mode** (invitations only, or anyone can sign up), chosen on its
page. The Apps screen warns about an app that offers a passkey connector open to
anyone, the way it warns about a connector without a rule.

Nor do they govern the Fediverse connector: a fediverse account carries no
verified email. Who can sign in through it is its server choice, **Any server**
or **Only these servers** with a list of server addresses, set on its page. Like
a domain rule, a change to that list reaches people who are already signed in:
someone from a server taken off it is refused at their next sign-in, the next
time an app authorizes them with their existing session, or the next time an
app refreshes their token. See the
[Fediverse walkthrough](connectors/fediverse.md#the-server-list).

The Bluesky connector is the same: a Bluesky account carries no email. Who can
sign in through it is its server choice, **Any server** or **Only these
servers**, and the list holds the servers where people sign in (all
Bluesky-hosted accounts sign in at `bsky.social`). A change to the list reaches
people who are already signed in in the same way. See the
[Bluesky walkthrough](connectors/bluesky.md#the-server-list).
