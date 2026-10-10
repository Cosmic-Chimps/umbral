/// Exporting the instance as the configuration file that would recreate it.
///
/// FR-029. What comes out must be something that can go back in, which is what
/// makes "rebuild this on new hardware" true rather than aspirational.
module Umbral.Handlers.Admin.ConfigExport

open System.Threading.Tasks
open OpenIddict.Abstractions
open Oxpecker
open Umbral.Infrastructure

let private listApps (applications: IOpenIddictApplicationManager) =
    task {
        let results = ResizeArray()
        let enumerator = applications.ListAsync(System.Nullable(), System.Nullable()).GetAsyncEnumerator()

        try
            let mutable go = true

            while go do
                let! moved = enumerator.MoveNextAsync()

                if moved then
                    let application = enumerator.Current
                    let! clientId = applications.GetClientIdAsync application
                    let! displayName = applications.GetDisplayNameAsync application
                    let! redirectUris = applications.GetRedirectUrisAsync application
                    let! postLogoutUris = applications.GetPostLogoutRedirectUrisAsync application

                    results.Add(
                        (clientId |> Option.ofObj |> Option.defaultValue ""),
                        (displayName |> Option.ofObj |> Option.defaultValue ""),
                        List.ofSeq redirectUris,
                        List.ofSeq postLogoutUris
                    )
                else
                    go <- false
        finally
            enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult()

        return List.ofSeq results
    }

let handler: EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let! connectors = ConnectorStore.listAll (ctx.GetService<Database.DbConfig>())
            let! apps = listApps (ctx.GetService<IOpenIddictApplicationManager>())

            let yaml = ConfigFile.export connectors apps

            // Served as a download rather than a page: it is a file the
            // operator puts in a repository, not something to read on screen.
            ctx.SetHttpHeader("Content-Disposition", "attachment; filename=\"umbral.yaml\"")
            ctx.SetContentType "application/yaml; charset=utf-8"
            return! ctx.WriteText yaml
        })

let endpoint = GET [ route "/admin/config" handler ]
