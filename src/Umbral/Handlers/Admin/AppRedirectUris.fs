/// Adding and removing an app's redirect URIs and post-logout redirect URIs.
///
/// Four routes, one handler: the two lists differ only in which OpenIddict
/// property they live in, whether they may become empty, and the words and
/// log details around them, so a `Kind` carries those and the rule itself
/// stays in `Domains.RedirectUris`. The screen shows what each route allows,
/// but the routes are reachable directly, so every refusal is decided here too.
module Umbral.Handlers.Admin.AppRedirectUris

open System
open OpenIddict.Abstractions
open Oxpecker
open Umbral.Domains
open Umbral.Infrastructure
open Umbral.Handlers.Admin

/// What differs between the two lists.
type private Kind =
    { /// The list's route, `redirect-uris` or `post-logout-uris`: names the list
      /// an address was typed in when a refused one is kept for the field.
      Route: string
      /// The list as stored on the application.
      Read: IOpenIddictApplicationManager -> obj -> System.Threading.Tasks.ValueTask<System.Collections.Immutable.ImmutableArray<string>>
      /// The one set on the descriptor that holds this list.
      Set: OpenIddictApplicationDescriptor -> Collections.Generic.HashSet<Uri>
      /// A redirect URI list may never be emptied: an app with none cannot
      /// sign anyone in. Post-logout may — the user then lands on Umbral's home.
      KeepOne: bool
      AddedDetail: string
      RemovedDetail: string
      AddedNotice: string
      RemovedNotice: string }

let private redirect: Kind =
    { Route = "redirect-uris"
      Read = fun m app -> m.GetRedirectUrisAsync app
      Set = fun d -> d.RedirectUris
      KeepOne = true
      AddedDetail = "redirect_uri_added"
      RemovedDetail = "redirect_uri_removed"
      AddedNotice = "app-redirect-uri-added"
      RemovedNotice = "app-redirect-uri-removed" }

let private postLogout: Kind =
    { Route = "post-logout-uris"
      Read = fun m app -> m.GetPostLogoutRedirectUrisAsync app
      Set = fun d -> d.PostLogoutRedirectUris
      KeepOne = false
      AddedDetail = "post_logout_redirect_uri_added"
      RemovedDetail = "post_logout_redirect_uri_removed"
      AddedNotice = "app-post-logout-uri-added"
      RemovedNotice = "app-post-logout-uri-removed" }

/// `add` or `remove` of one list, for one app. A refusal writes nothing and
/// logs nothing: the screen redirects back with the reason, as for any other
/// form that was refused.
let private change (kind: Kind) (isAdd: bool) (clientId: string) : EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let! form = WebForms.readValidated ctx
            let value = WebForms.value form "uri"

            let applications = ctx.GetService<IOpenIddictApplicationManager>()
            let! application = applications.FindByClientIdAsync clientId

            match application with
            | null -> return! WebForms.redirectWithNotice ctx Apps.listPath "app-not-found"
            | application ->

            let! stored = kind.Read applications application
            let current = List.ofSeq stored

            // The outcome: the new list and what to log, or the notice that says no.
            let outcome =
                if isAdd then
                    match RedirectUris.add current value with
                    | Ok list ->
                        // The stored form of the address, which `add` appended last.
                        Ok(list, kind.AddedDetail, List.last list, kind.AddedNotice)
                    | Error(RedirectUris.Invalid _) -> Error "app-redirect-uri-invalid"
                    | Error RedirectUris.AlreadyRegistered -> Error "app-redirect-uri-duplicate"
                    | Error RedirectUris.Full -> Error "app-redirect-uri-limit"
                else
                    match RedirectUris.remove current value kind.KeepOne with
                    | Ok list -> Ok(list, kind.RemovedDetail, value, kind.RemovedNotice)
                    | Error RedirectUris.LastOne -> Error "app-redirect-uri-last"
                    | Error RedirectUris.NotRegistered -> Error "app-redirect-uri-not-registered"

            match outcome with
            | Error notice ->
                // An address refused as invalid goes back to the field it was
                // typed in, through the single-use cookie, never the URL.
                if isAdd && notice = "app-redirect-uri-invalid" then
                    match Apps.KeptAddress.encode kind.Route value with
                    | Some reason -> WebForms.NoticeReason.stash ctx notice "app" clientId reason
                    | None -> ()

                return! WebForms.redirectWithNotice ctx (Apps.detailPath clientId) notice
            | Ok(newList, detail, changed, notice) ->

            // The overload that takes a descriptor leaves the secret alone when
            // none is given, so this does not re-hash it.
            let descriptor = OpenIddictApplicationDescriptor()
            do! applications.PopulateAsync(descriptor, application)
            let set = kind.Set descriptor
            set.Clear()

            for s in newList do
                set.Add(Uri(s, UriKind.Absolute)) |> ignore

            do! applications.UpdateAsync(application, descriptor)

            do!
                SecurityEvents.record
                    config
                    clock
                    { SecurityEvents.event SecurityEvents.AppUpdated SecurityEvents.Success SecurityEvents.Actor.Operator with
                        ClientId = Some clientId
                        RemoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string
                        Detail = Some $"{detail};uri={Uri.EscapeDataString changed}" }

            return! WebForms.redirectWithNotice ctx (Apps.detailPath clientId) notice
        })

let endpoint =
    POST
        [ routef "/admin/apps/{%s}/redirect-uris" (change redirect true)
          routef "/admin/apps/{%s}/redirect-uris/remove" (change redirect false)
          routef "/admin/apps/{%s}/post-logout-uris" (change postLogout true)
          routef "/admin/apps/{%s}/post-logout-uris/remove" (change postLogout false) ]
