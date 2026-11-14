# Run Umbral from an Aspire AppHost

If you build with [.NET Aspire](https://aspire.dev), the NuGet package
`CosmicChimps.Aspire.Hosting.Umbral` runs Umbral from your AppHost in one call, as
`AddKeycloak` does for Keycloak. Add it to the AppHost project:

```bash
dotnet add package CosmicChimps.Aspire.Hosting.Umbral
```

The package is published with each Umbral release from 0.12.0 on.

It starts the published image, `ghcr.io/cosmic-chimps/umbral`, and gives your apps
the issuer address. It can also create your apps and the passkey connector when
Umbral starts, so there is nothing to click: see
[Declare your app and sign in](#declare-your-app-and-sign-in). It never claims
Umbral for you.

## Run it on your machine

```csharp
var umbral = builder.AddUmbral("umbral")
    .WithDataVolume();

builder.AddProject<Projects.Web>("web")
    .WithReference(umbral)
    .WaitFor(umbral);
```

`aspire run` then starts Umbral at `http://localhost:8080`. The port is fixed on
purpose: the public origin is the issuer in every token, and each app's redirect
addresses are registered against it once, so a random port would break both. To
use another port, pass it: `AddUmbral("umbral", port: 8081)`.

In run mode, with no public origin given, the package sets:

| Variable | Value |
| --- | --- |
| `UMBRAL__PUBLIC_ORIGIN` | `http://localhost:<port>` |
| `ASPNETCORE_ENVIRONMENT` | `Development` |
| `UMBRAL__INSECURE_ALLOW_HTTP` | `true` |

The last two are the laptop pair from the
[settings reference](settings.md): they let Umbral accept plain `http://`. They are
set only while the public origin is plain http on a loopback address (`localhost`,
`127.0.0.1` or `[::1]`), and such an origin must use the resource's port. An
origin given with `WithPublicOrigin` is used in run mode too, and any other
origin turns the pair off: for `aspire run` on a laptop, leave it out.

Aspire waits for `/healthz` before it starts what you marked `WaitFor`, and the
dashboard links the resource to `/admin`.

`WithReference(umbral)` gives the app `UMBRAL_ISSUER`, set to Umbral's public
origin and never to its internal address (`http://umbral:8080`): an app checks the
issuer against exactly what Umbral writes in tokens. `UMBRAL_ISSUER` is the
variable every [sample](../../samples/README.md) reads.

In run mode the issuer is `http://localhost:<port>`, which an app running on your
machine (an `AddProject` resource) reaches. An app in a container (`AddContainer`,
`AddDockerfile`) cannot: `localhost` there is the container itself, so it can't
load the discovery document. Run that app as a project while you develop, or
deploy both and use the public origin.

## Declare your app and sign in

Declare the passkey connector and your app in the AppHost. Umbral creates both when
it starts, and the project that signs in through the app receives its issuer,
client ID and secret:

```csharp
var umbral = builder.AddUmbral("umbral")
    .WithDataVolume()
    .WithPasskeys(PasskeySignUp.Open);        // the passkey connector; invitations only by default

var web = builder.AddProject<Projects.Web>("web");

var app = umbral.AddApp("web")                // key = client ID; a web (confidential) app by default
    .WithDisplayName("Sample web app");
web.WithReference(app)                        // UMBRAL_ISSUER, UMBRAL_CLIENT_ID, UMBRAL_CLIENT_SECRET
    .WaitFor(umbral);
```

`aspire run` starts Umbral, then your project, and a new passkey signs in to it.
There is no admin step: signing in to an app never needed the claim, and Umbral
applies the declaration whether or not it has been claimed. **Claim Umbral only
when you want to open the admin area**: use the second link in the dashboard,
"Claim (first run)", which goes to `/admin/setup`, and paste the setup token from
Umbral's console log. With `PasskeySignUp.Open`, claim the instance before you
publish it (see [For a production instance](#for-a-production-instance)).

What the declaration does, and what it never touches, is in
[Declared apps](#declared-apps) below. To register an app by hand instead, leave
out `WithPasskeys` and `AddApp`: then Umbral gets no declaration, and you
[claim the instance](../../README.md#3-claim-the-instance) and
[register your app](../../README.md#5-register-an-app) in the admin area, as the
README says.

### The AppHost methods

- `AddApp(key)` declares an app. The key is its client ID: 1–32 lowercase letters,
  digits or hyphens. `AddApp` throws on a key that breaks the rule or is used twice.
  The app is a web (confidential) app. `AddApp(key, UmbralAppType.Spa)` makes a
  public app that signs in with PKCE only, with no secret and no
  `UMBRAL_CLIENT_SECRET`.
- `WithDisplayName(name)` sets the name people see. It defaults to the key.
- `WithConnectors("passkey", ...)` limits the app's sign-in methods to those
  connectors. Without it the app offers every shared connector.
- `WithReference(app)` on a project sets `UMBRAL_ISSUER`, `UMBRAL_CLIENT_ID` and,
  for a web app, `UMBRAL_CLIENT_SECRET`. An app referenced by two projects needs
  explicit redirect URIs.
- `WithPasskeys(PasskeySignUp.InvitationsOnly | Open)` declares the passkey
  connector.

### Redirect URIs

In run mode a web app with no redirect URI of its own gets the `http` endpoint of
the project that references it, plus `/signin-oidc` and, as the post-logout URI,
`/signout-callback-oidc`. `WithCallbackPaths(signIn, signOut)` changes the paths,
or turns this on for a spa app. `WithRedirectUri(uri)` and
`WithPostLogoutRedirectUri(uri)` add explicit ones; an app with an explicit
redirect URI gets only those, unless you also call `WithCallbackPaths`. An app with
no redirect URI at all is an error that names it.

When you publish, a project's endpoint is an internal address no browser reaches,
so the package refuses to derive the URIs and says to call `WithRedirectUri` with
the public address:

```csharp
umbral.AddApp("web")
    .WithRedirectUri("https://app.example.com/signin-oidc")
    .WithPostLogoutRedirectUri("https://app.example.com/signout-callback-oidc");
```

## Declared apps

A declaration is a list of apps and, optionally, the passkey connector, read from
the environment each time Umbral starts. The package writes it for you from
`AddApp` and `WithPasskeys` (`UMBRAL__DECLARATION`, plus
`UMBRAL__APP_SECRETS__<key>` for each web app's secret). You can write the same
variables yourself without Aspire: see the [settings reference](settings.md).
Without `AddApp` or `WithPasskeys`, the package sets neither, and Umbral reads no
declaration.

**What the declaration owns.** At every start a declared app is created if it is
missing and otherwise updated to match: its name, type, redirect URIs, post-logout
redirect URIs, sign-in methods and secret. The passkey connector's *enabled*,
*who can sign up* and *shared* settings are owned the same way. What the
declaration does not name stays yours: an app's branding and the passkey
connector's invitations are still changed in the admin area. It never touches
other connectors, users, the operator credential or instance settings.

**The Declared mark.** In the admin area a declared app, and the passkey connector
when it is declared, carry a **Declared** mark. Its owned fields are shown as
values, and **Delete** and **Regenerate secret** are not offered: any change there
would be undone at the next start. The page says where to change it. A request that
tries anyway is refused with "This app is declared in Umbral's start-up settings,
so it was not changed. Change it there, then start Umbral again."

**When an app leaves the declaration.** An app you stop declaring is handed back,
never deleted, disabled or changed: it keeps working, loses the mark, and from the
next start the admin area owns it like any app you made there. Remove its
`UMBRAL__APP_SECRETS__<key>` variable with it: a secret variable for a key that is
no longer declared stops the start. The Aspire package does this for you. Dropping
`WithPasskeys` hands the passkey connector back the same way. When Umbral starts
with no declaration at all (the variable unset), everything still marked declared is
handed back, so turning the feature off never leaves an app locked. You can declare
an app again later: it was the declaration's before.

**Identity.** An app's key is its client ID, so it is the same at every start and on
every volume. The display name is free to change; the key is not. Change the key and
you declare a new app, and the old one is handed back.

**Conflicts are refused, never merged.** If a declared key equals the client ID of an
app made in the admin area, Umbral refuses it. If the passkey connector was set up
by hand and differs from the declaration (for example, invitations only where you
declare open sign-up), Umbral refuses that too, because it may be in use. A passkey
connector set up by hand that already has exactly the declared settings (enabled,
shared, the same sign-up mode) is adopted: nothing changes and nobody is widened,
and it is marked Declared from then on. Apps made in the admin area have random
client IDs, so they only conflict with a key you chose to match.

**A refused declaration stops Umbral.** Umbral checks the whole declaration before it
writes anything: the JSON, each key, each redirect URI by the admin area's own rules
(exact, absolute, no wildcard, no fragment, at most 10 per list, no duplicate), each
secret, and the conflicts above. If anything is wrong, nothing is written and Umbral
exits, with each problem and what to do about it in its log (`docker logs umbral`,
or the resource's console in the Aspire dashboard). A failure while applying also
stops the start, and the whole declaration is applied in one transaction, so you
never get half of it. Fix what the log says and start again. A container that says
why it did not start is better than an app served with yesterday's redirect URIs.

**Secrets.** A web app's secret is an Aspire secret parameter,
`<umbral>-<key>-client-secret`, generated at 48 characters the first time. In run
mode Aspire keeps it in the AppHost's user secrets, so it is the same at every run
and never in your repository. `WithClientSecret(parameter)` uses your own parameter
instead. Umbral stores only a hash, as for an app made in the admin area, and never
logs the value. At each start it checks the variable against that hash; when the
parameter has changed, the secret is replaced (a rotation, recorded in the security
log), and the project that references the app gets the new value.

The secrets are ordinary environment variables. They are visible to
`docker inspect` on the Umbral container, written to a compose publish's `.env`
file, and masked only in the Aspire dashboard. Treat the Docker host, and that
`.env` file, as holding them. Umbral never generates a declared app's secret, since
the project that references the app could not know it.

**The security log.** Every change a declaration makes is a security event with the
actor `system`: the app or connector it touched and, in the detail, `declared`
followed by what changed (never a secret or a redirect URI). A start that changes
nothing records nothing, and logs one line saying how many apps were created,
updated, unchanged or handed back.

### For a production instance

Nothing in a declaration is for laptops only: with the secrets as parameters you can
publish it. Two things change:

- **Apps change only by redeploying.** The apps and the passkey connector are owned by
  the declaration, so the admin area will not change them. Edit the AppHost (or your
  compose file) and deploy again.
- **Claim before you publish with open sign-up.** Umbral applies the declaration
  before anyone has claimed it. With `PasskeySignUp.Open`, anyone who reaches an
  unclaimed instance can sign up to the declared apps. Start it, claim it at
  `/admin/setup` with the setup token, and only then make it reachable. Where
  starting is what makes it reachable, publish it with
  `PasskeySignUp.InvitationsOnly` first, claim it, then change to `Open` and
  deploy again.

Publishing also needs explicit redirect URIs, as above.

## Deploy it

`aspire publish` and `aspire deploy` need to know the address people will use, so
the public origin is required, and it must be `https://`:

```csharp
builder.AddUmbral("umbral")
    .WithDataVolume()
    .WithPublicOrigin("https://auth.example.com")
    .WithTrustedProxies("10.99.0.1");
```

`WithPublicOrigin` also takes an Aspire parameter, for an address you choose at
deploy time: `WithPublicOrigin(builder.AddParameter("umbral-origin"))`. Apps get
its value as it is, so give it exactly as Umbral will report its issuer: lowercase,
no default port, no trailing slash, for example `https://auth.example.com`.

If you declare apps, publishing needs explicit redirect URIs too: see
[Redirect URIs](#redirect-uris).

In publish mode the package never sets `ASPNETCORE_ENVIRONMENT=Development` or
`UMBRAL__INSECURE_ALLOW_HTTP`. Publishing (`aspire publish` with a compute
environment, or `aspire deploy`) without `WithPublicOrigin`, or with an `http://`
origin, fails with a message that names `WithPublicOrigin`. A string origin is
stored the way Umbral reports its issuer: scheme and host in lowercase, no default
port, no trailing slash.

Aspire does not put a reverse proxy in front of Umbral. Terminating TLS is yours,
as in [Run it for real, behind HTTPS](https.md); that page's
[Using a different reverse proxy](https.md#using-a-different-reverse-proxy) lists
what the proxy must send.

`WithTrustedProxies("10.99.0.1")` sets `UMBRAL__TRUSTED_PROXIES`, the addresses
whose `X-Forwarded-*` headers Umbral believes. Pass several as separate
arguments; they are joined with commas. See the [settings reference](settings.md).

## The image tag

The package's version is the image tag: package `X.Y.Z` runs
`ghcr.io/cosmic-chimps/umbral:X.Y.Z`, never `latest`. To run another tag, use
Aspire's own method:

```csharp
builder.AddUmbral("umbral")
    .WithImageTag("0.11.0");
```

## Your data

Everything Umbral keeps is in `/data`. `WithDataVolume()` mounts a Docker volume
there (pass a name to choose it); `WithDataBindMount("path")` mounts a folder
instead. It holds the operator credential, the connectors, the apps and the signing
keys: without it you claim Umbral again and, unless the apps are declared, register
them again. Back it up as
[Your data: back it up](backup.md) says.

## A working example

[`samples/aspire/`](../../samples/aspire/README.md) is an AppHost that runs
Umbral and the ASP.NET Core sample together.
