/// `GET /admin`: the address an operator types, and the one every message page
/// links back to, so it must always go somewhere useful.
///
/// Not a screen of its own. `requireOperator` already redirects to first-run
/// setup when no credential exists and to sign-in when there is no session; all
/// that is left to say here is where a signed-in operator lands, which is where
/// setup and sign-in themselves land: Connectors, because the first thing a
/// new instance needs is a provider (the five-minute promise).
module Umbral.Handlers.Admin.Landing

open Oxpecker
open Umbral.Infrastructure

let handler: EndpointHandler =
    AdminAuth.requireOperator (fun ctx -> WebForms.redirect ctx "/admin/connectors")

let endpoint = GET [ route "/admin" handler ]
