# Implementation Plan: "Last used" on the sign-in page

**Branch**: `alta/loyal-oder-ccph` | **Date**: 2026-10-21 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/009-last-used-connector/spec.md`

## Summary

After a successful end-user sign-in, Umbral sets one cookie,
`umbral_last_connector`, holding the connector's slug. The sign-in picker reads
it and, when it names one of two or more connectors the page offers, puts that
button first with a **Last used** badge and an "or" divider under it. Nothing is
stored on the server and nothing new is logged.

The cookie is written in `Sessions.signIn`, the one function every end-user
session goes through (provider callback, passkey sign-in, passkey sign-up and
invitation). The operator's admin sign-in uses `AdminAuth` and never reaches it,
and nothing that fails or is refused reaches it either. See
[research.md](research.md) R1.

## Technical Context

**Language/Version**: F# on .NET 10 (as the rest of `src/Umbral`)

**Primary Dependencies**: ASP.NET Core cookies, Oxpecker view engine; no new package

**Storage**: none (a browser cookie)

**Testing**: xUnit through the real endpoints (`tests/Umbral.Tests`, `Fixtures.TestApp`), plus a unit test of the ordering rule

**Target Platform**: the Umbral container (linux/amd64, linux/arm64)

**Project Type**: web service with server-rendered pages

**Performance Goals**: no measurable change; the picker reads one request cookie

**Constraints**: no script reads the cookie; Tailwind CSS regenerated with `build-css.sh` (CI and Docker run `--check`)

**Scale/Scope**: one new infrastructure file, edits to `Sessions.fs`, `ThemeChoice.fs`, `Picker.fs`, `Texts.fs`, `umbral.input.css`; one new test file

## Constitution Check

*Checked before research and again after design: both pass.*

| Gate | Answer |
| --- | --- |
| Concept count | No new noun. A hint on the existing sign-in page; the badge says "Last used", which names no thing. |
| Single artifact | No dependency. No state outside the browser or the data directory: the cookie lives in the browser only. |
| Protocol safety | Nothing about tokens, signatures or keys. The cookie is not a credential and is never trusted for anything but the order of buttons. |
| Secure default | `HttpOnly`, `SameSite=Lax`, `Secure` over HTTPS, path `/`; holds only a slug (Principle IV), no new security-log event. The value is matched against offered slugs and never written into a page. |
| Documentation | README paragraph next to `umbral_theme`; CHANGELOG `### Added` (tasks T009, T010). |
| Scope | Not on the Principle VII list. Not account management: the hint is per browser, not per user. |

## Project Structure

### Documentation (this feature)

```text
specs/009-last-used-connector/
├── spec.md
├── plan.md              # this file
├── research.md          # R1–R6
├── data-model.md        # the cookie, the ordering rule
├── quickstart.md        # how to see it working
├── contracts/
│   └── last-used-cookie.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Umbral/
├── Infrastructure/
│   ├── BrowserCookies.fs     # NEW: the hint cookies' options; remember/read the last connector
│   └── Sessions.fs           # signIn also remembers the slug
├── Handlers/
│   ├── ThemeChoice.fs        # uses BrowserCookies.options (no behaviour change)
│   └── Connect/Picker.fs     # `arrange` + the first button, badge and divider
├── Views/Texts.fs            # LastUsedBadge, LastUsedName, PickerOr
├── Assets/umbral.input.css   # .um-last-used, .um-or
└── Assets/umbral.css         # regenerated
tests/Umbral.Tests/
└── LastUsedConnectorTests.fs # NEW
README.md, CHANGELOG.md
```

**Structure Decision**: the existing single web project. `BrowserCookies.fs` is
compiled before `Sessions.fs` (it needs nothing but ASP.NET Core).

## Complexity Tracking

No violations.
