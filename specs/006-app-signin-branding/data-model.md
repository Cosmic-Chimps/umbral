# Data Model: Per-App Sign-In Branding

**Feature**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

## Schema change (Umbral lane, `Infrastructure/Database.fs`)

`SchemaVersion` 3 → **4**. The table is added to `schema` (so a fresh database
gets it) and step 4 in `upgradeSteps` runs the same statement for an existing
database (research R6, R10).

```sql
CREATE TABLE IF NOT EXISTS app_branding (
    client_id     TEXT PRIMARY KEY,
    accent_color  TEXT NULL
        CHECK (accent_color IS NULL
               OR (length(accent_color) = 6 AND accent_color NOT GLOB '*[^0-9a-f]*')),
    logo_png      BLOB NULL,
    logo_sha256   TEXT NULL UNIQUE,
    logo_width    INTEGER NULL,
    logo_height   INTEGER NULL,
    updated_at    TEXT NOT NULL,
    CHECK ((logo_png IS NULL) = (logo_sha256 IS NULL)),
    CHECK ((logo_png IS NULL) = (logo_width IS NULL) AND (logo_png IS NULL) = (logo_height IS NULL))
);
```

No foreign key into OpenIddict's tables, like `app_connector_choices`. The two
lanes never join (`Database.fs` header).

| Column | Meaning | Rules |
| --- | --- | --- |
| `client_id` | The app, by OpenIddict client id | One row per app at most |
| `accent_color` | The accent as six lowercase hex digits, no `#` | Canonicalised on save: `#F26B1D` is stored as `f26b1d` |
| `logo_png` | The rewritten PNG (research R2), never the request body | 32–512 px each side, ≤ 1.25 MB |
| `logo_sha256` | Lowercase hex SHA-256 of `logo_png`. The logo's address | Unique, so it can be looked up by address |
| `logo_width`, `logo_height` | From `IHDR`, for the `<img>` width and height attributes (no layout shift) | Present exactly when there is a logo |
| `updated_at` | ISO-8601 UTC, from the injected `TimeProvider` | |

**Row lifecycle**: a row exists while the app has a logo, an accent, or both.

```text
(no row) --set logo or accent--> (row: logo and/or accent)
(row)    --replace logo-------> (row: new logo_png, new logo_sha256)
(row)    --remove logo / clear accent, other part still set--> (row)
(row)    --remove the last remaining part--> (no row)
(any)    --app deleted--> (no row)
```

## Domain types (`Domains/Branding.fs`, `Domains/PngLogo.fs`)

Pure, with no I/O, placed with the other `Domains/` modules.

```fsharp
/// Six lowercase hex digits. Only `Accent.parse` makes one.
type Accent = private Accent of string
module Accent =
    val parse : string -> Result<Accent, AccentError>   // accepts "#rrggbb", either case; trims
    val hex   : Accent -> string                        // "f26b1d"
    val rgb   : Accent -> int * int * int
    val onAccent : Accent -> OnAccent                   // Black | White, by WCAG contrast (R4)
    val contrastWith : Accent -> (int * int * int) -> float
    val lowContrastOnCard : Accent -> bool              // < 3.0 against #14141f or #fffdf8 (FR-017, amended by 007; was #11161d)

type AccentError = | NotHexColour   // "#rrggbb" expected, e.g. "#f26b1d"

/// A validated, rewritten logo.
type Logo = { Png: byte[]; Sha256: string; Width: int; Height: int }

type LogoError =
    | Empty
    | TooLarge of bytes: int64            // > 1.25 MB stored, or over the request limit
    | NotPng                              // signature missing: JPEG/WebP/SVG/anything sent without the script
    | Corrupt                             // bad length, CRC, IHDR fields, IDAT size, filter byte
    | Interlaced
    | UnsupportedChunk of name: string    // unknown critical chunk
    | TooSmall of width: int * height: int  // < 32 either side
    | TooBig of width: int * height: int    // > 512 either side

module PngLogo =
    val maxSide : int       // 512
    val minSide : int       // 32
    val maxBytes : int      // 1_310_720
    val validate : byte[] -> Result<Logo, LogoError>
```

`Branding` as the picker and the Apps screen see it:

```fsharp
type AppBranding =
    { Accent: Accent option
      Logo: LogoRef option }          // address and size, not bytes
and LogoRef = { Sha256: string; Width: int; Height: int }
```

## Store (`Infrastructure/BrandingStore.fs`)

All SQL is literal (MetaTests rule 3).

| Function | Purpose |
| --- | --- |
| `forApp config clientId : Task<AppBranding option>` | Picker and Apps card. Never reads the BLOB |
| `allForApps config : Task<Map<string, AppBranding>>` | One read for the whole Apps screen |
| `logoBytes config sha256 : Task<byte[] option>` | The logo endpoint |
| `setLogo config now clientId (logo: Logo) : Task<unit>` | Upsert. Replaces the BLOB, hash and size in one statement |
| `removeLogo config now clientId : Task<unit>` | Clears the logo. Deletes the row if no accent is left |
| `setAccent config now clientId (Accent option) : Task<unit>` | Upsert, or clear. Deletes the row if nothing is left |
| `deleteFor config clientId : Task<unit>` | Called by `AppDelete` |

A replaced logo's old bytes go when the row is overwritten, so nothing else is
needed for "no longer kept" (FR-020).

## Security events (no new type)

| Action | `EventType` | `Detail` |
| --- | --- | --- |
| Logo saved (new or replacement) | `AppUpdated` | `branding_logo_set` |
| Logo removed | `AppUpdated` | `branding_logo_removed` |
| Accent saved or changed | `AppUpdated` | `branding_accent_set` |
| Accent cleared | `AppUpdated` | `branding_accent_cleared` |

`ClientId`, `RemoteIp` and `Actor.Operator` are always set. App deletion keeps
its existing `AppDeleted` event and does not log the branding separately.
