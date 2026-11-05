/// The GC settings the image sets with `ENV` (Dockerfile), in one list for the
/// two tests that need them: IdleMemoryTests gives them to the process it
/// measures, so that it runs as the container does, and MetaTests checks that
/// the Dockerfile still says the same, so the two cannot drift apart.
module Umbral.Tests.Infrastructure.ImageEnvironment

let imageEnvironment = [ "DOTNET_gcServer", "0"; "DOTNET_GCgen0size", "0x200000" ]
