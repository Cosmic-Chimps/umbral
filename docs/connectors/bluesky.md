# Bluesky connector

Lets people sign in to your apps with their Bluesky handle. Any account on the
AT Protocol works, whether Bluesky hosts it or the person runs a server of
their own (a PDS). There is no account for you to create at any provider and
nothing to paste anywhere: Umbral publishes one small document about itself,
and each person's server reads it the first time someone from that server signs
in.

Back to the [README](../../README.md). Other providers:
[Discord](discord.md) · [GitHub](github.md) · [Google](google.md) · [Apple](apple.md) ·
[Microsoft](microsoft.md) · [OpenID Connect](oidc.md) · [Passkeys](passkey.md) · [Fediverse](fediverse.md).

## Before you start

You need Umbral running with `UMBRAL__PUBLIC_ORIGIN` set to the address people
really use, and that address has to be:

- **`https`**, on a real domain name;
- **without a port**, so `https://auth.example.org` and not
  `https://auth.example.org:8443`;
- **reachable from the internet**, because each person's server fetches
  Umbral's client metadata document from it. The document is at
  `<public origin>/connect/bluesky/client-metadata.json`, and its address is the
  name Umbral goes by at every server. AT Protocol puts no port in that address.

If the instance is not at such an address, the **Bluesky** pane on the
**Connectors** screen says so: *Bluesky needs Umbral at an https address without
a port, reachable from the internet. This instance is at {origin}, so Bluesky
cannot be enabled here.* (with your origin in place of `{origin}`). With no
connector yet, that is all it offers: no settings and no **Enable Bluesky**, and
a crafted request to create one is refused. A connector set up before the
address changed keeps its sections, so you can still save its settings, disable
it or delete it. Enabling it is refused with *Not enabled: Bluesky needs Umbral
at an https address without a port, reachable from the internet.* For a laptop
trial, see [Gotchas](#gotchas).

Umbral's container must also be able to reach the servers your people use over
HTTPS, and the PLC directory at `https://plc.directory`, where most accounts are
looked up. A server on a private or loopback address is refused.

## 1. Nothing to create at a provider

Other connectors ask you to create an application at the provider and paste its
client ID and secret into Umbral. Bluesky asks for neither. Umbral is a *public
client*: it holds no secret, and what a server needs to know about it is in the
client metadata document, which Umbral serves while the connector is set up. It
names:

- the callback address, `<public origin>/connect/bluesky/callback`, as the only
  place a sign-in may return to;
- the permission Umbral asks for, `atproto`, and nothing else;
- the name "Umbral" and your public origin as its website.

You register nothing, so there is nothing to renew and nothing to back up for
this connector beyond Umbral's own data.

## 2. Enable the connector

Open **Connectors** in the admin area and choose **Bluesky** in the list, which
shows it after **Fediverse**. The pane has:

- **Client metadata address.** The one address servers fetch, with a copy
  button. *Each server fetches this address to learn about Umbral. There is
  nothing to paste anywhere, but it must be reachable from the internet.*
- A note that a Bluesky account has no email, so allowed email domains do not
  apply to it.
- The settings: **Shared with every app**, who can sign in, and **Servers**.

Choose who can sign in:

- **Any server**: *Anyone with a Bluesky or other AT Protocol account.*
- **Only these servers**: *Only accounts that sign in at the servers listed
  below.*

With **Only these servers**, fill in **Servers**: *The server where people sign
in, separated by commas or spaces. Accounts hosted by Bluesky all sign in at
bsky.social. Used only with "Only these servers".* For example
`bsky.social, pds.example.org`. See [The server list](#the-server-list).

Leave **Shared with every app** ticked to offer it everywhere, or untick it and
choose it per app on the **Apps** screen, as with any connector. Click **Enable
Bluesky**. No restart is needed. There is one Bluesky connector per instance,
and you can change who can sign in later; it takes effect at once.

If **Only these servers** is chosen with an empty list, nobody can sign in, and
the pane says so: *The list is empty, so nobody can sign in through Bluesky. Add
a server, or choose Any server.* If an entry in **Servers** is not a server address, the pane
names it under the field and saves nothing.

## How a person signs in

On an app's sign-in page the person presses **Sign in with Bluesky**. The button
carries the link icon, not Bluesky's butterfly. Umbral asks for their handle: the
page is titled *What is your Bluesky handle?*, with the field **Handle** and the
hint *Like you.bsky.social, or your own domain if you use one. It is on your
Bluesky profile.* They type one of these forms, and all of them work:

- `alice.bsky.social`
- `@alice.bsky.social`
- `alice.example.com`, for someone who owns that domain
- a DID, such as `did:plc:…` or `did:web:…`

A bare server name such as `bsky.social` is not accepted as a server: it is
itself a valid handle, so Umbral reads it as an account.

They press **Continue**. Umbral works out where the account lives and which
server the person signs in at, makes sure that server is allowed, and sends them
to it. Their server's own page asks them to sign in if they are not, and to let
Umbral identify them. When they agree they come back, and Umbral sends them on
to the app. If the app offers other connectors too, the page has a link, **Other
ways to sign in**, back to the choice.

To find the account Umbral uses the handle the way AT Protocol defines it: the
handle's domain says which account it is (by a DNS record, or at
`https://<handle>/.well-known/atproto-did`), and the account's own published
identity must name that handle back. A handle that does not confirm both ways
is not found. Umbral then reads the account's server from its identity, and the
one server that account signs in at from that server. It follows no redirect on
the way.

## The server list

The list holds the hosts where people **sign in**: the authorization server, not
the server that stores their data.

- **Accounts hosted by Bluesky** all sign in at `bsky.social`, although their
  data sits on one of many `*.host.bsky.network` servers. So `bsky.social` is
  the one entry that admits every Bluesky-hosted account.
- **A self-hosted PDS** is usually its own authorization server, so its host is
  the entry, for example `pds.example.org`.

The list is read as the Fediverse list is: entries are separated by commas or
spaces, and capitals, `https://` and a trailing slash are dropped. A wildcard is
refused, and so is an entry that is not a server address; nothing is saved
until every entry is right. See the
[Fediverse page](fediverse.md#the-server-list).

The list is checked at four points:

1. **At the step**, before Umbral sends anything to the server. Someone whose
   account signs in at a server that is not listed sees *Accounts on {server}
   cannot sign in here. Use another way to sign in.*
2. **At the callback**, when the person comes back from their server, so
   removing a server takes effect for a sign-in already under way.
3. **At a remembered sign-in**, when an app authorizes a person with their
   existing session.
4. **At a token refresh.**

For the last two, Umbral keeps with each person's link the host of the server
they last signed in through, because a DID does not say where it signs in. A
person removed from the list is refused the next time, and cannot sign in until
the server is listed again. Access tokens already issued keep working until
they run out, within an hour. Removing a server does not delete the people who
signed in from it.

## What people see

1. They click **Sign in with Bluesky** on Umbral's page.
2. They type their handle and press **Continue**.
3. Their server asks them to sign in if they are not, then asks whether to let
   Umbral identify them.
4. They are sent back to Umbral, which sends them on to the app they came from.

If they refuse at their server, or the sign-in cannot be checked, they land on
the sign-in page with a notice, as for any connector. See
[Troubleshooting](#troubleshooting).

## What an app receives

| Claim | Value |
| --- | --- |
| `sub` | Umbral's own opaque identifier for the user, as for every connector. |
| `name` | The display name on the person's Bluesky profile, or their handle when there is none. Refreshed at every sign-in. |
| `preferred_username` | The handle, such as `alice.bsky.social`, only when it is confirmed both ways: the handle leads to this account and the account names the handle. Otherwise none, and an earlier one is cleared. |
| `picture` | The avatar's address, when the profile has one. |
| `profile` | `https://bsky.app/profile/<did>`. |
| `idp` | `bluesky`. |

`name`, `preferred_username`, `picture` and `profile` need the `profile`
scope; `idp` is always there. There is **no email**: Bluesky gives
none, so the app gets no `email` and no `email_verified`, whatever scopes it
asks for. An app that must have an email can ask the person for it itself, or
not offer the Bluesky connector (on the **Apps** screen, choose its sign-in
methods).

The display name and avatar are read from the person's public profile on their
own server. If that cannot be read, the sign-in goes on with the handle as the
name and no picture. A display name has control and format characters removed
and is cut to a sensible length.

## Identity is the DID

The person is identified by their **DID**, the permanent identifier of an AT
Protocol account, such as `did:plc:…`. It is never the handle, because a handle
can pass to someone else and a DID cannot. A person who moves from
`alice.bsky.social` to `alice.example.com` signs in with the new handle and is
the same Umbral user, and `preferred_username` follows the new handle. A person
with two accounts is two users.

Umbral checks who is speaking before it signs anyone in. After the exchange it
reads the identity in the answer again and works out, from the account's own
published identity, which server is the authority for it. That must be the
server that issued the answer. Without this check a server could claim anyone's
DID. A sign-in that fails it is refused and recorded in the security log.

## What the server learns

The person's server sees a client named "Umbral", with your public origin as its
website, the callback address, and the `atproto` permission. Umbral asks for no
other permission. It cannot post, follow or read the person's messages. Umbral
does not ask for a refresh token.

The server also sees the handle the person typed, as a hint for its own sign-in
page.

## What Umbral keeps

Nothing from the server. The tokens are used once, to read the public profile,
and then dropped. Umbral keeps the DID, the handle, the display name and avatar
address, and the host of the server the person signed in through. Bluesky never
learns that a person signed out of Umbral.

The key Umbral uses to prove each request is new for every sign-in. It lives only
in the encrypted cookie of that attempt, and is never written to the data
directory. The cookie dies with the attempt, in at most ten minutes.

## Allowed email domains

They do not apply: a Bluesky account carries no email, so there is nothing to
judge, and a Bluesky identity never joins another user by email. Who can sign in
is the choice between **Any server** and **Only these servers**. The **Apps**
screen treats a Bluesky connector as limited exactly when it is on **Only these
servers**. See [Who can sign in](../who-can-sign-in.md).

## Troubleshooting

The handle step shows these under the field and keeps what the person typed.

- *Enter a Bluesky handle like you.bsky.social.* The text is not a handle or a
  DID. It also appears for an unknown kind of DID, and for a handle on a private,
  loopback or link-local address, which Umbral refuses. Security log reason:
  `not_a_handle`.
- *No Bluesky account was found for {handle}. Check the spelling.* The handle
  leads to no account, or the account does not name the handle back, or its
  identity could not be read. Fix the spelling. Security log reason:
  `handle_not_found`.
- *Accounts on {server} cannot sign in here. Use another way to sign in.* The
  list is on **Only these servers** and the server where this person signs in is
  not on it. If it should be, add it on **Connectors**. Remember that
  Bluesky-hosted accounts sign in at `bsky.social`. Reason: `server_not_allowed`.
- *{server} did not answer. Try again in a moment.* A server on the way (the
  handle's domain, the PLC directory, the account's server or the sign-in
  server) timed out or gave an error. If it keeps happening, check that
  Umbral's container can reach it over HTTPS. Reason: `server_unreachable`.
- *The server for {handle} does not offer the sign-in Umbral uses. Use another
  way to sign in.* The sign-in server lacks something AT Protocol requires, or
  its own description is inconsistent, or it refused a request twice. Nothing
  can be done on your side. Reason: `server_unsupported`.

A person who presses Cancel at their server comes back to *You cancelled the
sign-in. Choose an option below to try again.* Other failures after the person
comes back from their server land on the sign-in page with one of the usual
notices: *Sign-in didn't complete. Try again, or choose a
different option.* for a wrong or reused request, a failed exchange or an
identity Umbral could not confirm, and *This account can't be used to sign in
here. Try a different account or another option below, or contact whoever runs
this Umbral.* for a server that is no longer allowed. Try again; if it repeats,
read the security log, below.

The reasons for those failures, in the security log, are `iss_mismatch` (the
answer came from a server other than the one the attempt started with),
`server_not_allowed`, `exchange_failed`, `token_invalid` (the answer lacked the
`atproto` permission, a `sub`, or was not a DPoP token), `did_mismatch` (the
answer named a different account from the one the person typed) and
`not_authoritative` (the server that answered is not the authority for that
account). One more reason can appear when the handle step opens or a handle is submitted:
`origin_unsuitable`, when the instance's address cannot work for Bluesky (see
[Before you start](#before-you-start)).

## Gotchas

- **The client metadata address is the name Umbral goes by.** If you change
  `UMBRAL__PUBLIC_ORIGIN`, servers see a different client the next time
  someone signs in. Nothing is registered, so there is nothing to clean up.
- **An origin that cannot work is refused, not guessed at.** If an enabled
  connector later finds itself at an `http` address or one with a port, its entry
  on **Connectors** carries the attention marker, the pane shows the warning
  above, and people who press the button are sent back to the sign-in page
  with *Sign-in didn't complete. Try again, or choose a different option.*
  Security log reason: `origin_unsuitable`.
- **Trying it from a laptop.** With `ASPNETCORE_ENVIRONMENT=Development` and
  `UMBRAL__INSECURE_ALLOW_HTTP=true` both set, and `UMBRAL__PUBLIC_ORIGIN` at a
  loopback address such as `http://127.0.0.1:8080`, Umbral signs in as AT
  Protocol's *localhost client*, which servers accept without fetching anything.
  A real Bluesky account can then be tried from a laptop. The pane says: *Development:
  Umbral signs in as AT Protocol's localhost client, so no address is fetched.*
  Use `127.0.0.1`: `localhost` is not accepted as the origin for this. Private
  addresses and plain `http` are allowed to the servers on the way in this
  mode. Never set these on a server.
- **The PLC directory.** Umbral reads most account identities from
  `https://plc.directory`. It can be pointed at another one with
  `UMBRAL__BLUESKY_PLC_DIRECTORY`, for a private mirror or for tests. It must be
  `https` unless the development setting is on. It is not on any screen.
- **Private and loopback addresses are refused.** Every fetch Umbral makes for
  this connector (the handle's address, the account's identity, the servers'
  descriptions and the profile) refuses private, loopback and link-local
  addresses, connects only to the standard https port, follows no redirect,
  and has a size limit. One attempt has 20 seconds in all. A sign-in server's
  endpoints must be on its own address, and with **Only these servers** an
  unlisted sign-in server is not contacted at all.
- **Rate limit.** The handle step and the callback share the sign-in limit: 30 a
  minute per address.
- **The security log** records a failed sign-in as a `sign_in_failed` event
  whose detail is `bluesky:` and a reason, then `;server=<host>` when the
  server is known. A failure of the request's own checks, such as a reused
  or expired attempt, carries the check after the reason:
  `bluesky:exchange_failed;<check>;server=<host>`. No token, key or code is recorded. See
  [Troubleshooting](../run/troubleshooting.md).
