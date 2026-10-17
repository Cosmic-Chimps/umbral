/// Where Umbral's look lives. The design tokens (palette, type scale, radii,
/// spacing, shadows) and the `.um-*` component CSS are not F# data any more:
/// they are `src/Umbral/Assets/tailwind.config.js` and `umbral.input.css`,
/// compiled once to `umbral.css` by `Assets/build-css.sh`, committed, and served
/// by Umbral itself (Views/Assets.fs). Nothing on a page comes from a third party
/// — an identity provider's credential pages must not, and an offline homelab
/// still gets a styled UI.
///
/// What stays here is what F# code needs to reference: the icon set, the brand
/// mark (the Gnomon, specs/007-umbra-look research R6) and the person's theme
/// choice (light, dark or follow the system), which `Layout` writes on `<html>`.
///
/// Rule: class names are never built by interpolation — only literal strings are
/// written in views, so Tailwind's scanner (it reads Views/ and Handlers/) always
/// sees them and the committed CSS contains them.
module Umbral.Views.Theme

open System
open Microsoft.AspNetCore.Http

// ---------------------------------------------------------------------------
// Icons: a closed union. Adding an icon means adding a case here — there is
// no way to render an icon Umbral hasn't declared. Each is inline SVG path
// data, so no icon font is downloaded.
//
// Paths are from Material Symbols Outlined (opsz 24, wght 400, FILL 0,
// GRAD 0), Apache License 2.0, https://github.com/google/material-design-icons.
// All share the viewBox `0 -960 960 960`.
// ---------------------------------------------------------------------------

