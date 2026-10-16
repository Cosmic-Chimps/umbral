/// The `app_branding` store (spec 006, data-model.md "Row lifecycle"): one row
/// while an app has a logo, an accent or both, and no row otherwise.
///
/// Against a real SQLite file with the real schema, since the CHECKs, the
/// unique hash and the upsert are the behaviour. The `Logo` values here are
/// built directly: the store never reads the bytes, so they need not be a valid
/// PNG (that is `PngLogo`'s tests).
module Umbral.Tests.BrandingStoreTests

open System
open System.IO
open System.Threading.Tasks
open Xunit
open Umbral.Domains.Branding
open Umbral.Infrastructure
open Umbral.Infrastructure.Database

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

let private t0 = DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero)
let private t1 = t0.AddMinutes 5.0

let private accent (hex: string) =
    match Accent.parse hex with
    | Ok a -> a
    | Error e -> failwith $"{hex}: {e}"

let private logo (seed: byte) (width: int) (height: int) : Logo =
    let bytes = [| 0x89uy; 0x50uy; seed; seed |]
    { Png = bytes
      Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData bytes).ToLowerInvariant()
      Width = width
      Height = height }

let private rowCount (config: DbConfig) =
    task {
        use! connection = openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT count(*) FROM app_branding"
        let! value = command.ExecuteScalarAsync()
        return Convert.ToInt32 value
    }

let private updatedAt (config: DbConfig) (clientId: string) =
    task {
        use! connection = openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT updated_at FROM app_branding WHERE client_id = @id"
        command.Parameters.AddWithValue("@id", clientId) |> ignore
        let! value = command.ExecuteScalarAsync()
        return DateTimeOffset.Parse(string value)
    }

let private hexOf (branding: AppBranding option) =
    branding |> Option.bind _.Accent |> Option.map Accent.hex

let private logoOf (branding: AppBranding option) =
    branding |> Option.bind _.Logo

[<Fact>]
let ``an app with no row has no branding`` () =
    withDb (fun config ->
        task {
            let! branding = BrandingStore.forApp config "grafana"
            Assert.True(branding.IsNone)

            let! all = BrandingStore.allForApps config
            Assert.True(all.IsEmpty)

            let! bytes = BrandingStore.logoBytes config (String('0', 64))
            Assert.True(bytes.IsNone)
        })

[<Fact>]
let ``the row lifecycle: accent, then logo, remove the logo, clear the accent`` () =
    withDb (fun config ->
        task {
            // An accent alone makes the row.
            do! BrandingStore.setAccent config t0 "grafana" (Some(accent "#F26B1D"))
            let! first = BrandingStore.forApp config "grafana"
            Assert.Equal(Some "f26b1d", hexOf first)
            Assert.True((logoOf first).IsNone)
            let! n = rowCount config
            Assert.Equal(1, n)

            // A logo joins it: one row with both.
            let a = logo 1uy 200 100
            do! BrandingStore.setLogo config t1 "grafana" a
            let! both = BrandingStore.forApp config "grafana"
            Assert.Equal(Some "f26b1d", hexOf both)
            Assert.Equal(Some { Sha256 = a.Sha256; Width = 200; Height = 100 }, logoOf both)
            let! n = rowCount config
            Assert.Equal(1, n)
            let! n = updatedAt config "grafana"
            Assert.Equal(t1, n)

            // Removing the logo keeps the accent, and the row.
            do! BrandingStore.removeLogo config t1 "grafana"
            let! accentOnly = BrandingStore.forApp config "grafana"
            Assert.Equal(Some "f26b1d", hexOf accentOnly)
            Assert.True((logoOf accentOnly).IsNone)
            let! n = rowCount config
            Assert.Equal(1, n)

            // Its bytes went with it.
            let! gone = BrandingStore.logoBytes config a.Sha256
            Assert.True(gone.IsNone)

            // Clearing the last remaining part leaves no row at all.
            do! BrandingStore.setAccent config t1 "grafana" None
            let! none = BrandingStore.forApp config "grafana"
            Assert.True(none.IsNone)
            let! n = rowCount config
            Assert.Equal(0, n)
        })

