/// The single mounted directory that holds everything Umbral persists.
///
/// Principle II: "All state MUST live in one mounted data directory, so that
/// backup is `cp` and restore is `cp` back." This module is where that promise
/// is kept honest — every path Umbral writes to is derived here, so a new one
/// cannot quietly appear somewhere else.
module Umbral.Infrastructure.DataDirectory

open System
open System.IO

[<Literal>]
let ConfigurationKey = "UMBRAL:DATA_DIRECTORY"

[<Literal>]
let EnvironmentVariable = "UMBRAL__DATA_DIRECTORY"

[<Literal>]
let DefaultPath = "/data"

type DataDirectory =
    private
        { Root: string }

    member this.Path = this.Root

    /// The SQLite file. Umbral's six tables and OpenIddict's four.
    member this.DatabaseFile = Path.Combine(this.Root, "umbral.db")

    /// Token signing and encryption certificates (Keys.fs), and the ASP.NET
    /// Core Data Protection key ring. Both live here for the same reason: lose
    /// them and every issued token is undecryptable and every stored connector
    /// secret unreadable.
    member this.KeysDirectory = Path.Combine(this.Root, "keys")

/// Resolve and create. Fails loudly rather than starting half-working: a
/// read-only or unwritable data directory means Umbral would accept a connector
/// secret and silently lose it, which is worse than refusing to start.
let resolve (configured: string | null) : Result<DataDirectory, string> =
    let root =
        match configured with
        | null -> DefaultPath
        | value when String.IsNullOrWhiteSpace value -> DefaultPath
        | value -> value.Trim()

    try
        Directory.CreateDirectory root |> ignore

        // Creating the directory is not proof it can be written to: a read-only
        // bind mount satisfies the first and fails the second.
        let probe = Path.Combine(root, ".umbral-write-probe")
        File.WriteAllText(probe, "")
        File.Delete probe

        Ok { Root = Path.GetFullPath root }
    with
    | :? UnauthorizedAccessException ->
        Error(
            $"The data directory \"{root}\" is not writable. Check the ownership of the mounted volume, "
            + $"or set {EnvironmentVariable} to a directory Umbral can write to."
        )
    | :? IOException as ex ->
        Error(
            $"The data directory \"{root}\" could not be prepared: {ex.Message}. "
            + $"Mount a writable volume there, or set {EnvironmentVariable} to another path."
        )

/// Read through a configuration lookup, accepting either spelling of the
/// setting, the same way PublicOrigin does.
let fromConfiguration (read: string -> string | null) =
    match read ConfigurationKey with
    | null -> resolve (read EnvironmentVariable)
    | value -> resolve value
