/// Deleting a user (T085). The confirmation on the Users screen says this is
/// permanent and names the cascade; this handler carries it out — revoking
/// authorizations first, since the row and its `linked_accounts` disappear
/// together once the delete runs (ON DELETE CASCADE, data-model.md).
module Umbral.Handlers.Admin.UserDelete

open System.Threading.Tasks
open OpenIddict.Abstractions
open Oxpecker
open Umbral.Infrastructure

/// F#'s `task { for x in ... }` binds `seq<'T>`, not `IAsyncEnumerable<'T>`,
/// so OpenIddict's authorization enumerable is drained by hand instead.
let private revokeAll
    (source: System.Collections.Generic.IAsyncEnumerable<obj>)
    (authorizations: IOpenIddictAuthorizationManager)
    =
    task {
        let enumerator = source.GetAsyncEnumerator()
        let mutable go = true

        while go do
            let! hasNext = enumerator.MoveNextAsync()

            if hasNext then
                let! _ = authorizations.TryRevokeAsync enumerator.Current
                ()
            else
                go <- false

        do! enumerator.DisposeAsync()
    }

let private deleteUser (config: Database.DbConfig) (id: string) : Task<unit> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- "DELETE FROM users WHERE id = @id"
        command.Parameters.AddWithValue("@id", id) |> ignore
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

let handler (id: string) : EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<System.TimeProvider>()
            let! _ = WebForms.readValidated ctx

            match! Umbral.Infrastructure.Users.findById config id with
            | None -> return! WebForms.redirectWithNotice ctx "/admin/users" "user-not-found"
            | Some _ ->

            let authorizations = ctx.GetService<IOpenIddictAuthorizationManager>()
            do! revokeAll (authorizations.FindBySubjectAsync id) authorizations

            do! deleteUser config id

            let remoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string

            do!
                SecurityEvents.record
                    config
                    clock
                    { SecurityEvents.event SecurityEvents.UserDeleted SecurityEvents.Success SecurityEvents.Actor.Operator with
                        UserId = Some id
                        RemoteIp = remoteIp }

            return! WebForms.redirectWithNotice ctx "/admin/users" "user-deleted"
        })

let endpoint = POST [ routef "/admin/users/{%s}/delete" handler ]
