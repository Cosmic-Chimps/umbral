/// Liveness for a container orchestrator or a proxy health check (FR-032).
///
/// Unauthenticated and unlimited on purpose: whoever asks is a machine with no
/// credential, and a limit would turn a busy minute into a restart loop. The
/// body is one word for both outcomes and says nothing about this instance's
/// configuration, version or data.
///
/// It touches SQLite with `SELECT 1`. A process that is up but cannot open its
/// database serves every login as a 500, and "can serve" is the only thing worth
/// reporting; the cost is one pooled connection per probe. It deliberately does
/// not check connectors, the clock or the key ring: a probe that fails for a
/// reason a restart cannot fix gets the container killed for nothing.
module Umbral.Handlers.Health

open Microsoft.Extensions.Logging
open Oxpecker
open Umbral.Infrastructure

let handler: EndpointHandler =
    fun ctx ->
        task {
            try
                use! connection = Database.openConnection (ctx.GetService<Database.DbConfig>())
                use command = connection.CreateCommand()
                command.CommandText <- "SELECT 1"
                let! _ = command.ExecuteScalarAsync()
                return! ctx.WriteText "ok"
            with ex ->
                ctx.GetLogger().LogWarning(ex, "Health check could not reach the database")
                ctx.SetStatusCode 503
                return! ctx.WriteText "unavailable"
        }

let endpoint = GET [ route "/healthz" handler ]