[<Fact>]
let ``removing the last part by the other route also deletes the row`` () =
    withDb (fun config ->
        task {
            // Logo alone, then the accent set and cleared, then the logo removed.
            do! BrandingStore.setLogo config t0 "grafana" (logo 1uy 64 64)
            do! BrandingStore.setAccent config t0 "grafana" (Some(accent "#000000"))
            do! BrandingStore.setAccent config t0 "grafana" None

            let! logoOnly = BrandingStore.forApp config "grafana"
            Assert.True((logoOf logoOnly).IsSome)
            Assert.True((hexOf logoOnly).IsNone)
            let! n = rowCount config
            Assert.Equal(1, n)

            do! BrandingStore.removeLogo config t0 "grafana"
            let! n = rowCount config
            Assert.Equal(0, n)
        })

[<Fact>]
let ``removing or clearing what is not there changes nothing and creates no row`` () =
    withDb (fun config ->
        task {
            do! BrandingStore.removeLogo config t0 "grafana"
            do! BrandingStore.setAccent config t0 "grafana" None
            let! n = rowCount config
            Assert.Equal(0, n)

            // Nor does it disturb another app's row.
            do! BrandingStore.setAccent config t0 "photos" (Some(accent "#5fb3e8"))
            do! BrandingStore.removeLogo config t1 "photos"
            let! photos = BrandingStore.forApp config "photos"
            Assert.Equal(Some "5fb3e8", hexOf photos)
        })

[<Fact>]
let ``setLogo twice leaves one row and only the second logo resolves`` () =
    withDb (fun config ->
        task {
            let first = logo 1uy 100 100
            let second = logo 2uy 300 150
            do! BrandingStore.setLogo config t0 "grafana" first
            do! BrandingStore.setLogo config t1 "grafana" second

            let! n = rowCount config
            Assert.Equal(1, n)

            let! old = BrandingStore.logoBytes config first.Sha256
            Assert.True(old.IsNone)

            let! current = BrandingStore.logoBytes config second.Sha256
            Assert.Equal<byte array>(second.Png, current.Value)

            let! branding = BrandingStore.forApp config "grafana"
            Assert.Equal(Some { Sha256 = second.Sha256; Width = 300; Height = 150 }, logoOf branding)
        })

[<Fact>]
let ``replacing the logo keeps the accent`` () =
    withDb (fun config ->
        task {
            do! BrandingStore.setAccent config t0 "grafana" (Some(accent "#1f6fa8"))
            do! BrandingStore.setLogo config t0 "grafana" (logo 1uy 64 64)
            do! BrandingStore.setLogo config t1 "grafana" (logo 2uy 64 64)

            let! branding = BrandingStore.forApp config "grafana"
            Assert.Equal(Some "1f6fa8", hexOf branding)
        })

[<Fact>]
let ``uploading the same logo again to the same app is accepted`` () =
    withDb (fun config ->
        task {
            let a = logo 1uy 64 64
            do! BrandingStore.setLogo config t0 "grafana" a
            do! BrandingStore.setLogo config t1 "grafana" a
            let! n = rowCount config
            Assert.Equal(1, n)
        })

[<Fact>]
let ``changing the accent replaces it and keeps the logo`` () =
    withDb (fun config ->
        task {
            let a = logo 1uy 64 64
            do! BrandingStore.setLogo config t0 "grafana" a
            do! BrandingStore.setAccent config t0 "grafana" (Some(accent "#f26b1d"))
            do! BrandingStore.setAccent config t1 "grafana" (Some(accent "#0A0A10"))

            let! branding = BrandingStore.forApp config "grafana"
            Assert.Equal(Some "0a0a10", hexOf branding)
            Assert.Equal(Some a.Sha256, logoOf branding |> Option.map _.Sha256)
        })

