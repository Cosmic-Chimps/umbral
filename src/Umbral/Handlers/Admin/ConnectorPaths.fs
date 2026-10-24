/// The addresses of the Connectors screen, in one place: the screens that redirect
/// to a connector (the handlers of its forms) and the screen itself agree on them.
module Umbral.Handlers.Admin.ConnectorPaths

let listPath = "/admin/connectors"
let newPath = "/admin/connectors/new"
let domainsPath = "/admin/connectors/domains"

let detailPath (slug: string) = listPath + "/" + System.Uri.EscapeDataString slug

/// `new` and `domains` are addresses of the screen, so no connector may take them as its slug.
let reservedSlugs = set [ "new"; "domains" ]
