# Aspire sample

A [.NET Aspire](https://aspire.dev) AppHost (C#, .NET 10, Aspire 13.6.5) that
starts Umbral and the [ASP.NET Core sample](../aspnetcore/README.md) together
with one command, `aspire run`. It runs another sample; it is not a new stack.
Umbral is added with `AddUmbral`, from the hosting package
`CosmicChimps.Aspire.Hosting.Umbral` (see
[Run Umbral from an Aspire AppHost](../../docs/run/aspire.md)), referenced here
from its source. It runs the published image, `ghcr.io/cosmic-chimps/umbral`, at
the last release, with its data in a Docker volume named `umbral-aspire-data`.
The AppHost declares the sample's app and the passkey connector, so Umbral
creates them when it starts: you sign in to the sample with a passkey without
opening Umbral's admin area. This is the base of a demo on a laptop.

The declaration needs Umbral 0.12.0 or later. Until that release is published,
build the image from this repository and run that tag (see "Another image"
below).

## Before you start

- Docker.
- The .NET 10 SDK.
- The Aspire CLI (`aspire`). See the [Aspire documentation](https://aspire.dev)
  for the install command of your platform.

The Umbral here runs with the laptop settings of the quick start,
`ASPNETCORE_ENVIRONMENT=Development` and `UMBRAL__INSECURE_ALLOW_HTTP=true`, on
plain `http://localhost:8080`: **for a laptop only, never expose it to a
network.** See the
[quick start](../../README.md#quick-start-a-first-login-on-your-own-machine).

## How to run it

From this folder:

```sh
aspire run
```

The console prints the address of the Aspire dashboard, with its login token:
open it. Umbral starts first; the sample starts once Umbral is healthy. Open the
sample at `http://localhost:5000` and sign in: on Umbral's sign-in page choose
**Passkey**, create your account with a passkey, and you are back in the sample,
signed in. A second `aspire run` changes nothing in Umbral: the app and the
connector are already as declared.

**If a port is taken.** The ports stay fixed, only the default changes. Set
`Umbral:Port` (default 8080) or `Sample:Port` (default 5000) in the AppHost's
user secrets, for example `dotnet user-secrets set Sample:Port 5001`. The
declared redirect URIs follow the sample's port, and the next start applies
them. On macOS, AirPlay Receiver listens on 5000: turn it off in System
Settings, or use another port for the sample.

**Another image.** `Umbral:ImageTag` runs another tag of
`ghcr.io/cosmic-chimps/umbral`. To run this repository's code, from the
repository's root:

```sh
docker build -t ghcr.io/cosmic-chimps/umbral:dev .
cd samples/aspire && dotnet user-secrets set Umbral:ImageTag dev
```

## Umbral's admin area

Signing in to the sample needs no claim. To open Umbral's admin area, claim it
once per `umbral-aspire-data` volume: open `http://localhost:8080/admin/setup`
(the **Claim (first run)** link of the `umbral` resource in the dashboard) and
paste the **Setup token** from Umbral's console logs, then choose an
**Operator credential**. Or, from a shell:
`docker exec $(docker ps -q -f volume=umbral-aspire-data) /app/Umbral reset-credential`.
It claims Umbral and prints a password to sign in with. The filter is this
sample's volume, which only its container mounts, so it never reaches another
Umbral on the machine, such as the quick start's.

On **Apps**, the sample's app is marked **Declared**: its addresses and sign-in
methods come from `AppHost.cs` and change there. Its branding is changed in the
admin area.

**From an earlier version of this sample.** A volume set up with the old steps
already has the passkey connector with **Anyone can sign up**: Umbral takes it
over as declared. The app you created by hand is left as it is, with its own
client ID; you can delete it on **Apps**, and remove the
`Parameters:umbral-client-id` and `Parameters:umbral-client-secret` user
secrets, which nothing reads any more.

## What you should see

- The Aspire dashboard lists `umbral` at `http://localhost:8080` and `sample`
  at `http://localhost:5000`, both **Running**, with the declared app
  `umbral-sample` under `umbral`.
- On the sample, `/` shows a heading, the links **Public** and **Private**, and
  **Sign in**.
- `/public` opens for anyone.
- `/private` sends you to Umbral's sign-in page, titled **Sign in to** and the
  app's name. After you sign in, it shows a table with `sub`, `name` and `idp`,
  and `email` and `email_verified` when Umbral has an address for you.
- **Sign out** goes through Umbral and comes back to `/`, which shows **Sign in**
  again. `/private` then sends you to Umbral again.

## How it works

All of it is in [`AppHost.cs`](AppHost.cs).

- **Umbral.** `AddUmbral("umbral", port: ...)` runs
  `ghcr.io/cosmic-chimps/umbral` at the package's version, which from source is
  the last release in [`CHANGELOG.md`](../../CHANGELOG.md), on the fixed port 8080,
  with `UMBRAL__PUBLIC_ORIGIN` set to `http://localhost:8080` and the laptop pair
  named above. `WithDataVolume("umbral-aspire-data")` keeps `/data`. Aspire waits
  for its `/healthz`.
- **The project reference.** The sample is `../aspnetcore`, run as it is. It
  reads `PORT` and listens there, so the AppHost pins it to 5000, the port its
  declared redirect URIs are taken from.
- **The declaration.** `WithPasskeys(PasskeySignUp.Open)` declares the passkey
  connector, and `AddApp("sample")` the sample's app, with client ID `sample`.
  Umbral receives them as `UMBRAL__DECLARATION` and applies them at every start
  (see [Declared apps](../../docs/run/aspire.md#declared-apps)).
- **The credentials.** `WithReference(sampleApp)` gives the sample
  `UMBRAL_ISSUER` (Umbral's public origin, so the sample and the container
  cannot disagree), `UMBRAL_CLIENT_ID` and `UMBRAL_CLIENT_SECRET`. The secret is
  an Aspire secret parameter, `umbral-sample-client-secret`, generated on the
  first run and kept in the AppHost's user secrets, never in a file of the
  repository.
- **The redirect URIs** are the sample's address with `/signin-oidc` and
  `/signout-callback-oidc`, the paths the sample uses.
- **Nothing is deployed.** The AppHost is for `aspire run` on your machine. It
  sets no public origin, so publishing it is refused: a deployment needs
  `WithPublicOrigin` with an https address (see
  [Run Umbral from an Aspire AppHost](../../docs/run/aspire.md#deploy-it)).
