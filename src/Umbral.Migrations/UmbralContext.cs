using Microsoft.EntityFrameworkCore;

namespace Umbral.Migrations;

/// <summary>
/// The EF Core context, which exists for OpenIddict and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Umbral's own six tables are raw <c>Microsoft.Data.Sqlite</c> with
/// hand-written SQL (<c>Infrastructure/Database.fs</c>). OpenIddict ships
/// stores for EF Core, EF6 and MongoDB only, and hand-writing
/// <c>IOpenIddictApplicationStore</c> and its three siblings would be weeks of
/// work on the most security-critical path in the product — exactly what
/// Principle III says not to do. So EF Core is here, scoped to the four tables
/// OpenIddict owns, and nowhere else.
/// </para>
/// <para>
/// It lives in this C# project rather than in the F# one because EF Core's
/// design-time tooling scaffolds C#, and a context separated from its own
/// migrations forces a project reference cycle: the migrations need the
/// context, and the web app needs the migrations at runtime to call
/// <c>MigrateAsync</c>. Keeping both here makes the dependency one-way —
/// Umbral → Umbral.Migrations — and nothing in Umbral's F# code refers to this
/// type by name.
/// </para>
/// <para>
/// Deliberately empty of DbSets. <c>UseOpenIddict()</c> on the options builder
/// adds OpenIddict's four entity sets; a DbSet here would mean an Umbral table
/// had crossed into the EF lane, which is the thing to notice.
/// </para>
/// </remarks>
public sealed class UmbralContext(DbContextOptions<UmbralContext> options) : DbContext(options);
