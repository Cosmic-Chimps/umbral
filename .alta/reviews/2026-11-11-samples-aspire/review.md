# Documentation review: the Aspire sample (task r5hd)

Branch `alta/friendly-jaguar-xpjv`, `0bc654d..cb86f70` (sample 7af4a1d). Reviewed 2026-11-13 by the
documentation reviewer, against the code, `notes.md` and the ASP.NET Core sample.

## Verdict

**Not clear yet: one blocking item, four nits**, plus one condition on the base, not on the content
(see "The base"). The README is short and exact, and the two extras (port overrides, the dashboard
notification) are well judged and explained. One re-check after the fix.

## What was checked

- **Every admin label** in `samples/aspire/README.md` and in the AppHost's `steps` against
  `Texts.fs`, by script: **Setup token**, **Operator credential**, **Connectors**, **Passkey**,
  **Anyone can sign up**, **Enable passkeys**, **Apps**, **Create app**, **Name**, **App type**,
  **Web application**, **Redirect URI**, **Post-logout redirect URI (optional)**, **Client ID**,
  **Client secret**, **Sign in**, **Sign out**. All verbatim. **Sign in to** is `PickerTitleForApp`
  with the app's name, as the README says.
- **What you should see**, against `samples/aspnetcore/Program.cs`: the heading, **Public** and
  **Private**, the table of `sub`, `name`, `idp`, `email`, `email_verified`. Right.
- **How it works**, against `AppHost.cs`: the image and pinned tag, the fixed ports with their
  overrides, the laptop pair, the volume at `/data`, `/healthz` (`Handlers/Health.fs`), `PORT`,
  `UMBRAL_ISSUER` from the endpoint, the parameters only when set, and the notification. Right.
- **The README sentence** (seven to eight), **the samples table row and sentence**, **the
  changelog fragment**: right, apart from N3 and N4.
- `check-links.py`: 400 links in 39 files, all resolve. `changelog.sh check`: in order.

## Blocking

### B1. `docker ps -q -f name=umbral` can reach the wrong Umbral

Step 1 (in the README and in the dashboard's steps) offers
`docker exec $(docker ps -q -f name=umbral) /app/Umbral reset-credential`. Docker's `name` filter
matches any container whose name **contains** `umbral`. The main README's quick start names its
container exactly `umbral`, so a reader who tried the quick start first has two matches:

- both running: `docker exec <id1> <id2> /app/Umbral …` runs `<id2>` as the command, and it fails;
- only the quick-start one running (the Aspire one stopped, or not yet up): it runs
  `reset-credential` **in the other instance**. On a claimed instance that replaces its operator
  credential.

Select the container by this sample's own volume, which only it mounts:

```sh
docker exec $(docker ps -q -f volume=umbral-aspire-data) /app/Umbral reset-credential
```

Make the same change in `AppHost.cs`'s `steps` (with `{VolumeName}`).

## Nits

1. **Say what `reset-credential` gives you.** "Or, from a shell: …" replaces both pasting the token
   and choosing a credential. On an unclaimed instance it claims it and prints a password, once
   (README, "Claim an instance from a script"). Add: "It claims Umbral and prints a password to
   sign in with."
2. **`Umbral:Port` moves more than the redirect URIs.** "If a port is taken" says to register the
   redirect URIs with the new port, which covers `Sample:Port`. With `Umbral:Port` set, the setup
   page, the dashboard address and the issuer move too: "…and with `Umbral:Port`, open Umbral at that
   port instead of 8080 everywhere below". The dashboard's steps already use the real values.
3. **The changelog says "printed the first time".** They are shown in the dashboard as a
   notification, and written to the AppHost's log. Say "shown in the Aspire dashboard the first time".
4. **The main README's CI sentence now follows the Aspire mention.** "…and an Aspire AppHost that
   runs Umbral and the ASP.NET Core sample together. CI runs each web sample against a real Umbral
   before every release." This can read as if CI walks the Aspire sample against Umbral. Its
   workflow builds it, as `samples/README.md` says correctly. One clause: "…together (CI builds it)."

## The base

This branch starts at local `main` 0bc654d. That main contains the f0rg branch merged with its whole
history, and that history holds material the f0rg documentation review asked to keep out of the
public repository until the maintainer decides on a report to Forgejo. GitHub does not have those
commits. Do not push this branch as it is. Once the maintainer has put f0rg on main as one squashed
commit, rebase 7af4a1d and cb86f70 onto that main.
