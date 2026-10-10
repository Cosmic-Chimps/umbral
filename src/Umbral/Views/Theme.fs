/// Design tokens as F# data (research.md D10): no frontend build step, no
/// wwwroot. Tailwind comes from its CDN and is configured at runtime from the
/// token lists below; `tokensCss` emits the same values as CSS custom
/// properties for the handful of places Tailwind utilities can't reach.
module Umbral.Views.Theme

open System

// ---------------------------------------------------------------------------
// Umbral's own palette: calm, dark, legible. A security product's chrome
// should disappear — the one colour that should draw the eye is the one
// warning the operator about something destructive.
// ---------------------------------------------------------------------------

let colors =
    [ "surface", "#0d1117"
      "surface-container-lowest", "#090c10"
      "surface-container-low", "#11161d"
      "surface-container", "#161c25"
      "surface-container-high", "#1f2631"
      "surface-container-highest", "#2a3340"
      "on-surface", "#e6e9ef"
      "on-surface-variant", "#9aa7b8"
      "outline", "#5b6b7f"
      "outline-variant", "#2d3845"
      "primary", "#5fb3e8"
      "on-primary", "#07263b"
      "primary-container", "#1f6fa8"
      "secondary", "#7fd8c4"
      "secondary-container", "#1d4a40"
      "success", "#7fd8a0"
      "warning", "#f0c85a"
      "error", "#f28b82"
      "on-error", "#430806"
      "error-container", "#7a211b"
      "on-error-container", "#ffdad4" ]

/// size, line height, weight, tracking
let typeStyles =
    [ "display", ("34px", "40px", "700", "-0.02em")
      "headline-lg", ("24px", "32px", "700", "-0.015em")
      "headline-md", ("20px", "28px", "600", "-0.01em")
      "body-lg", ("16px", "24px", "400", "0em")
      "body-md", ("14px", "20px", "400", "0em")
      "label-md", ("13px", "18px", "600", "0.01em")
      "label-sm", ("11px", "14px", "700", "0.06em") ]

let radii =
    [ "sm", "0.25rem"
      "default", "0.5rem"
      "md", "0.75rem"
      "lg", "1rem"
      "full", "9999px" ]

let spaces =
    [ "xs", "0.25rem"
      "sm", "0.5rem"
      "md", "1rem"
      "lg", "1.5rem"
      "xl", "2rem" ]

let effects =
    [ "--border-micro", "1px solid rgb(255 255 255 / 0.06)"
      "--border-ghost", "1px solid rgb(255 255 255 / 0.12)"
      "--shadow-level-1", "0 1px 2px 0 rgb(0 0 0 / 0.4)"
      "--shadow-level-2", "0 10px 28px -8px rgb(0 0 0 / 0.55)"
      "--font-family", "ui-sans-serif, system-ui, -apple-system, \"Segoe UI\", sans-serif"
      "--font-mono", "ui-monospace, \"SF Mono\", \"JetBrains Mono\", monospace" ]

/// Every custom property this theme defines
let tokens: (string * string) list =
    [ for name, value in colors -> $"--color-{name}", value
      for name, (size, line, weight, tracking) in typeStyles do
          yield $"--text-{name}-size", size
          yield $"--text-{name}-line", line
          yield $"--text-{name}-weight", weight
          yield $"--text-{name}-tracking", tracking
      for name, value in radii -> $"--radius-{name}", value
      for name, value in spaces -> $"--space-{name}", value
      yield! effects ]

let tokensCss =
    let declarations = tokens |> List.map (fun (name, value) -> $"{name}: {value};") |> String.concat " "
    $":root {{ color-scheme: dark; {declarations} }}"

// ---------------------------------------------------------------------------
// Tailwind (CDN, no build step): utilities such as bg-surface-container-low,
// text-on-surface-variant or rounded-lg resolve to the tokens above. Rule:
// class names are never built by interpolation — only literal strings are
// written in views, so the CDN's static scanner always sees them.
// ---------------------------------------------------------------------------