type Icon =
    | Add
    | ArrowBack
    | CheckCircle
    | Computer
    | ContentCopy
    | DarkMode
    | DeleteForever
    | ErrorMark
    | ExploreOff
    | Group
    | Home
    | Info
    | Key
    | LightMode
    | Link
    | LinkOff
    | Lock
    | Logout
    | Power
    | Refresh
    | Search
    | Shield
    | Warning

    /// The SVG path data (viewBox `0 -960 960 960`)
    member this.Path =
        match this with
        | Add -> "M440-440H200v-80h240v-240h80v240h240v80H520v240h-80v-240Z"
        | ArrowBack -> "m313-440 224 224-57 56-320-320 320-320 57 56-224 224h487v80H313Z"
        | CheckCircle -> "m424-296 282-282-56-56-226 226-114-114-56 56 170 170Zm56 216q-83 0-156-31.5T197-197q-54-54-85.5-127T80-480q0-83 31.5-156T197-763q54-54 127-85.5T480-880q83 0 156 31.5T763-763q54 54 85.5 127T880-480q0 83-31.5 156T763-197q-54 54-127 85.5T480-80Zm0-80q134 0 227-93t93-227q0-134-93-227t-227-93q-134 0-227 93t-93 227q0 134 93 227t227 93Zm0-320Z"
        | Computer -> "M40-120v-80h880v80H40Zm120-120q-33 0-56.5-23.5T80-320v-440q0-33 23.5-56.5T160-840h640q33 0 56.5 23.5T880-760v440q0 33-23.5 56.5T800-240H160Zm0-80h640v-440H160v440Zm0 0v-440 440Z"
        | ContentCopy -> "M360-240q-33 0-56.5-23.5T280-320v-480q0-33 23.5-56.5T360-880h360q33 0 56.5 23.5T800-800v480q0 33-23.5 56.5T720-240H360Zm0-80h360v-480H360v480ZM200-80q-33 0-56.5-23.5T120-160v-560h80v560h440v80H200Zm160-240v-480 480Z"
        | DarkMode -> "M480-120q-150 0-255-105T120-480q0-150 105-255t255-105q14 0 27.5 1t26.5 3q-41 29-65.5 75.5T444-660q0 90 63 153t153 63q55 0 101-24.5t75-65.5q2 13 3 26.5t1 27.5q0 150-105 255T480-120Zm0-80q88 0 158-48.5T740-375q-20 5-40 8t-40 3q-123 0-209.5-86.5T364-660q0-20 3-40t8-40q-78 32-126.5 102T200-480q0 116 82 198t198 82Zm-10-270Z"
        | DeleteForever -> "m376-300 104-104 104 104 56-56-104-104 104-104-56-56-104 104-104-104-56 56 104 104-104 104 56 56Zm-96 180q-33 0-56.5-23.5T200-200v-520h-40v-80h200v-40h240v40h200v80h-40v520q0 33-23.5 56.5T680-120H280Zm400-600H280v520h400v-520Zm-400 0v520-520Z"
        | ErrorMark -> "M508.5-291.5Q520-303 520-320t-11.5-28.5Q497-360 480-360t-28.5 11.5Q440-337 440-320t11.5 28.5Q463-280 480-280t28.5-11.5ZM440-440h80v-240h-80v240Zm40 360q-83 0-156-31.5T197-197q-54-54-85.5-127T80-480q0-83 31.5-156T197-763q54-54 127-85.5T480-880q83 0 156 31.5T763-763q54 54 85.5 127T880-480q0 83-31.5 156T763-197q-54 54-127 85.5T480-80Zm0-80q134 0 227-93t93-227q0-134-93-227t-227-93q-134 0-227 93t-93 227q0 134 93 227t227 93Zm0-320Z"
        | ExploreOff -> "m660-660-55 191-136-136 191-55ZM791-55l-91-91q-49 32-104.5 49T480-80q-83 0-156-31.5T197-197q-54-54-85.5-127T80-480q0-60 17-115.5T146-700l-91-91 57-57 736 736-57 57Zm23-205-58-58q22-38 33-78.5t11-83.5q0-133-93.5-226.5T480-800q-43 0-83.5 11T318-756l-58-58q49-32 104.5-49T480-880q83 0 156 31.5T763-763q54 54 85.5 127T880-480q0 60-17 115.5T814-260ZM537-537ZM423-423Zm57 263q43 0 83.5-11t78.5-33L491-355l-191 55 55-191-151-151q-22 38-33 78.5T160-480q0 133 93.5 226.5T480-160Z"
        | Group -> "M40-160v-112q0-34 17.5-62.5T104-378q62-31 126-46.5T360-440q66 0 130 15.5T616-378q29 15 46.5 43.5T680-272v112H40Zm720 0v-120q0-44-24.5-84.5T666-434q51 6 96 20.5t84 35.5q36 20 55 44.5t19 53.5v120H760ZM247-527q-47-47-47-113t47-113q47-47 113-47t113 47q47 47 47 113t-47 113q-47 47-113 47t-113-47Zm466 0q-47 47-113 47-11 0-28-2.5t-28-5.5q27-32 41.5-71t14.5-81q0-42-14.5-81T544-792q14-5 28-6.5t28-1.5q66 0 113 47t47 113q0 66-47 113ZM120-240h480v-32q0-11-5.5-20T580-306q-54-27-109-40.5T360-360q-56 0-111 13.5T140-306q-9 5-14.5 14t-5.5 20v32Zm296.5-343.5Q440-607 440-640t-23.5-56.5Q393-720 360-720t-56.5 23.5Q280-673 280-640t23.5 56.5Q327-560 360-560t56.5-23.5ZM360-240Zm0-400Z"
        | Home -> "M240-200h120v-240h240v240h120v-360L480-740 240-560v360Zm-80 80v-480l320-240 320 240v480H520v-240h-80v240H160Zm320-350Z"
        | Info -> "M440-280h80v-240h-80v240Zm68.5-331.5Q520-623 520-640t-11.5-28.5Q497-680 480-680t-28.5 11.5Q440-657 440-640t11.5 28.5Q463-600 480-600t28.5-11.5ZM480-80q-83 0-156-31.5T197-197q-54-54-85.5-127T80-480q0-83 31.5-156T197-763q54-54 127-85.5T480-880q83 0 156 31.5T763-763q54 54 85.5 127T880-480q0 83-31.5 156T763-197q-54 54-127 85.5T480-80Zm0-80q134 0 227-93t93-227q0-134-93-227t-227-93q-134 0-227 93t-93 227q0 134 93 227t227 93Zm0-320Z"
        | Key -> "M223.5-423.5Q200-447 200-480t23.5-56.5Q247-560 280-560t56.5 23.5Q360-513 360-480t-23.5 56.5Q313-400 280-400t-56.5-23.5ZM280-240q-100 0-170-70T40-480q0-100 70-170t170-70q67 0 121.5 33t86.5 87h352l120 120-180 180-80-60-80 60-85-60h-47q-32 54-86.5 87T280-240Zm0-80q56 0 98.5-34t56.5-86h125l58 41 82-61 71 55 75-75-40-40H435q-14-52-56.5-86T280-640q-66 0-113 47t-47 113q0 66 47 113t113 47Z"
        | LightMode -> "M480-360q50 0 85-35t35-85q0-50-35-85t-85-35q-50 0-85 35t-35 85q0 50 35 85t85 35Zm0 80q-83 0-141.5-58.5T280-480q0-83 58.5-141.5T480-680q83 0 141.5 58.5T680-480q0 83-58.5 141.5T480-280ZM200-440H40v-80h160v80Zm720 0H760v-80h160v80ZM440-760v-160h80v160h-80Zm0 720v-160h80v160h-80ZM256-650l-101-97 57-59 96 100-52 56Zm492 496-97-101 53-55 101 97-57 59Zm-98-550 97-101 59 57-100 96-56-52ZM154-212l101-97 55 53-97 101-59-57Zm326-268Z"
        | Link -> "M440-280H280q-83 0-141.5-58.5T80-480q0-83 58.5-141.5T280-680h160v80H280q-50 0-85 35t-35 85q0 50 35 85t85 35h160v80ZM320-440v-80h320v80H320Zm200 160v-80h160q50 0 85-35t35-85q0-50-35-85t-85-35H520v-80h160q83 0 141.5 58.5T880-480q0 83-58.5 141.5T680-280H520Z"
        | LinkOff -> "m770-302-60-62q40-11 65-42.5t25-73.5q0-50-35-85t-85-35H520v-80h160q83 0 141.5 58.5T880-480q0 57-29.5 105T770-302ZM634-440l-80-80h86v80h-6ZM792-56 56-792l56-56 736 736-56 56ZM440-280H280q-83 0-141.5-58.5T80-480q0-69 42-123t108-71l74 74h-24q-50 0-85 35t-35 85q0 50 35 85t85 35h160v80ZM320-440v-80h65l79 80H320Z"
        | Lock -> "M240-80q-33 0-56.5-23.5T160-160v-400q0-33 23.5-56.5T240-640h40v-80q0-83 58.5-141.5T480-920q83 0 141.5 58.5T680-720v80h40q33 0 56.5 23.5T800-560v400q0 33-23.5 56.5T720-80H240Zm0-80h480v-400H240v400Zm296.5-143.5Q560-327 560-360t-23.5-56.5Q513-440 480-440t-56.5 23.5Q400-393 400-360t23.5 56.5Q447-280 480-280t56.5-23.5ZM360-640h240v-80q0-50-35-85t-85-35q-50 0-85 35t-35 85v80ZM240-160v-400 400Z"
        | Logout -> "M200-120q-33 0-56.5-23.5T120-200v-560q0-33 23.5-56.5T200-840h280v80H200v560h280v80H200Zm440-160-55-58 102-102H360v-80h327L585-622l55-58 200 200-200 200Z"
        | Power -> "M480-80q-83 0-156-31.5T197-197q-54-54-85.5-127T80-480q0-84 31.5-156.5T197-763l56 56q-44 44-68.5 102T160-480q0 134 93 227t227 93q134 0 227-93t93-227q0-67-24.5-125T707-707l56-56q54 54 85.5 126.5T880-480q0 83-31.5 156T763-197q-54 54-127 85.5T480-80Zm-40-360v-440h80v440h-80Z"
        | Refresh -> "M480-160q-134 0-227-93t-93-227q0-134 93-227t227-93q69 0 132 28.5T720-690v-110h80v280H520v-80h168q-32-56-87.5-88T480-720q-100 0-170 70t-70 170q0 100 70 170t170 70q77 0 139-44t87-116h84q-28 106-114 173t-196 67Z"
        | Search -> "M784-120 532-372q-30 24-69 38t-83 14q-109 0-184.5-75.5T120-580q0-109 75.5-184.5T380-840q109 0 184.5 75.5T640-580q0 44-14 83t-38 69l252 252-56 56ZM380-400q75 0 127.5-52.5T560-580q0-75-52.5-127.5T380-760q-75 0-127.5 52.5T200-580q0 75 52.5 127.5T380-400Z"
        | Shield -> "M480-80q-139-35-229.5-159.5T160-516v-244l320-120 320 120v244q0 152-90.5 276.5T480-80Zm0-84q104-33 172-132t68-220v-189l-240-90-240 90v189q0 121 68 220t172 132Zm0-316Z"
        | Warning -> "m40-120 440-760 440 760H40Zm138-80h604L480-720 178-200Zm330.5-51.5Q520-263 520-280t-11.5-28.5Q497-320 480-320t-28.5 11.5Q440-297 440-280t11.5 28.5Q463-240 480-240t28.5-11.5ZM440-360h80v-200h-80v200Zm40-100Z"

