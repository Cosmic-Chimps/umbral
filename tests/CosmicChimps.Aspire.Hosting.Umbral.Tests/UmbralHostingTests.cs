using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Publishing;
using Xunit;

namespace CosmicChimps.Aspire.Hosting.Umbral.Tests;

// Model level: nothing here starts a container or needs Docker. The model is built in run mode
// and each test evaluates the environment for the operation it is about.
public sealed partial class UmbralHostingTests
{
    private static IDistributedApplicationBuilder NewBuilder() => DistributedApplication.CreateBuilder([]);

    // Runs the resource's environment callbacks for an operation, as Aspire does when it starts or
    // publishes it. A value is a string, or the ParameterResource itself when it is not known yet.
    private static async Task<Dictionary<string, object>> EnvAsync(IResourceWithEnvironment resource, DistributedApplicationOperation operation)
    {
        var executionContext = new DistributedApplicationExecutionContext(operation);
        var env = new Dictionary<string, object>();
        var context = new EnvironmentCallbackContext(executionContext, resource, env);

        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        return env;
    }

    private static Task<Dictionary<string, object>> PublishEnvAsync(IResourceWithEnvironment resource) =>
        EnvAsync(resource, DistributedApplicationOperation.Publish);

    [Fact]
    public async Task Run_mode_sets_the_laptop_pair_on_http_localhost()
    {
        var umbral = NewBuilder().AddUmbral("umbral").Resource;

        var env = await EnvAsync(umbral, DistributedApplicationOperation.Run);

        Assert.Equal("http://localhost:8080", env["UMBRAL__PUBLIC_ORIGIN"]);
        Assert.Equal("Development", env["ASPNETCORE_ENVIRONMENT"]);
        Assert.Equal("true", env["UMBRAL__INSECURE_ALLOW_HTTP"]);
    }

    [Theory]
    [InlineData(null, 8080)]
    [InlineData(8081, 8081)]
    public void The_host_port_is_fixed_and_the_target_is_8080(int? port, int expected)
    {
        var umbral = NewBuilder().AddUmbral("umbral", port).Resource;

        var endpoint = Assert.Single(umbral.Annotations.OfType<EndpointAnnotation>());

        Assert.Equal("http", endpoint.Name);
        Assert.Equal(expected, endpoint.Port);
        Assert.Equal(8080, endpoint.TargetPort);
    }

    [Fact]
    public async Task Run_mode_uses_the_port_in_the_default_origin()
    {
        var umbral = NewBuilder().AddUmbral("umbral", port: 8081).Resource;

        var env = await EnvAsync(umbral, DistributedApplicationOperation.Run);

        Assert.Equal("http://localhost:8081", env["UMBRAL__PUBLIC_ORIGIN"]);
    }

    [Fact]
    public async Task Run_mode_with_an_https_origin_has_no_pair()
    {
        var umbral = NewBuilder().AddUmbral("umbral").WithPublicOrigin("https://x.example/").Resource;

        var env = await EnvAsync(umbral, DistributedApplicationOperation.Run);

        Assert.Equal("https://x.example", env["UMBRAL__PUBLIC_ORIGIN"]);
        Assert.DoesNotContain("ASPNETCORE_ENVIRONMENT", env.Keys);
        Assert.DoesNotContain("UMBRAL__INSECURE_ALLOW_HTTP", env.Keys);
    }

    [Theory]
    [InlineData("http://localhost:9000", true)]
    [InlineData("http://127.0.0.1:9000", true)]
    [InlineData("http://[::1]:9000", true)]
    [InlineData("http://x.example", false)]
    [InlineData("http://192.168.1.5:8080", false)]
    public async Task Run_mode_sets_the_pair_only_for_a_loopback_http_origin(string origin, bool pair)
    {
        var umbral = NewBuilder().AddUmbral("umbral").WithPublicOrigin(origin).Resource;

        var env = await EnvAsync(umbral, DistributedApplicationOperation.Run);

        Assert.Equal(pair, env.ContainsKey("UMBRAL__INSECURE_ALLOW_HTTP"));
        Assert.Equal(pair, env.ContainsKey("ASPNETCORE_ENVIRONMENT"));
    }

    [Theory]
    [InlineData("http://localhost:9000", true)]
    [InlineData("https://x.example", false)]
    [InlineData("http://x.example", false)]
    public async Task Run_mode_with_a_parameter_origin_resolves_it_to_decide_the_pair(string value, bool pair)
    {
        var builder = NewBuilder();
        var origin = builder.AddParameter("origin", value);
        var umbral = builder.AddUmbral("umbral").WithPublicOrigin(origin).Resource;

        var env = await EnvAsync(umbral, DistributedApplicationOperation.Run);

        Assert.Equal(value, env["UMBRAL__PUBLIC_ORIGIN"]);
        Assert.Equal(pair, env.ContainsKey("UMBRAL__INSECURE_ALLOW_HTTP"));
    }