let private js (s: string) = "'" + s.Replace("'", "\\'") + "'"

let tailwindConfig =
    let colorEntries = colors |> List.map (fun (n, v) -> $"{js n}: {js v}") |> String.concat ", "

    let fontSizes =
        typeStyles
        |> List.map (fun (n, (size, line, weight, tracking)) ->
            $"{js n}: [{js size}, {{ lineHeight: {js line}, fontWeight: {js weight}, letterSpacing: {js tracking} }}]")
        |> String.concat ", "

    let radiusEntries =
        radii
        |> List.map (fun (n, v) -> $"""{js (if n = "default" then "DEFAULT" else n)}: {js v}""")
        |> String.concat ", "

    $"""tailwind.config = {{ theme: {{ extend: {{
  colors: {{ {colorEntries} }},
  fontSize: {{ {fontSizes} }},
  borderRadius: {{ {radiusEntries} }},
  fontFamily: {{ sans: ['ui-sans-serif', 'system-ui', 'sans-serif'], mono: ['ui-monospace', '"SF Mono"', 'monospace'] }},
  maxWidth: {{ page: '1120px' }},
  boxShadow: {{ 'level-1': 'var(--shadow-level-1)', 'level-2': 'var(--shadow-level-2)' }}
}} }} }};"""

/// `.um-*` components (Views/Components.fs). Umbral's analogue of a design
/// system's component layer — the reference project calls these `.fr-*`.
let componentCss =
    """
@layer base {
  html { background: var(--color-surface); -webkit-font-smoothing: antialiased; }
  body { @apply bg-surface text-on-surface font-sans text-body-lg; }
  ::selection { background: rgb(95 179 232 / 0.3); color: var(--color-on-surface); }
  :focus-visible { outline: 2px solid var(--color-primary); outline-offset: 2px; border-radius: var(--radius-sm); }
  a { text-underline-offset: 3px; }
  input { caret-color: var(--color-primary); }
}
@layer components {
  .um-card { @apply bg-surface-container-low rounded-lg p-6 md:p-8; border: var(--border-micro); }
  .um-btn { @apply inline-flex h-11 items-center justify-center gap-2 rounded px-5 text-body-md font-semibold whitespace-nowrap; }
  .um-btn:disabled, .um-btn[aria-disabled="true"] { @apply opacity-50 cursor-not-allowed; }
  .um-btn-primary { @apply bg-primary text-on-primary; }
  .um-btn-secondary { @apply bg-surface-container-high text-on-surface border border-outline-variant; }
  .um-btn-ghost { @apply text-on-surface-variant; }
  .um-btn-danger { @apply bg-error text-on-error; }
  .um-btn-sm { @apply h-9 px-3.5 text-label-md; }
  .um-label { @apply block text-label-sm uppercase text-on-surface-variant mb-1.5; }
  .um-input { @apply w-full h-11 rounded bg-surface-container border border-outline-variant px-3.5 text-body-md text-on-surface placeholder:text-outline; }
  .um-input:focus { @apply border-primary outline-none; }
  .um-input[aria-invalid="true"] { @apply border-error; }
  .um-input[readonly] { @apply text-on-surface-variant; }
  .um-mono { @apply font-mono; }
  .um-hint { @apply mt-1.5 text-label-md font-normal text-on-surface-variant; }
  .um-field-error { @apply mt-1.5 flex items-center gap-1.5 text-label-md text-error; }
  .um-alert { @apply flex items-start gap-3 rounded p-4 text-body-md border; }
  .um-alert-success { @apply bg-success/10 border-success/30 text-on-surface; }
  .um-alert-error { @apply bg-error/10 border-error/40 text-error; }
  .um-alert-warning { @apply bg-warning/10 border-warning/30 text-on-surface; }
  .um-alert-info { @apply bg-secondary/10 border-secondary/30 text-on-surface; }
  .um-pill { @apply inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-label-md; }
  .um-pill-primary { @apply bg-primary/10 text-primary border-primary/25; }
  .um-pill-success { @apply bg-success/10 text-success border-success/25; }
  .um-pill-muted { @apply bg-surface-container-high text-on-surface-variant border-outline-variant; }
  .um-link { @apply text-primary underline decoration-primary/40 hover:decoration-primary; }
  .material-symbols-outlined { font-size: 1.25em; line-height: 1; vertical-align: -0.2em; user-select: none; }
}
@media (prefers-reduced-motion: no-preference) {
  .um-btn, .um-input, .um-link { transition: background-color 160ms ease, border-color 160ms ease, box-shadow 200ms ease, color 160ms ease; }
}
.um-btn-primary:hover { @apply brightness-110; }
.um-btn-secondary:hover { @apply bg-surface-container-highest; }
.um-btn-ghost:hover { @apply text-on-surface bg-surface-container; }
.um-btn-danger:hover { @apply brightness-110; }
"""