// ---------------------------------------------------------------------------
// The Gnomon: a sun, an upright and the shadow it casts on the threshold.
//
// Three drawings, because the detail that reads at 64 px is mud at 16: `full`
// (33 px and up) has the base line, `small` (up to 32 px) drops it and thickens
// the upright, `tiny` (16 px) is thicker still. Each part carries a class and no
// colour: umbral.input.css colours them from the `--mark-*` properties of the
// theme (SVG presentation attributes do not take var()), and no accent sheet
// sets those, so an app's colour never recolours Umbral's mark.
// ---------------------------------------------------------------------------

let private svgOpen =
    """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 48 48" fill="none" aria-hidden="true" focusable="false">"""

module Mark =
    /// 33 px and up: with the base line
    let full =
        svgOpen
        + """<circle class="um-mark-sun" cx="11" cy="11" r="4.5"/><path class="um-mark-base" d="M6 39h36" stroke-width="3" stroke-linecap="round"/><path class="um-mark-shadow" d="M22 39H44L22 33Z"/><path class="um-mark-upright" d="M22 39V13" stroke-width="4" stroke-linecap="round"/></svg>"""

    /// Up to 32 px: no base line, a heavier upright
    let small =
        svgOpen
        + """<circle class="um-mark-sun" cx="11" cy="11" r="5.5"/><path class="um-mark-shadow" d="M22 40H45L22 32Z"/><path class="um-mark-upright" d="M22 40V13" stroke-width="5.5" stroke-linecap="round"/></svg>"""

    /// 16 px
    let tiny =
        svgOpen
        + """<circle class="um-mark-sun" cx="11" cy="11" r="7"/><path class="um-mark-shadow" d="M22 42H46L22 31Z"/><path class="um-mark-upright" d="M22 42V14" stroke-width="8" stroke-linecap="round"/></svg>"""