[<Fact>]
let ``allForApps returns every branded app, keyed by client id`` () =
    withDb (fun config ->
        task {
            let a = logo 1uy 64 32
            do! BrandingStore.setAccent config t0 "grafana" (Some(accent "#f26b1d"))
            do! BrandingStore.setLogo config t0 "photos" a
            do! BrandingStore.setAccent config t0 "recipes" (Some(accent "#1f6fa8"))
            do! BrandingStore.setLogo config t0 "recipes" (logo 2uy 128 128)

            let! all = BrandingStore.allForApps config

            Assert.Equal<string list>([ "grafana"; "photos"; "recipes" ], all |> Map.toList |> List.map fst)
            Assert.Equal(Some "f26b1d", hexOf (Map.tryFind "grafana" all))
            Assert.True((logoOf (Map.tryFind "grafana" all)).IsNone)
            Assert.Equal(Some { Sha256 = a.Sha256; Width = 64; Height = 32 }, logoOf (Map.tryFind "photos" all))
            Assert.True((hexOf (Map.tryFind "photos" all)).IsNone)
            Assert.Equal(Some "1f6fa8", hexOf (Map.tryFind "recipes" all))
            Assert.Equal(Some 128, logoOf (Map.tryFind "recipes" all) |> Option.map _.Width)
        })

[<Fact>]
let ``deleteFor removes the row and its logo, and only that app's`` () =
    withDb (fun config ->
        task {
            let a = logo 1uy 64 64
            do! BrandingStore.setAccent config t0 "grafana" (Some(accent "#f26b1d"))
            do! BrandingStore.setLogo config t0 "grafana" a
            do! BrandingStore.setAccent config t0 "photos" (Some(accent "#1f6fa8"))

            do! BrandingStore.deleteFor config "grafana"

            let! gone = BrandingStore.forApp config "grafana"
            Assert.True(gone.IsNone)
            let! bytes = BrandingStore.logoBytes config a.Sha256
            Assert.True(bytes.IsNone)
            let! photos = BrandingStore.forApp config "photos"
            Assert.Equal(Some "1f6fa8", hexOf photos)

            // An app that had none is not an error: AppDelete calls this for every app.
            do! BrandingStore.deleteFor config "never-branded"
            let! n = rowCount config
            Assert.Equal(1, n)
        })

[<Fact>]
let ``two apps cannot hold the same logo bytes`` () =
    withDb (fun config ->
        task {
            // The hash is the logo's address, so it is unique. The consequence is
            // that the same upload to a second app is refused by the database.
            // The Apps screen is not expected to produce it: two apps with a
            // byte-identical rewritten PNG is a deliberate act, and the second
            // upload surfaces as an error instead of silently sharing a row.
            let shared = logo 1uy 64 64
            do! BrandingStore.setLogo config t0 "grafana" shared

            let! error = Record.ExceptionAsync(fun () -> BrandingStore.setLogo config t0 "photos" shared :> Task)
            Assert.NotNull error

            // The failed write left nothing behind, and the first app is intact.
            let! n = rowCount config
            Assert.Equal(1, n)
            let! photos = BrandingStore.forApp config "photos"
            Assert.True(photos.IsNone)
            let! bytes = BrandingStore.logoBytes config shared.Sha256
            Assert.True(bytes.IsSome)
        })

[<Fact>]
let ``what a page reads about a logo has no bytes in it`` () =
    // The reference type has no byte field at all; this guards the type staying
    // that way, since the Apps screen and the picker read it on every request
    // and a 1.25 MB BLOB per read is the cost to avoid.
    let fields = typeof<LogoRef>.GetProperties() |> Array.map _.Name |> Array.sort
    Assert.Equal<string array>([| "Height"; "Sha256"; "Width" |], fields)
