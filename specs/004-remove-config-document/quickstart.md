# Quickstart: Validating the Removal

Run these after implementation, on the branch that holds 003's merged result.
The contract they check is [contracts/removed-surface.md](./contracts/removed-surface.md).

## V0. Inventory is empty (before and after)

Before starting, this lists the work (research R1). Afterwards it must print
only the allowed hits: superseded notes in `specs/001-*`, `specs/002-*` and
`specs/003-*`, this feature's own folder, and the constitution and vision
records.

```bash
grep -rn -E "ConfigFile|ConfigDocument|ConfigExport|CONFIG_FILE|umbral\.yaml|hash-credential|applyCredentialHash|isCredentialHash|ConfigApplied|config_applied|ExtraScopes|extra_scopes|/admin/config" \
  src tests docs README.md Dockerfile docker-compose.yml .specify/memory \
  --include='*.fs' --include='*.fsproj' --include='*.md' --include='*.yml' --include='Dockerfile' \
  | grep -v -E '/(bin|obj)/'
```

`YamlDotNet` must appear only in `tests/Umbral.Tests/Umbral.Tests.fsproj` and
`tests/Umbral.Tests/MetaTests.fs`.

## V1. Build and the whole test suite

```bash
dotnet build        # warnings are errors (Directory.Build.props)
dotnet test         # from the repo root
```

**Expect**: green. The moved redirect URI theories (`RedirectUriTests.fs`) and
the event coverage meta-test pass.

## V2. A file in the data directory is ignored (SC-001, User Story 1)

```bash
mkdir -p ./v2-data && sudo chown 1654:1654 ./v2-data
printf 'version: 1\nconnectors:\n  - slug: github\n    kind: github\n    client_id: "x"\n    client_secret: oops-a-literal\n' > ./v2-data/umbral.yaml
docker run -d --name umbral-v2 -p 8080:8080 -v "$PWD/v2-data":/data \
  -e UMBRAL__PUBLIC_ORIGIN=http://localhost:8080 \
  -e UMBRAL__CONFIG_FILE=/data/umbral.yaml umbral
docker logs umbral-v2 2>&1 | grep -i -E "yaml|configuration file" ; echo "exit=$?"
```

**Expect**: the container stays up (the literal secret would have stopped the
old version). No log line mentions the file (`exit=1` from grep). The
first-run banner is printed, so the instance is unclaimed. `/admin/connectors`
lists no GitHub connector once you have claimed it. `./v2-data/umbral.yaml` is
unchanged.

## V3. Admin changes survive restarts (SC-001)

Claim the instance and create a connector, then disable it.
`docker restart umbral-v2` three times.

**Expect**: the connector is still disabled each time.

## V4. Export is gone (FR-002)

```bash
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:8080/admin/config
curl -s -o /dev/null -w '%{http_code}\n' -b "$ADMIN_COOKIE" http://localhost:8080/admin/config
```

**Expect**: `404` both times.

## V5. Unattended claim (User Story 2)

```bash
docker run -d --name umbral-v5 -p 8081:8080 -v umbral-v5:/data \
  -e UMBRAL__PUBLIC_ORIGIN=http://localhost:8081 umbral
docker exec umbral-v5 /app/Umbral reset-credential
```

**Expect**: "This instance had no operator yet. It is now claimed", and a
credential printed once. `/admin/signin` accepts it. `/admin/setup` no longer
asks for a token.

## V6. Scopes are the kind's own (FR-007)

Covered by the connector runtime tests. By hand: start a sign-in through a
GitHub connector and read the `scope` parameter of the redirect to GitHub.

**Expect**: exactly the GitHub kind's required scopes.

## V7. Move by copying the data directory (SC-003, User Story 3)

Follow the README's backup steps on `umbral-v5`, restore the copy into a new
volume, and start a second container on it with the same
`UMBRAL__PUBLIC_ORIGIN`.

**Expect**: the same connectors, apps and users. An app's existing client
secret still works at the token endpoint. A browser session that was signed in
stays signed in.

## V8. Documentation and governance (SC-004, SC-005)

- `README.md`: no configuration section and no `UMBRAL__CONFIG_FILE` row. It
  has a note on claiming from a script and says that copying the data
  directory is how to move an instance.
- `.specify/memory/constitution.md`: version `2.0.0`, Principle V marked
  removed, no "Config parity" row.
- `docs/vision.md`: the YAML row is marked dropped.
