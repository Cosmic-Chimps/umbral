/// The addresses of the Connectors screen, in one place: the screens that redirect
/// to a connector (the handlers of its forms) and the screen itself agree on them.
module Umbral.Handlers.Admin.ConnectorPaths

let listPath = "/admin/connectors"
let newPath = "/admin/connectors/new"
let domainsPath = "/admin/connectors/domains"

/// `new` and `domains` are addresses of the screen, so no connector may take them as its slug.
let reservedSlugs = set [ "new"; "domains" ]

/// Versions before 0.5.0 let a connector take a reserved slug. Its pane is at
/// `_` and the slug: `_` is not a character of a slug, so no other connector
/// has that address.
let private reservedPrefix = "_"

let detailPath (slug: string) =
    let segment = if reservedSlugs.Contains slug then reservedPrefix + slug else slug
    listPath + "/" + System.Uri.EscapeDataString segment

/// The slug of a connector's address, the reverse of `detailPath`, for the routes
/// that take the slug from the path.
let slugOfSegment (segment: string) =
    if segment.StartsWith reservedPrefix && reservedSlugs.Contains(segment.Substring reservedPrefix.Length) then
        segment.Substring reservedPrefix.Length
    else
        segment
