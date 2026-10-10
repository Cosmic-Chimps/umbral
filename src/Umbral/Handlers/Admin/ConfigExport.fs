/// Exporting the instance as the configuration file that would recreate it.
///
/// FR-029. What comes out must be something that can go back in, which is what
/// makes "rebuild this on new hardware" true rather than aspirational.
module Umbral.Handlers.Admin.ConfigExport

open System.Threading.Tasks
open OpenIddict.Abstractions
open type OpenIddict.Abstractions.OpenIddictConstants
open Oxpecker
open Umbral.Domains
open Umbral.Infrastructure

let private listApps (config: Database.DbConfig) (applications: IOpenIddictApplicationManager) =
    task {
        let results = ResizeArray()
        let! choices = AppConnectors.allChoices config
        let enumerator = applications.ListAsync(System.Nullable(), System.Nullable()).GetAsyncEnumerator()

        try
            let mutable go = true

            while go do
                let! moved = enumerator.MoveNextAsync()

                if moved then
                    let application = enumerator.Current
                    let! clientId = applications.GetClientIdAsync application
                    let! displayName = applications.GetDisplayNameAsync application
                    let! clientType = applications.GetClientTypeAsync application
                    let! redirectUris = applications.GetRedirectUrisAsync application
                    let! postLogoutUris = applications.GetPostLogoutRedirectUrisAsync application

                    let app: ConfigFile.ExportedApp =
                        { ClientId = clientId |> Option.ofObj |> Option.defaultValue ""
                          Name = displayName |> Option.ofObj |> Option.defaultValue ""
                          Confidential = (clientType = ClientTypes.Confidential)
                          RedirectUris = List.ofSeq redirectUris
                          PostLogoutRedirectUris = List.ofSeq postLogoutUris
                          Choice =
                            choices
                            |> Map.tryFind (clientId |> Option.ofObj |> Option.defaultValue "")
                            |> Option.defaultValue Connectors.AllShared }

                    results.Add app
                else
                    go <- false
        finally
            enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult()

        return List.ofSeq results
    }

let handler: EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let! connectors = ConnectorStore.listAll config
            let! apps = listApps config (ctx.GetService<IOpenIddictApplicationManager>())

            let yaml = ConfigFile.export connectors apps

            // Served as a download rather than a page: it is a file the
            // operator puts in a repository, not something to read on screen.
            ctx.SetHttpHeader("Content-Disposition", "attachment; filename=\"umbral.yaml\"")
            ctx.SetContentType "application/yaml; charset=utf-8"
            return! ctx.WriteText yaml
        })

let endpoint = GET [ route "/admin/config" handler ]
