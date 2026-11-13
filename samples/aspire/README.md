# Aspire sample

A [.NET Aspire](https://aspire.dev) AppHost (C#, .NET 10, Aspire 13.6.5) that
starts Umbral and the [ASP.NET Core sample](../aspnetcore/README.md) together
with one command, `aspire run`. It runs another sample; it is not a new stack.
Umbral comes from its published image, `ghcr.io/cosmic-chimps/umbral`, at a
pinned release (`0.11.0`), with its data in a Docker volume named
`umbral-aspire-data`, so the claim, the connector and the app survive a restart.
This is the base of a demo on a laptop.

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
open it. The first run starts Umbral only, because the sample's client ID and
secret are not set yet, and the dashboard shows the steps below at the top, as
a notification (they are also in the AppHost's log).

**If a port is taken.** The ports stay fixed, only the default changes. Set
`Umbral:Port` (default 8080) or `Sample:Port` (default 5000) in the same user
secrets, for example `dotnet user-secrets set Sample:Port 5001`, and register the
redirect URIs below with that port instead; with `Umbral:Port`, open Umbral at
that port instead of 8080 everywhere below (the setup page and the issuer move
with it). The steps in the dashboard already use the real values. On macOS, AirPlay Receiver listens on
5000: turn it off in System Settings, or use another port for the sample.

## What to create in Umbral

Once per `umbral-aspire-data` volume:

1. **Claim Umbral.** Open `http://localhost:8080/admin/setup` and paste the
   **Setup token** from Umbral's console logs (the `umbral` resource in the
   dashboard), then choose an **Operator credential**. Or, from a shell:
   `docker exec $(docker ps -q -f volume=umbral-aspire-data) /app/Umbral reset-credential`.
   It claims Umbral and prints a password to sign in with. The filter is this
   sample's volume, which only its container mounts, so it never reaches another
   Umbral on the machine, such as the quick start's.
2. On **Connectors**, choose **Passkey**, pick **Anyone can sign up** so that
   you can create your own account from the sign-in page, and press
   **Enable passkeys**. A passkey needs no account at any provider (see
   [Passkeys](../../docs/connectors/passkey.md)); any other connector works too,
   once it is registered with its provider.
3. On **Apps**, press **Create app** with:
   - **Name**: anything.
   - **App type**: **Web application**
   - **Redirect URI**: `http://localhost:5000/signin-oidc`
   - **Post-logout redirect URI (optional)**: `http://localhost:5000/signout-callback-oidc`

   Then copy the **Client ID** and the **Client secret**. The secret is shown
   once.
4. Give them to the AppHost, from this folder:

   ```sh
   dotnet user-secrets set Parameters:umbral-client-id <client id>
   dotnet user-secrets set Parameters:umbral-client-secret <client secret>
   ```

5. Stop `aspire run` and start it again.

The ports are fixed because Umbral's public origin and the sample's redirect URI
are exact addresses, and Aspire's random ports would break both. The secret goes
to user secrets only, never to a file in the repository.

## What you should see

- The Aspire dashboard lists two resources, both **Running**: `umbral` at
  `http://localhost:8080` and `sample` at `http://localhost:5000`.
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

- **The container.** Umbral runs from `ghcr.io/cosmic-chimps/umbral:0.11.0` on
  the fixed port 8080, with `UMBRAL__PUBLIC_ORIGIN` set to
  `http://localhost:8080`, the laptop pair named above, and the volume
  `umbral-aspire-data` at `/data`. Aspire waits for its `/healthz`.
- **The project reference.** The sample is `../aspnetcore`, run as it is. It
  reads `PORT` and listens there, so the AppHost pins it to 5000, the port its
  redirect URI names.
- **The issuer.** `UMBRAL_ISSUER` comes from Umbral's endpoint, so the sample
  and the container cannot disagree.
- **The parameters.** `umbral-client-id` and `umbral-client-secret` (the second
  marked secret) are read from user secrets and passed as `UMBRAL_CLIENT_ID` and
  `UMBRAL_CLIENT_SECRET`.
- **Added once set.** The sample is added only when both are set, because it
  refuses to start without them and an unset parameter would make Aspire prompt
  for it. Otherwise the AppHost shows the steps in the dashboard, as a
  notification through Aspire's interaction service, and writes them to its
  console (`aspire run` keeps that in its log file).
- **Nothing is deployed.** There is no publish mode here: the AppHost is for
  `aspire run` on your machine.