    [Fact]
    public async Task Publish_mode_with_an_https_origin_never_sets_Development_or_the_pair()
    {
        var umbral = NewBuilder().AddUmbral("umbral").WithPublicOrigin("https://x.example").Resource;

        var env = await PublishEnvAsync(umbral);

        Assert.Equal("https://x.example", env["UMBRAL__PUBLIC_ORIGIN"]);
        Assert.DoesNotContain("ASPNETCORE_ENVIRONMENT", env.Keys);
        Assert.DoesNotContain("UMBRAL__INSECURE_ALLOW_HTTP", env.Keys);
    }

    [Fact]
    public async Task Publish_mode_with_a_parameter_origin_passes_the_parameter_and_no_pair()
    {
        var builder = NewBuilder();
        var origin = builder.AddParameter("origin", "http://localhost:9000");
        var umbral = builder.AddUmbral("umbral").WithPublicOrigin(origin).Resource;

        var env = await PublishEnvAsync(umbral);

        Assert.Same(origin.Resource, env["UMBRAL__PUBLIC_ORIGIN"]);
        Assert.DoesNotContain("ASPNETCORE_ENVIRONMENT", env.Keys);
        Assert.DoesNotContain("UMBRAL__INSECURE_ALLOW_HTTP", env.Keys);
    }

    [Fact]
    public async Task Publish_mode_without_an_origin_throws_and_names_the_call()
    {
        var umbral = NewBuilder().AddUmbral("umbral").Resource;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => PublishEnvAsync(umbral));