/// Favicon: the tiny drawing in fixed dark colours on a dark rounded tile, so it
/// stays legible on light browser tabs and does not depend on the page's theme
/// (a favicon is a separate document and cannot read the stylesheet).
let faviconHref =
    let inner =
        Mark.tiny
            .Replace(svgOpen, "")
            .Replace("</svg>", "")
            .Replace("""class="um-mark-sun" """, """fill="#f5c26b" """)
            .Replace("""class="um-mark-shadow" """, """fill="#a7a2b6" opacity="0.7" """)
            .Replace("""class="um-mark-upright" """, """stroke="#ece8df" """)

    let tile =
        $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="-4 -4 56 56"><rect x="-4" y="-4" width="56" height="56" rx="12" fill="#0b0b14"/><g fill="none">{inner}</g></svg>"""

    "data:image/svg+xml;base64," + Convert.ToBase64String(Text.Encoding.UTF8.GetBytes tile)

// ---------------------------------------------------------------------------
// The theme choice: a per-browser display preference, kept in one cookie. The
// server reads it to write `data-theme` on `<html>` (Layout), so the page is
// themed before the first paint with no script (spec 007, research R1, R5).
// ---------------------------------------------------------------------------

/// The cookie that holds the choice: `light` or `dark`; absent means System
[<Literal>]
let ThemeCookie = "umbral_theme"

type ThemeChoice =
    | System
    | Light
    | Dark

module ThemeChoice =
    /// The value written on `<html data-theme>` and posted by the theme control
    let attr (choice: ThemeChoice) =
        match choice with
        | System -> "system"
        | Light -> "light"
        | Dark -> "dark"

    /// A posted choice: exactly `system`, `light` or `dark`, anything else `None`
    let parseChoice (value: string) : ThemeChoice option =
        match value with
        | "system" -> Some System
        | "light" -> Some Light
        | "dark" -> Some Dark
        | _ -> None

    /// What the request's cookie says: exact, case-sensitive `light` or `dark`;
    /// absent or anything else (`Light`, `blue`, empty) is System.
    let ofRequest (ctx: HttpContext) : ThemeChoice =
        match ctx.Request.Cookies.TryGetValue ThemeCookie with
        | true, "light" -> Light
        | true, "dark" -> Dark
        | _ -> System
