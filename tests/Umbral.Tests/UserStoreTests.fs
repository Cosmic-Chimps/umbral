module Umbral.Tests.UserStoreTests

open System
open System.IO
open System.Threading.Tasks
open Xunit
open Umbral.Domains
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Infrastructure.ProviderAdapters

let private withDb (body: Database.DbConfig -> Task<unit>) =
    task {
        let path = Path.Combine(Path.GetTempPath(), "umbral-us-" + Guid.NewGuid().ToString "N" + ".db")
        let config: Database.DbConfig = { DataSource = path }

        try
            do! Database.initializeSchema config
            do! body config
        finally
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            for suffix in [ ""; "-wal"; "-shm" ] do
                if File.Exists(path + suffix) then File.Delete(path + suffix)
    }

let private clock = TimeProvider.System

let private profile subject email verified =
    { Subject = subject
      Email = email
      EmailVerified = verified
      DisplayName = Some "Sam Rivera"
      AvatarUrl = None
      Username = None
      ProfileUrl = None }

[<Fact>]
let ``a first sign-in creates a user and a link`` () =
    withDb (fun config ->
        task {
            let! r = UserStore.resolve config clock Discord (profile "disc-1" (Some "sam@example.com") true)

            Assert.Equal(Linking.CreateNew, r.Decision)
            Assert.True r.User.IsEnabled
            // The subject is Umbral's own, never the provider's.
            Assert.NotEqual<string>("disc-1", r.User.Id)

            let! kinds = UserStore.linkedKinds config r.User.Id
            Assert.Equal<string list>([ "discord" ], kinds)
        })

[<Fact>]
let ``signing in again is the same user`` () =
    withDb (fun config ->
        task {
            let! first = UserStore.resolve config clock Discord (profile "disc-1" (Some "sam@example.com") true)
            let! second = UserStore.resolve config clock Discord (profile "disc-1" (Some "sam@example.com") true)

            Assert.Equal(Linking.ExistingLink first.User.Id, second.Decision)
            Assert.Equal<string>(first.User.Id, second.User.Id)
        })

[<Fact>]
let ``a second provider with the same VERIFIED email links to the same user`` () =
    withDb (fun config ->
        task {
            let! discord = UserStore.resolve config clock Discord (profile "disc-1" (Some "sam@example.com") true)
            let! github = UserStore.resolve config clock GitHub (profile "gh-9" (Some "sam@example.com") true)

            Assert.Equal(Linking.LinkToExisting discord.User.Id, github.Decision)
            Assert.Equal<string>(discord.User.Id, github.User.Id)

            let! kinds = UserStore.linkedKinds config discord.User.Id
            Assert.Equal<string list>([ "discord"; "github" ], kinds)
        })

[<Fact>]
let ``a second provider with an UNVERIFIED matching email does NOT link`` () =
    withDb (fun config ->
        task {
            // The account-takeover case, end to end through the database.
            let! discord = UserStore.resolve config clock Discord (profile "disc-1" (Some "sam@example.com") true)
            let! github = UserStore.resolve config clock GitHub (profile "gh-9" (Some "sam@example.com") false)

            Assert.Equal(Linking.CreateNew, github.Decision)
            Assert.Equal(Linking.EmailNotVerified, github.Reason)
            Assert.NotEqual<string>(discord.User.Id, github.User.Id)
        })

[<Fact>]
let ``a provider asserting no email creates its own user`` () =
    withDb (fun config ->
        task {
            let! a = UserStore.resolve config clock GitHub (profile "gh-1" None false)
            let! b = UserStore.resolve config clock Google (profile "goo-1" None false)

            // Two people with private emails must not collapse into one.
            Assert.NotEqual<string>(a.User.Id, b.User.Id)
        })

[<Fact>]
let ``email matching ignores case`` () =
    withDb (fun config ->
        task {
            let! first = UserStore.resolve config clock Discord (profile "disc-1" (Some "Sam@Example.com") true)
            let! second = UserStore.resolve config clock GitHub (profile "gh-9" (Some "sam@example.com") true)

            // Addresses are case-insensitive in practice; treating them
            // otherwise would create a second account for the same person.
            Assert.Equal<string>(first.User.Id, second.User.Id)
        })

[<Fact>]
let ``the display name and avatar refresh on a later sign-in`` () =
    withDb (fun config ->
        task {
            let! first = UserStore.resolve config clock Discord (profile "disc-1" (Some "sam@example.com") true)

            let! _ =
                UserStore.resolve
                    config
                    clock
                    Discord
                    { profile "disc-1" (Some "sam@example.com") true with
                        DisplayName = Some "Sam R."
                        AvatarUrl = Some "https://cdn.example.com/a.png" }

            match! Users.findById config first.User.Id with
            | Some user ->
                Assert.Equal(Some "Sam R.", Some user.DisplayName)
                Assert.Equal(Some "https://cdn.example.com/a.png", user.AvatarUrl)
            | None -> failwith "the user vanished"
        })

[<Fact>]
let ``losing verification at the provider does not unpick an existing link`` () =
    withDb (fun config ->
        task {
            let! first = UserStore.resolve config clock Discord (profile "disc-1" (Some "sam@example.com") true)
            let! again = UserStore.resolve config clock Discord (profile "disc-1" (Some "sam@example.com") false)

            // Keyed on the provider's immutable id, so this is still the same
            // person — and their account must not become unverified beneath them.
            Assert.Equal<string>(first.User.Id, again.User.Id)

            match! Users.findById config first.User.Id with
            | Some user -> Assert.True user.EmailVerified
            | None -> failwith "the user vanished"
        })

[<Fact>]
let ``concurrent first sign-ins create exactly one user`` () =
    withDb (fun config ->
        task {
            // The primary key on (kind, subject) is the defence; the loser of
            // the race must update rather than insert a second user.
            let attempts =
                [ for _ in 1..8 ->
                      UserStore.resolve config clock Discord (profile "disc-race" (Some "race@example.com") true) ]

            let! results = Task.WhenAll attempts
            let distinct = results |> Array.map (fun r -> r.User.Id) |> Array.distinct

            Assert.Single distinct |> ignore
        })
