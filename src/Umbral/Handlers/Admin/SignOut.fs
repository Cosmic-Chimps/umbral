/// Operator sign-out. A form POST, never a GET link: a link is followable by
/// an image tag on someone else's page.
module Umbral.Handlers.Admin.SignOut

open Oxpecker
open Umbral.Infrastructure

let handler: EndpointHandler =
    fun ctx ->
        task {
            let! _ = WebForms.readValidated ctx
            do! AdminAuth.signOut ctx
            return! WebForms.redirect ctx "/admin/signin"
        }

let endpoint = POST [ route "/admin/signout" handler ]
