# Data Model: The Umbra Look (spec 007)

No table, no column, no file in the data directory changes.

## Theme choice (browser only)

| Field | Values | Where |
| --- | --- | --- |
| `umbral_theme` cookie | `light` \| `dark`; absent means System | the person's browser, Path `/`, 365 days |

In code: `type ThemeChoice = System | Light | Dark` (`Views/Theme.fs`).

- `Theme.ofRequest : HttpContext -> ThemeChoice`: exact `light` / `dark`,
  otherwise `System`.
- `Theme.attr : ThemeChoice -> string`: `system` / `light` / `dark`.
- `Theme.parseChoice : string -> ThemeChoice option`: the posted value
  (`system` / `light` / `dark`), anything else `None`.

Transitions: System → Light/Dark (cookie set); Light ↔ Dark (cookie replaced);
Light/Dark → System (cookie deleted). An unknown posted value changes nothing.

## Accent (spec 006, amended)

`Accent.lowContrastOnCard` is true when the accent is below 3:1 against the
dark card `#14141f` **or** the light card `#fffdf8`.
