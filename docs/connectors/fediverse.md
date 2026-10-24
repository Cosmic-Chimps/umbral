# Fediverse connector

Lets people sign in to your apps with an account on a Mastodon-compatible server,
such as mastodon.social or fosstodon.org. There is no account for you to create
at any provider: Umbral registers itself with each server the first time someone
from that server signs in.

A server works when its OAuth metadata advertises PKCE (`S256`) and app
registration, or when it is GoToSocial, which enforces PKCE without publishing
metadata. So these servers work:

- **Mastodon 4.3 and later.** Umbral reads the server's OAuth metadata, which
  these versions publish.
- **GoToSocial.**

These do not: **Akkoma** and **Pleroma** do no PKCE, **Misskey** and
**Sharkey** have no app registration Umbral can use, and **Mastodon before 4.3**
publishes no metadata. A person who tries one of them stays on the account step and sees:
*{server} does not offer the sign-in Umbral uses. Mastodon and GoToSocial servers
do; Akkoma, Pleroma, Misskey and Sharkey do not yet. Use another account, or
another way to sign in.* (with the server's own name in place of `{server}`). The
person picks another account or another connector; nothing is saved and nothing
is asked of you.

Back to the [README](../../README.md). Other providers:
[Discord](discord.md) · [GitHub](github.md) · [Google](google.md) ·
[OpenID Connect](oidc.md) · [Passkeys](passkey.md).

## Before you start

You need Umbral running with `UMBRAL__PUBLIC_ORIGIN` set to the address people
really use. The callback address Umbral gives each server is built from it:
`<public origin>/connect/fediverse/callback`. If you change
`UMBRAL__PUBLIC_ORIGIN` later, Umbral registers again on each server, with the
new address, the next time someone from it signs in.

Umbral's container must be able to reach the servers your people use, over
HTTPS. A server on a private or loopback address is refused, unless you are
trying Umbral on a laptop with the development setting described under
[Gotchas](#gotchas).

## 1. Nothing to create at a provider

Other connectors ask you to create an application at the provider and paste its
client ID and secret into Umbral. Here Umbral does that itself, once for each
server, the first time someone from it signs in:

1. Umbral checks that the server works as described above.
2. It registers an app named "Umbral" on that server, asking for the
   `read:accounts` permission, with the callback address above and your public
   origin as the app's website.
3. It keeps the client ID and secret the server gives back, one pair per server,
   in the database in the data directory, the secret encrypted with the key ring
   in `keys/`. This is so even when you keep connector secrets in an external store
   with `UMBRAL__SECRET_STORE`: these secrets are created by Umbral for each
   server, so they stay beside the database and are covered by your backup of the
   data directory.

You paste nothing and copy nothing.

## 2. Enable the connector

Open **Connectors** in the admin area and choose **Fediverse** in the list. It
needs no client ID or secret. The **Callback URL** it shows is the one Umbral
registers on each server itself; there is nothing to paste anywhere. Choose who
can sign in:

- **Any server**: *Anyone with an account on a server that works like Mastodon.*
- **Only these servers**: *Only accounts on the servers listed below.*

With **Only these servers**, fill in **Servers**: *Server addresses, separated by
commas or spaces. Used only with "Only these servers".* For example
`mastodon.social, fosstodon.org`. See [The server list](#the-server-list).

Leave **Shared with every app** ticked to offer it everywhere, or untick it and
choose it per app on the **Apps** screen, as with any connector. Click **Enable
Fediverse**: *Saved. People can sign in with a Fediverse account from the next
page load.* No restart is needed. There is one Fediverse connector per instance,
and you can change who can sign in later; it takes effect at once.

If **Only these servers** is chosen with an empty list, nobody can sign in. The
pane says so: *The list is empty, so nobody can sign in through Fediverse. Add a
server, or choose Any server.*

If an entry in **Servers** is not a server address, the pane names it under the
field and saves nothing. See [The server list](#the-server-list).

## How a person signs in

On an app's sign-in page the person presses **Sign in with Fediverse**. Umbral
asks where their account is. The page is titled *Where is your account?*, with
the field **Your account or server** and the hint *A handle like
@you@mastodon.social, or a server like mastodon.social. Mastodon and GoToSocial
servers work.* They type one of four forms, and they all work:

- `@ana@mastodon.social`
- `ana@mastodon.social`
- `mastodon.social`
- `https://mastodon.social`

They press **Continue**. Umbral works out the server, makes sure it is allowed,
and sends them to it to approve the sign-in. When they come back they are signed
in and sent on to the app. If the app offers other connectors too, the page has a
link, **Other ways to sign in**, back to the choice.

For a handle, Umbral asks the handle's domain where the account really lives,
because a server can use one name in handles and another for its website. For a
server name, it uses that server as typed. Capitals, a trailing slash and the
default port are tidied away, so `Mastodon.Social` and `mastodon.social` are the
same server.

## The server list

With **Only these servers**, the list is checked before Umbral contacts any
server.

- **How it is read.** Entries are separated by commas or spaces. Each entry is
  tidied the way a typed server is: capitals, `@`, `https://` and a trailing
  slash are dropped, so you can paste addresses as people write them.
- **Wildcards are refused.** `*.example.social` is not a server. List each
  server. The pane says: *"{entry}" has a wildcard; list each server.* (with the
  entry in place of `{entry}`).
- **Something that is not an address is refused**: *"{entry}" is not a server
  address.* Nothing is saved until every entry is right.
- **A server with two names needs both listed.** Some servers use one name in
  handles (`@ana@example.com`) and another for the server itself
  (`social.example.com`). Umbral checks the name the person typed and the name
  the server turns out to have, so list both.
- **An empty list refuses everyone**, as above. To let every server in, choose
  **Any server**.
- **Someone from a server that is not listed** sees: *Accounts on {server} cannot
  sign in here. Use another way to sign in.* Umbral has not contacted that
  server. The check runs again when the person comes back from their server, so
  removing a server from the list takes effect for a sign-in already under way.
  That person lands on the sign-in page with the notice *This account can't be
  used to sign in here. Try a different account or another option below, or
  contact whoever runs this Umbral.*

Removing a server from the list does not delete its registration and does not
delete the people who signed in from it. It does reach the people from it who
are already signed in, as a change to allowed email domains does: they are
refused the next time an app authorizes them with their existing session, or
the next time an app refreshes their token, and they cannot sign in until the
server is listed again. Access tokens already issued keep working until they
run out, within an hour.

## Scopes Umbral requests

| Scope | What it lets Umbral read |
| --- | --- |
| `read:accounts` | The signed-in account: its id, username, display name, avatar and page address |

Umbral asks for nothing else. It cannot post, follow, read the person's timeline
or send anything. It keeps the access token only for the one request that reads
the account, and then discards it. Umbral sends PKCE (`S256`) with every request.

## What people see

1. They click **Sign in with Fediverse** on Umbral's page.
2. They type their account or server and press **Continue**.
3. Their server asks them to sign in if they are not, then shows an authorization
   page for an app named "Umbral", listing that it can read their account. The
   first time only, they approve.
4. They are sent back to Umbral, which sends them on to the app they came from.

If they refuse at their server, they land on the sign-in page with: *You
cancelled the sign-in. Choose an option below to try again.*

## What Umbral takes from the server

- **Identity** is the server's name together with the account's id on that server
  (the subject is `<server>#<account id>`). It is never the handle, so a person
  who changes their username or display name is still the same Umbral user. A
  person with accounts on two servers is two users, because Umbral cannot know
  they are the same person.
- **`name`** is the account's display name, or its username if there is none. It
  is refreshed at every sign-in.
- **`preferred_username`** is `user@server`, with the server's own name. For a
  server whose handles use another name, this shows the server and not the
  handle's domain.
- **`picture`** is the account's avatar address.
- **`profile`** is the address of the account's page, and only when it is HTTPS.
- **No email.** A fediverse account has no verified email, so the app gets no
  `email` and no `email_verified`, whatever scopes it asks for. A claim the
  server does not supply is absent. Apps see `idp` set to `fediverse`.

An app that must have an email can ask the person for it itself, or not offer the
Fediverse connector (on the **Apps** screen, choose its sign-in methods).

## What the server learns

The server sees an app named "Umbral", with your public origin as its website, the
callback address, and the `read:accounts` permission. The server's own
administrators can see that app in their list of applications, and the person can
see it among the apps they have authorised.

## Allowed email domains

They do not apply: a fediverse account carries no verified email, so there is
nothing to judge. The Fediverse pane on the Connectors screen says so: *A fediverse account has no
verified email, so allowed email domains do not apply to it and it never joins
another account by email.* Who can sign in is the choice between **Any server**
and **Only these servers**. The **Apps** screen treats a Fediverse connector as
limited exactly when it is on **Only these servers**. See
[Who can sign in](../../README.md#who-can-sign-in).

## Troubleshooting

The account step shows these under the field and keeps what the person typed.

- *Enter a handle like @you@mastodon.social, or a server address.* The text is
  neither a handle nor a server address. It also appears for a private,
  loopback or link-local address, which Umbral refuses. Fix the typing, or type
  the server's public name.
- *{server} does not offer the sign-in Umbral uses. Mastodon and GoToSocial
  servers do; Akkoma, Pleroma, Misskey and Sharkey do not yet. Use another
  account, or another way to sign in.* The server is not one of the supported
  kinds, or runs a Mastodon before 4.3. Nothing can be done on your side; the
  person uses another account or connector.
- *Accounts on {server} cannot sign in here. Use another way to sign in.* The
  list is on **Only these servers** and this server is not on it. If it should
  be, add it on **Connectors**, and both names for a server with two.
- *{server} did not answer. Try again in a moment.* The server timed out, gave
  an error, or Umbral could not register on it. Try again; if it keeps happening,
  check that Umbral's container can reach the server over HTTPS.

Failures after the person returns from their server land on the sign-in page with
one of the usual notices: *Sign-in didn't complete. Try again, or choose a
different option.* for a wrong or reused request, a failed exchange or an account
Umbral could not read, and the two notices quoted above for a refusal and for a
server that is no longer allowed. Try again; if it repeats, read the security
log, below.

On the **Connectors** screen, a Fediverse entry that cannot be saved shows the
entry that was refused under **Servers**, as quoted under
[The server list](#the-server-list). *Choose Any server or Only these servers,
then save again.* appears if the choice is missing; choose one and save.

## Gotchas

- **A server that deletes the app.** If an administrator removes the "Umbral" app
  on a server, or the server loses it, Umbral notices the next time someone signs
  in from there (it checks at most once a day, and also if the code exchange is
  refused), registers again once, and carries on. If that registration is refused
  too, the person is told the server did not answer. Nothing for you to do unless
  it persists.
- **Servers without PKCE are refused.** Akkoma and Pleroma do not implement it, and
  Misskey has no app registration Umbral can use. Umbral will not send a sign-in
  to a server it cannot be sure checks the code, so there is no setting that lets
  them in.
- **Private and loopback addresses are refused.** Umbral will not send a
  registration to `localhost`, to an IP address typed as the server, or to a name
  that resolves to a private, loopback or link-local address. The check happens again when Umbral connects, so a name that is
  changed after the first check is refused too. For a laptop trial against a
  server of your own, run with `ASPNETCORE_ENVIRONMENT=Development` and
  `UMBRAL__INSECURE_ALLOW_HTTP=true`: both are needed, and then plain `http` and
  private addresses are allowed.
- **Changing `UMBRAL__PUBLIC_ORIGIN`.** Each server keeps the callback address
  Umbral registered with. Umbral remembers it too, and registers again with the
  new address the next time someone from that server signs in. The old app stays
  in the server's list until its administrator removes it.
- **No screen lists servers in this version.** The registrations are in the
  database (`fediverse_servers`), not on any admin screen. Deleting the connector
  keeps them, and keeps the people who signed in with it.
- **Rate limit.** The account step and the callback share the sign-in limit: 30 a
  minute per address.
- **The security log** records `fediverse_server_registered`, with `server=<host>`,
  whenever a registration is stored, including a registration made again. A
  failed sign-in is a `sign_in_failed` event whose detail starts with
  `fediverse:` and a reason, then `server=<host>` when the server is known. No
  token, code or secret is recorded. See
  [Troubleshooting](../../README.md#troubleshooting).
