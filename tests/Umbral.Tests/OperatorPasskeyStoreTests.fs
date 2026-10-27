module Umbral.Tests.OperatorPasskeyStoreTests

open System
open System.IO
open System.Threading.Tasks
open Xunit
open Umbral.Infrastructure
open Umbral.Infrastructure.Database
open Umbral.Infrastructure.OperatorPasskeys

let private withDb (body: DbConfig -> Task<unit>) =
    task {
        let path = Path.Combine(Path.GetTempPath(), "umbral-" + Guid.NewGuid().ToString "N" + ".db")
        let config = { DataSource = path }

        try
            do! initializeSchema config
            do! body config
        finally
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            for suffix in [ ""; "-wal"; "-shm" ] do
                if File.Exists(path + suffix) then
                    File.Delete(path + suffix)
    }

let private clock = TimeProvider.System

/// A passkey with a credential id made from `seed`, so each one is distinct.
let private passkey (seed: int) : OperatorPasskey =
    { Id = newId ()
      CredentialId = [| byte seed; 2uy; 3uy |]
      PublicKey = [| 9uy; byte seed |]
      SignCount = 0u
      Name = $"Key {seed}"
      Transports = [ "internal"; "usb" ]
      CreatedUtc = DateTimeOffset.MinValue
      LastUsedUtc = None }

let private addOne config seed =
    task {
        let key = passkey seed
        let! outcome = add config clock key
        Assert.Equal(Added, outcome)
        return key
    }

[<Fact>]
let ``an added passkey is listed and found by id and by credential id`` () =
    withDb (fun config ->
        task {
            let! key = addOne config 1

            let! all = list config
            Assert.Equal(1, all.Length)
            let stored = all.Head
            Assert.Equal(key.Id, stored.Id)
            Assert.Equal<byte array>(key.CredentialId, stored.CredentialId)
            Assert.Equal<byte array>(key.PublicKey, stored.PublicKey)
            Assert.Equal("Key 1", stored.Name)
            Assert.Equal<string list>([ "internal"; "usb" ], stored.Transports)
            Assert.Equal(None, stored.LastUsedUtc)
            Assert.True(stored.CreatedUtc > DateTimeOffset.UtcNow.AddMinutes -1.0)

            let! byId = findById config key.Id
            Assert.Equal(Some key.Id, byId |> Option.map _.Id)
            let! byCredential = findByCredentialId config key.CredentialId
            Assert.Equal(Some key.Id, byCredential |> Option.map _.Id)
            let! missing = findById config "nope"
            Assert.True(missing.IsNone)

            let! exists = credentialIdExists config key.CredentialId
            Assert.True exists
            let! other = credentialIdExists config [| 99uy |]
            Assert.False other
            let! total = count config
            Assert.Equal(1, total)
        })

[<Fact>]
let ``the eleventh passkey is refused`` () =
    withDb (fun config ->
        task {
            for seed in 1..MaxPasskeys do
                let! _ = addOne config seed
                ()

            let! outcome = add config clock (passkey 50)
            Assert.Equal(LimitReached, outcome)
            let! total = count config
            Assert.Equal(MaxPasskeys, total)
        })

[<Fact>]
let ``a credential id already held is not unique`` () =
    withDb (fun config ->
        task {
            let! _ = addOne config 1
            let! outcome = add config clock (passkey 1)
            Assert.Equal(CredentialNotUnique, outcome)
            let! total = count config
            Assert.Equal(1, total)
        })

[<Fact>]
let ``recordUse updates the counter and the last used time`` () =
    withDb (fun config ->
        task {
            let! key = addOne config 1
            do! recordUse config clock key.Id 7u

            let! stored = findById config key.Id
            Assert.Equal(7u, stored.Value.SignCount)
            Assert.True stored.Value.LastUsedUtc.IsSome
        })

[<Fact>]
let ``the last passkey can be removed while password sign-in is on`` () =
    withDb (fun config ->
        task {
            let! key = addOne config 1
            let! outcome = remove config key.Id
            Assert.Equal(Removed, outcome)
            let! total = count config
            Assert.Equal(0, total)
        })

[<Fact>]
let ``the last passkey stays while password sign-in is off`` () =
    withDb (fun config ->
        task {
            let! key = addOne config 1
            let! switched = turnPasswordSignInOff config clock
            Assert.Equal(SwitchedOff, switched)

            let! outcome = remove config key.Id
            Assert.Equal(LastWhilePasswordOff, outcome)
            let! total = count config
            Assert.Equal(1, total)
        })

[<Fact>]
let ``one of two passkeys can be removed while password sign-in is off`` () =
    withDb (fun config ->
        task {
            let! first = addOne config 1
            let! _ = addOne config 2
            let! _ = turnPasswordSignInOff config clock

            let! outcome = remove config first.Id
            Assert.Equal(Removed, outcome)
            let! total = count config
            Assert.Equal(1, total)
        })

[<Fact>]
let ``an unknown id is not found`` () =
    withDb (fun config ->
        task {
            let! _ = addOne config 1
            let! outcome = remove config "nope"
            Assert.Equal(NotFound, outcome)

            // Not found takes precedence over the rule, with the switch off too.
            let! _ = turnPasswordSignInOff config clock
            let! again = remove config "nope"
            Assert.Equal(NotFound, again)
        })

[<Fact>]
let ``password sign-in cannot be turned off without a passkey`` () =
    withDb (fun config ->
        task {
            let! enabledBefore = passwordSignInEnabled config
            Assert.True enabledBefore

            let! outcome = turnPasswordSignInOff config clock
            Assert.Equal(NoPasskey, outcome)

            let! enabled = passwordSignInEnabled config
            Assert.True enabled
        })

[<Fact>]
let ``password sign-in turns off with a passkey and on again`` () =
    withDb (fun config ->
        task {
            let! _ = addOne config 1
            let! outcome = turnPasswordSignInOff config clock
            Assert.Equal(SwitchedOff, outcome)
            let! off = passwordSignInEnabled config
            Assert.False off

            // Off twice is still off, not an error.
            let! again = turnPasswordSignInOff config clock
            Assert.Equal(SwitchedOff, again)

            do! turnPasswordSignInOn config clock
            let! on = passwordSignInEnabled config
            Assert.True on
        })

[<Fact>]
let ``the user handle is minted once and kept`` () =
    withDb (fun config ->
        task {
            let! first = userHandle config clock
            let! second = userHandle config clock
            Assert.StartsWith("operator-", first)
            Assert.Equal(first, second)
        })

[<Fact>]
let ``removeAll returns how many it removed`` () =
    withDb (fun config ->
        task {
            let! _ = addOne config 1
            let! _ = addOne config 2
            let! _ = addOne config 3

            let! removed = removeAll config
            Assert.Equal(3, removed)
            let! total = count config
            Assert.Equal(0, total)
            let! none = removeAll config
            Assert.Equal(0, none)
        })
