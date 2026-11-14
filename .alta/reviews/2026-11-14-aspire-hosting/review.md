# Documentation review: the Aspire hosting package (task a5pr)

Branch `alta/sturdy-partridge-kwqh`, commit 0d972e6 on main 7d90362. Reviewed 2026-11-14 by the
documentation reviewer, against the package's code (`UmbralResourceBuilderExtensions.cs`,
`UmbralOrigin.cs`, the csproj), Umbral's `PublicOrigin.fs`, `release.yml`, `pages.yml` and `notes.md`.

## Verdict

**Not clear yet: two blocking items, four nits.** The page is well placed and accurate about what the
package sets. The run-and-publish split, the laptop pair, the image tag and the data volume all match
the code. Both blocking items are about a reader following the page and failing: once with a
container app, and once before the package exists. One re-check after the fixes.

## What was checked

- **The laptop pair** is set only for plain http on `localhost`, `127.0.0.1` or `[::1]`. The package
  (`UmbralOrigin.IsLoopbackHttp`) and Umbral (`PublicOrigin.fs:77-82`) accept the same hosts. Right.
- **Publish mode**: no pair, a string origin must be `https://`, and with none the publish fails with
  a message naming `WithPublicOrigin`. A parameter origin passes through unchecked, and Umbral refuses
  a bad one at start. Right.
- **`WithReference`** sets `UMBRAL_ISSUER` to the public origin and adds no `services__` variable.
  **`WithTrustedProxies`** joins entries with commas. **`WithDataVolume`** and **`WithDataBindMount`**
  mount `/data`. The **Admin** link goes to `/admin`, and Aspire waits for `/healthz`. Right.
- **The image tag** is the package version (`-p:Version` in the release job), otherwise the last
  released `## X.Y.Z` of `CHANGELOG.md`. Never `latest`. Right.
- **The NuGet readme**, **samples/aspire/README.md**, the entries in **samples/README.md** and
  **README.md**, the **sidebar** entry under "Run it", and the **changelog fragment**: right, apart
  from the nits.
- **The maintainer's one-time steps.** The trusted-publishing policy (owner, repository, workflow
  file, environment), the `nuget` environment with the same deployment rule as `production`, the
  `NUGET_USER` variable, and the optional prefix reservation. They match the `nuget` job: it runs
  after `image`, has `id-token: write` only, and is skipped without the variable. Right.
- `check-links.py`: all links resolve. `changelog.sh check`: in order.

## Blocking

### B1. In run mode, `UMBRAL_ISSUER=http://localhost:<port>` is unreachable from a container app

The page says `WithReference(umbral)` gives the app the public origin, "never its internal address
(`http://umbral:8080`): an app checks the issuer against exactly what Umbral writes in tokens". That
is right for **checking** the issuer. But an app also **fetches** the discovery document and the
signing keys from that address. In run mode the address is `http://localhost:8080`, which reaches
Umbral only from the host. An app added with `AddProject` runs on the host and works, as the sample
does. An app added with `AddContainer` or `AddDockerfile` resolves `localhost` to itself, and its
first sign-in fails to load the discovery document, with nothing on the page to explain why.

Say it on the page, under "Run it on your machine", and in the NuGet readme's `WithReference`
section, in one sentence each. For example:

> In run mode the issuer is `http://localhost:<port>`, which an app running on your machine (an
> `AddProject` resource) reaches. An app in a container cannot: `localhost` there is the container
> itself. Run that app as a project while you develop, or deploy both and use the public origin.

If the package should support container apps in run mode later, that is a design question for the
maintainer, because the issuer must still equal what Umbral writes in tokens. It is not this page's.

### B2. The page goes live before the package exists

The docs site deploys on every push to `main` (`pages.yml`). The package is pushed to nuget.org only
by the first release that carries the `nuget` job, and only after the maintainer's one-time steps.
Merged before that release, the page tells readers to `dotnet add package
CosmicChimps.Aspire.Hosting.Umbral`, which fails, and the README and samples/README link to it.

Either the maintainer merges this right before the release that publishes the package, or the page
says when it exists. For example, after the install command: "The package is published with each
Umbral release from 0.12.0 on." The maintainer should confirm the version number. The first is
cleaner; the second is safe whatever the order.

## Nits

1. **`WithPublicOrigin` in run mode.** The table opens with "In run mode, with no public origin
   given", but does not say what happens when one is given. Then that origin applies in run mode too:
   the laptop pair goes, apps get that address as the issuer, and on a laptop an `https://` address
   usually does not reach the container. One sentence: "An origin given with `WithPublicOrigin` is
   used in run mode too, so for `aspire run` on a laptop leave it out."
2. **"`aspire publish` … fails with a message".** Your notes say `aspire publish` alone does nothing on
   an AppHost with no compute environment. The refusal comes when a publisher actually builds the
   output (the manifest, a deployment target). Say "Publishing (`aspire publish` with a compute
   environment, or `aspire deploy`) without `WithPublicOrigin` … fails".
3. **"an OIDC server".** The NuGet readme and the csproj's `Description` call Umbral "an OIDC server" and
   "a small OIDC server". The README and PRODUCT.md say "a self-hosted OpenID Connect provider".
   NuGet's search shows the description first, so use the product's own words there.
4. **PRODUCT.md** (Operating Context) says tagged releases go to GHCR and the README covers Caravana.
   Add the package as the other way the image is run: "…and the `CosmicChimps.Aspire.Hosting.Umbral`
   package runs that image from a .NET Aspire AppHost."
