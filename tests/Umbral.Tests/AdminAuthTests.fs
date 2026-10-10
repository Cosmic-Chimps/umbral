module Umbral.Tests.AdminAuthTests

open System
open System.IO
open System.Threading.Tasks
open Xunit
open Umbral.Infrastructure

let private withDb (body: Database.DbConfig -> Task<unit>) =
    task {
        let path = Path.Combine(Path.GetTempPath(), "umbral-auth-" + Guid.NewGuid().ToString "N" + ".db")
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
let private good = "correct-horse-battery-staple"

[<Fact>]
let ``a credential verifies against its own hash`` () =
    let stored = AdminAuth.hashCredential good
    Assert.True(AdminAuth.verifyCredential stored good)
    Assert.False(AdminAuth.verifyCredential stored "wrong")

[<Fact>]
let ``the stored form contains neither the credential nor a reversible encoding`` () =
    let stored = AdminAuth.hashCredential good
    Assert.DoesNotContain(good, stored)
    Assert.StartsWith("pbkdf2-sha256$", stored)

[<Fact>]
let ``two hashes of the same credential differ`` () =
    // A per-credential salt: identical credentials must not produce identical
    // rows, or the database leaks which instances share one.
    Assert.NotEqual<string>(AdminAuth.hashCredential good, AdminAuth.hashCredential good)

[<Fact>]
let ``the iteration count is stored, so raising it later does not lock anyone out`` () =
    let stored = AdminAuth.hashCredential good
    let parts = stored.Split '$'
    Assert.Equal(4, parts.Length)
    // An old credential must verify against the cost it was created with.
    Assert.True(int parts[1] >= 600_000)

[<Fact>]
let ``a malformed stored value never verifies`` () =
    // A truncated or hand-edited settings row must fail closed rather than throw.
    for bad in [ ""; "nonsense"; "pbkdf2-sha256$x$y$z"; "pbkdf2-sha256$600000$!!!$!!!"; "$$$" ] do
        Assert.False(AdminAuth.verifyCredential bad good)

[<Fact>]
let ``setup refuses a credential below the minimum length`` () =
    withDb (fun config ->
        task {
            let! result = AdminAuth.setInitialCredential config clock "short"
            Assert.Equal(AdminAuth.TooShort AdminAuth.MinimumLength, result)

            let! configured = AdminAuth.isConfigured config
            Assert.False configured
        })

[<Fact>]
let ``an instance is unconfigured until setup runs`` () =
    withDb (fun config ->
        task {
            // FR-016: no administrative screen is reachable before this, and
            // Umbral ships no default credential for anyone to guess at.
            let! before = AdminAuth.isConfigured config
            Assert.False before

            let! result = AdminAuth.setInitialCredential config clock good
            Assert.Equal(AdminAuth.Set, result)

            let! after = AdminAuth.isConfigured config
            Assert.True after
        })

[<Fact>]
let ``setup refuses a second time`` () =
    withDb (fun config ->
        task {
            let! _ = AdminAuth.setInitialCredential config clock good
            let! second = AdminAuth.setInitialCredential config clock "a-different-credential"

            // Otherwise the setup screen is a takeover button on any instance
            // whose admin session has expired.
            Assert.Equal(AdminAuth.AlreadyConfigured, second)

            let! stillOriginal = AdminAuth.verify config good
            Assert.True stillOriginal
        })

[<Fact>]
let ``verification works through the database`` () =
    withDb (fun config ->
        task {
            let! _ = AdminAuth.setInitialCredential config clock good
            let! right = AdminAuth.verify config good
            let! wrong = AdminAuth.verify config "not-it"
            Assert.True right
            Assert.False wrong
        })

[<Fact>]
let ``an unconfigured instance verifies nothing`` () =
    withDb (fun config ->
        task {
            // Including the empty string, which is what an empty form posts.
            let! any = AdminAuth.verify config ""
            Assert.False any
        })

[<Fact>]
let ``replacing the credential works and the old one stops`` () =
    withDb (fun config ->
        task {
            let! _ = AdminAuth.setInitialCredential config clock good
            let! result = AdminAuth.replaceCredential config clock "a-brand-new-credential"
            Assert.Equal(AdminAuth.Set, result)

            let! old = AdminAuth.verify config good
            let! replacement = AdminAuth.verify config "a-brand-new-credential"
            Assert.False old
            Assert.True replacement
        })
