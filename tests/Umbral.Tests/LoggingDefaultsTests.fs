/// What the container log looks like by default.
///
/// The first-run setup banner is the one thing in `docker logs` an operator has
/// to find, and EF Core's per-statement Information logging buried it under
/// about 120 lines of DDL. The defaults live in the committed appsettings.json,
/// which is the file the standard configuration layering lets an environment
/// variable override — a programmatic `AddFilter` would win over the operator's
/// `Logging__LogLevel__...` instead.
module Umbral.Tests.LoggingDefaultsTests

open System.IO
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Xunit
open Umbral.Tests.Infrastructure

let private appsettings =
    let rec up (dir: DirectoryInfo | null) =
        match dir with
        | null -> failwith "src/Umbral/appsettings.json was not found above the test binary."
        | d ->
            let candidate = Path.Combine(d.FullName, "src", "Umbral", "appsettings.json")
            if File.Exists candidate then candidate else up d.Parent

    up (DirectoryInfo System.AppContext.BaseDirectory)

/// The app with the committed defaults, then `overrides` on top, which is the
/// position an environment variable has.
let private enabled (overrides: (string * string) list) (category: string) (level: LogLevel) =
    task {
        use! app =
            Fixtures.createAppWith [] (fun builder ->
                builder.Configuration.AddJsonFile(appsettings, optional = false) |> ignore
                builder.Configuration.AddInMemoryCollection(dict overrides) |> ignore)

        return app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(category).IsEnabled level
    }

[<Fact>]
let ``EF Core statements are not logged by default`` () =
    task {
        let! on = enabled [] "Microsoft.EntityFrameworkCore.Database.Command" LogLevel.Information
        Assert.False on
    }

[<Fact>]
let ``per-request framework chatter and scheduler startup are quiet by default`` () =
    task {
        let! request = enabled [] "Microsoft.AspNetCore.Hosting.Diagnostics" LogLevel.Information
        let! scheduler = enabled [] "Quartz.Core.QuartzScheduler" LogLevel.Information
        Assert.False request
        Assert.False scheduler
    }

[<Fact>]
let ``warnings and the listening address still appear`` () =
    task {
        let! warning = enabled [] "Microsoft.EntityFrameworkCore.Database.Command" LogLevel.Warning
        let! listening = enabled [] "Microsoft.Hosting.Lifetime" LogLevel.Information
        let! own = enabled [] "Umbral.Anything" LogLevel.Information
        Assert.True warning
        Assert.True listening
        Assert.True own
    }

[<Fact>]
let ``an operator can raise the level with Logging__LogLevel environment variables`` () =
    task {
        // `Logging__LogLevel__X` reaches IConfiguration as `Logging:LogLevel:X`.
        let! on =
            enabled
                [ "Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command", "Information" ]
                "Microsoft.EntityFrameworkCore.Database.Command"
                LogLevel.Information

        Assert.True on
    }