        Assert.Contains("WithPublicOrigin", ex.Message);
        Assert.Contains("'umbral'", ex.Message);
    }

    [Theory]
    [InlineData("http://x.example")]
    [InlineData("http://localhost:8080")]
    public async Task Publish_mode_with_an_http_origin_throws(string origin)
    {
        var umbral = NewBuilder().AddUmbral("umbral").WithPublicOrigin(origin).Resource;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => PublishEnvAsync(umbral));

        Assert.Contains("https", ex.Message);
    }

    [Theory]
    [InlineData("auth.example.com")]
    [InlineData("ftp://x.example")]
    [InlineData("https://x.example/sub")]
    [InlineData("https://x.example?x=1")]
    [InlineData("https://x.example/#top")]
    public void WithPublicOrigin_rejects_what_is_not_an_origin(string origin)
    {
        var umbral = NewBuilder().AddUmbral("umbral");

        Assert.Throws<ArgumentException>(() => umbral.WithPublicOrigin(origin));
    }

    [Fact]
    public async Task The_publish_manifest_has_no_Development_and_no_laptop_pair()
    {
        var builder = NewBuilder().AddUmbral("umbral").WithDataVolume().WithPublicOrigin("https://x.example");
        var umbral = builder.Resource;

        var executionContext = new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish);
        using var stream = new MemoryStream();
        await using (var writer = new Utf8JsonWriter(stream))
        {
            var manifest = new ManifestPublishingContext(executionContext, Path.Combine(Path.GetTempPath(), "manifest.json"), writer);
            writer.WriteStartObject();
            await manifest.WriteContainerAsync(umbral);
            writer.WriteEndObject();
        }

        var json = Encoding.UTF8.GetString(stream.ToArray());

        Assert.Contains("UMBRAL__PUBLIC_ORIGIN", json);
        Assert.Contains("https://x.example", json);
        Assert.DoesNotContain("Development", json);
        Assert.DoesNotContain("INSECURE_ALLOW_HTTP", json);
    }

    [Fact]
    public async Task WithReference_gives_the_public_origin_in_run_mode()
    {
        var builder = NewBuilder();
        var umbral = builder.AddUmbral("umbral");
        var app = builder.AddContainer("app", "img").WithReference(umbral);

        var env = await EnvAsync(app.Resource, DistributedApplicationOperation.Run);

        Assert.Equal("http://localhost:8080", env["UMBRAL_ISSUER"]);
        Assert.DoesNotContain(env.Keys, k => k.StartsWith("services__", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithReference_gives_an_explicit_origin_in_both_modes()
    {
        var builder = NewBuilder();
        var umbral = builder.AddUmbral("umbral").WithPublicOrigin("https://x.example");
        var app = builder.AddContainer("app", "img").WithReference(umbral);

        var run = await EnvAsync(app.Resource, DistributedApplicationOperation.Run);
        var publish = await EnvAsync(app.Resource, DistributedApplicationOperation.Publish);

        Assert.Equal("https://x.example", run["UMBRAL_ISSUER"]);
        Assert.Equal("https://x.example", publish["UMBRAL_ISSUER"]);
        Assert.DoesNotContain(publish.Keys, k => k.StartsWith("services__", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithReference_with_a_parameter_origin_gives_that_parameter_at_publish()
    {
        var builder = NewBuilder();
        var origin = builder.AddParameter("origin", "https://x.example");
        var umbral = builder.AddUmbral("umbral").WithPublicOrigin(origin);
        var app = builder.AddContainer("app", "img").WithReference(umbral);

        var run = await EnvAsync(app.Resource, DistributedApplicationOperation.Run);
        var publish = await EnvAsync(app.Resource, DistributedApplicationOperation.Publish);

        Assert.Equal("https://x.example", run["UMBRAL_ISSUER"]);
        Assert.Same(origin.Resource, publish["UMBRAL_ISSUER"]);
    }

    [Fact]
    public async Task WithReference_at_publish_without_an_origin_throws()
    {
        var builder = NewBuilder();
        var umbral = builder.AddUmbral("umbral");
        var app = builder.AddContainer("app", "img").WithReference(umbral);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => PublishEnvAsync(app.Resource));

        Assert.Contains("WithPublicOrigin", ex.Message);
    }

    [Fact]
    public void The_default_image_is_the_baked_tag_of_the_ghcr_image()
    {
        var umbral = NewBuilder().AddUmbral("umbral").Resource;

        var image = Assert.Single(umbral.Annotations.OfType<ContainerImageAnnotation>());
        var baked = typeof(UmbralResource).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "UmbralImageTag").Value;

        Assert.Equal("ghcr.io", image.Registry);
        Assert.Equal("cosmic-chimps/umbral", image.Image);
        Assert.Equal(baked, image.Tag);
        Assert.NotEqual("latest", image.Tag);
        Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$", image.Tag!);
    }

    [Fact]
    public void Without_a_Version_the_tag_is_the_last_released_changelog_section()
    {
        var metadata = typeof(UmbralResource).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(a => a.Key, a => a.Value);
        var tag = metadata["UmbralImageTag"];

        // A build with -p:Version=X.Y.Z (the release job's pack) bakes X.Y.Z, whatever the changelog says.
        if (metadata["UmbralImageTagSource"] == "Version")
        {
            return;
        }

        var changelog = File.ReadAllText(Path.Combine(RepositoryRoot(), "CHANGELOG.md"));

        Assert.Equal(FirstHeading().Match(changelog).Groups[1].Value, tag);
    }

    [GeneratedRegex(@"(?m)^## (\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?)")]
    private static partial Regex FirstHeading();

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Umbral.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Umbral.slnx not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public async Task WithTrustedProxies_joins_the_entries_with_a_comma()
    {
        var umbral = NewBuilder().AddUmbral("umbral").WithTrustedProxies("10.99.0.1", "192.0.2.0/24").Resource;

        var env = await EnvAsync(umbral, DistributedApplicationOperation.Run);

        Assert.Equal("10.99.0.1,192.0.2.0/24", env["UMBRAL__TRUSTED_PROXIES"]);
    }

    [Fact]
    public async Task Trusted_proxies_are_absent_unless_set()
    {
        var umbral = NewBuilder().AddUmbral("umbral").Resource;

        var env = await EnvAsync(umbral, DistributedApplicationOperation.Run);

        Assert.DoesNotContain("UMBRAL__TRUSTED_PROXIES", env.Keys);
    }

    [Fact]
    public void WithTrustedProxies_rejects_an_empty_list_and_an_entry_with_a_comma()
    {
        var umbral = NewBuilder().AddUmbral("umbral");

        Assert.Throws<ArgumentException>(() => umbral.WithTrustedProxies());
        Assert.Throws<ArgumentException>(() => umbral.WithTrustedProxies("10.0.0.1,10.0.0.2"));
        Assert.Throws<ArgumentException>(() => umbral.WithTrustedProxies(" "));
    }

    [Fact]
    public void WithDataVolume_mounts_data_with_a_generated_or_given_name()
    {
        var generated = NewBuilder().AddUmbral("umbral").WithDataVolume().Resource;
        var named = NewBuilder().AddUmbral("umbral").WithDataVolume("x").Resource;

        var generatedMount = Assert.Single(generated.Annotations.OfType<ContainerMountAnnotation>());
        var namedMount = Assert.Single(named.Annotations.OfType<ContainerMountAnnotation>());

        Assert.Equal("/data", generatedMount.Target);
        Assert.Equal(ContainerMountType.Volume, generatedMount.Type);
        Assert.False(string.IsNullOrEmpty(generatedMount.Source));
        Assert.Equal("/data", namedMount.Target);
        Assert.Equal("x", namedMount.Source);
    }

    [Fact]
    public void WithDataBindMount_mounts_data_from_a_folder()
    {
        var umbral = NewBuilder().AddUmbral("umbral").WithDataBindMount("./d").Resource;

        var mount = Assert.Single(umbral.Annotations.OfType<ContainerMountAnnotation>());

        Assert.Equal("/data", mount.Target);
        Assert.Equal(ContainerMountType.BindMount, mount.Type);
    }
}