// ---------------------------------------------------------------------------
// Icons: a closed union. Adding an icon means adding a case here — there is
// no way to render a Material Symbol Umbral hasn't declared, and the font
// request below is subset to exactly the declared set.
// ---------------------------------------------------------------------------

type Icon =
    | Add
    | ArrowBack
    | CheckCircle
    | ContentCopy
    | DeleteForever
    | ErrorMark
    | ExploreOff
    | Group
    | Home
    | Info
    | Key
    | Link
    | LinkOff
    | Lock
    | Logout
    | Power
    | Refresh
    | Search
    | Shield
    | Warning

    /// The Material Symbols ligature
    member this.Name =
        match this with
        | Add -> "add"
        | ArrowBack -> "arrow_back"
        | CheckCircle -> "check_circle"
        | ContentCopy -> "content_copy"
        | DeleteForever -> "delete_forever"
        | ErrorMark -> "error"
        | ExploreOff -> "explore_off"
        | Group -> "group"
        | Home -> "home"
        | Info -> "info"
        | Key -> "key"
        | Link -> "link"
        | LinkOff -> "link_off"
        | Lock -> "lock"
        | Logout -> "logout"
        | Power -> "power_settings_new"
        | Refresh -> "refresh"
        | Search -> "search"
        | Shield -> "shield"
        | Warning -> "warning"

let iconNames =
    Reflection.FSharpType.GetUnionCases typeof<Icon>
    |> Array.map (fun case -> (Reflection.FSharpValue.MakeUnion(case, [||]) :?> Icon).Name)
    |> List.ofArray

let fontStylesheets =
    [ "https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&family=JetBrains+Mono:wght@400;500&display=swap"
      // display=block: ligature names never flash as literal words while the font loads
      "https://fonts.googleapis.com/css2?family=Material+Symbols+Outlined:opsz,wght,FILL,GRAD@24,400,0,0&display=block&icon_names="
      + (iconNames |> List.sort |> String.concat ",") ]

/// The Umbral mark: a shield standing for "the thing in front of everything else"
let markSvg =
    """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 48 48" fill="none" aria-hidden="true" focusable="false"><path d="M24 4 L41 11 V22 C41 33 34 41 24 44 C14 41 7 33 7 22 V11 Z" fill="#161c25" stroke="#5fb3e8" stroke-width="2.5" stroke-linejoin="round"/><path d="M17 23 L22 28 L32 17" stroke="#5fb3e8" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/></svg>"""

/// Favicon: the mark on a dark tile, so it stays visible on light browser tabs
let faviconHref =
    let inner = markSvg.Replace("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 48 48" fill="none" aria-hidden="true" focusable="false">""", "").Replace("</svg>", "")

    let tile =
        $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="-4 -4 56 56"><rect x="-4" y="-4" width="56" height="56" rx="12" fill="#0d1117"/>{inner}</svg>"""

    "data:image/svg+xml;base64," + Convert.ToBase64String(Text.Encoding.UTF8.GetBytes tile)
