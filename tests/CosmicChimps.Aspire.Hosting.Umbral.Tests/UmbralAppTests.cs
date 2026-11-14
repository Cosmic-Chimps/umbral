using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Publishing;
using Xunit;

namespace CosmicChimps.Aspire.Hosting.Umbral.Tests;

// Model level, like UmbralHostingTests: no container, the environment is evaluated by hand.
public sealed class UmbralAppTests
{
    private static IDistributedApplicationBuilder NewBuilder() => DistributedApplication.CreateBuilder([]);

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

    private static Task<Dictionary<string, object>> RunEnvAsync(IResourceWithEnvironment resource) =>
        EnvAsync(resource, DistributedApplicationOperation.Run);

    private static Task<Dictionary<string, object>> PublishEnvAsync(IResourceWithEnvironment resource) =>
        EnvAsync(resource, DistributedApplicationOperation.Publish);

    // A resource with an http endpoint at localhost:<port>, as a project has once Aspire allocated it.
    private static IResourceBuilder<ContainerResource> AddWeb(IDistributedApplicationBuilder builder, string name, int port)
    {
        var web = builder.AddContainer(name, "img").WithHttpEndpoint(port: port, targetPort: port, name: "http");
        var endpoint = web.Resource.Annotations.OfType<EndpointAnnotation>().Single();
        endpoint.AllocatedEndpoint = new AllocatedEndpoint(endpoint, "localhost", port);
        return web;
    }

    // The generator behind a default that persists it: the one field of the wrapper that holds a ParameterDefault.
    private static ParameterDefault Inner(ParameterDefault wrapper) =>
        (ParameterDefault)wrapper.GetType()
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            .Single(f => typeof(ParameterDefault).IsAssignableFrom(f.FieldType))
            .GetValue(wrapper)!;

    private static JsonElement Declaration(Dictionary<string, object> env)
    {
        var json = Assert.IsType<string>(env["UMBRAL__DECLARATION"]);
        return JsonDocument.Parse(json).RootElement;
    }

    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!).ToArray();

    [Fact]
    public async Task Without_AddApp_or_WithPasskeys_Umbral_gets_no_declaration_variables()
    {
        var umbral = NewBuilder().AddUmbral("umbral").Resource;

        var env = await RunEnvAsync(umbral);

        Assert.DoesNotContain(env.Keys, k => k.StartsWith("UMBRAL__DECLARATION", StringComparison.Ordinal));
        Assert.DoesNotContain(env.Keys, k => k.StartsWith("UMBRAL__APP_SECRETS__", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(PasskeySignUp.InvitationsOnly, "invitations")]
    [InlineData(PasskeySignUp.Open, "open")]
    public async Task WithPasskeys_declares_the_connector_with_its_sign_up(PasskeySignUp signUp, string expected)
    {
        var umbral = NewBuilder().AddUmbral("umbral").WithPasskeys(signUp).Resource;

        var declaration = Declaration(await RunEnvAsync(umbral));

        Assert.Equal(1, declaration.GetProperty("version").GetInt32());
        Assert.Equal(expected, declaration.GetProperty("passkeys").GetProperty("signUp").GetString());
        Assert.Equal(0, declaration.GetProperty("apps").GetArrayLength());
    }

    [Fact]
    public async Task WithPasskeys_defaults_to_invitations_only()
    {
        var umbral = NewBuilder().AddUmbral("umbral").WithPasskeys().Resource;

        var declaration = Declaration(await RunEnvAsync(umbral));

        Assert.Equal("invitations", declaration.GetProperty("passkeys").GetProperty("signUp").GetString());
    }

    [Fact]
    public async Task Passkeys_are_left_out_of_the_declaration_when_not_declared()
    {
        var builder = NewBuilder();
        var umbral = builder.AddUmbral("umbral");
        umbral.AddApp("web").WithRedirectUri("https://app.example/cb");

        var declaration = Declaration(await RunEnvAsync(umbral.Resource));

        Assert.False(declaration.TryGetProperty("passkeys", out _));
    }

    [Fact]
    public async Task The_declaration_has_the_shape_of_the_contract()
    {
        var builder = NewBuilder();
        var umbral = builder.AddUmbral("umbral").WithPasskeys(PasskeySignUp.Open);
        umbral.AddApp("web")
            .WithDisplayName("Sample web app")
            .WithRedirectUri("https://app.example/signin-oidc")
            .WithPostLogoutRedirectUri("https://app.example/signout-callback-oidc");
        umbral.AddApp("my-spa", UmbralAppType.Spa)
            .WithRedirectUri("https://spa.example/cb")
            .WithConnectors("passkey", "github");

        var declaration = Declaration(await RunEnvAsync(umbral.Resource));
        var apps = declaration.GetProperty("apps");

        Assert.Equal(2, apps.GetArrayLength());

        var web = apps[0];
        Assert.Equal("web", web.GetProperty("key").GetString());
        Assert.Equal("Sample web app", web.GetProperty("name").GetString());
        Assert.Equal("web", web.GetProperty("type").GetString());
        Assert.Equal(["https://app.example/signin-oidc"], Strings(web.GetProperty("redirectUris")));
        Assert.Equal(["https://app.example/signout-callback-oidc"], Strings(web.GetProperty("postLogoutRedirectUris")));
        Assert.Equal("all-shared", web.GetProperty("connectors").GetString());

        var spa = apps[1];
        Assert.Equal("my-spa", spa.GetProperty("key").GetString());
        Assert.False(spa.TryGetProperty("name", out _));
        Assert.Equal("spa", spa.GetProperty("type").GetString());
        Assert.False(spa.TryGetProperty("postLogoutRedirectUris", out _));
        Assert.Equal(["passkey", "github"], Strings(spa.GetProperty("connectors")));

        Assert.All(declaration.EnumerateObject().Select(p => p.Name), n => Assert.Contains(n, new[] { "version", "passkeys", "apps" }));
    }

    [Fact]
    public void A_web_app_gets_a_generated_secret_parameter_of_48_characters()
    {
        var builder = NewBuilder();
        var web = builder.AddUmbral("umbral").AddApp("web");

        var secret = Assert.IsType<ParameterResource>(web.Resource.ClientSecret);

        Assert.Equal("umbral-web-client-secret", secret.Name);
        Assert.True(secret.Secret);
        Assert.Contains(secret, builder.Resources);

        // persist: true wraps the generator so Aspire keeps the value in the AppHost's user secrets.
        Assert.Equal("UserSecretsParameterDefault", secret.Default!.GetType().Name);

        var generated = Assert.IsType<GenerateParameterDefault>(Inner(secret.Default));
        Assert.Equal(48, generated.MinLength);
        Assert.False(generated.Special);
        Assert.True(generated.GetDefaultValue().Length >= 48);
    }

    [Fact]
    public void A_spa_app_has_no_secret_parameter()
    {
        var builder = NewBuilder();
        var spa = builder.AddUmbral("umbral").AddApp("spa", UmbralAppType.Spa);

        Assert.Null(spa.Resource.ClientSecret);
        Assert.DoesNotContain(builder.Resources, r => r is ParameterResource);
    }

    [Fact]
    public async Task The_secret_variable_writes_an_underscore_for_a_hyphen_and_holds_the_parameter_not_its_value()
    {
        var builder = NewBuilder();
        var umbral = builder.AddUmbral("umbral").WithPublicOrigin("https://auth.example");
        var app = umbral.AddApp("my-app").WithRedirectUri("https://app.example/cb");
        umbral.AddApp("spa", UmbralAppType.Spa).WithRedirectUri("https://spa.example/cb");

        foreach (var operation in new[] { DistributedApplicationOperation.Run, DistributedApplicationOperation.Publish })
        {
            var env = await EnvAsync(umbral.Resource, operation);

            Assert.Same(app.Resource.ClientSecret, env["UMBRAL__APP_SECRETS__my_app"]);
            Assert.DoesNotContain("UMBRAL__APP_SECRETS__my-app", env.Keys);
            Assert.DoesNotContain("UMBRAL__APP_SECRETS__spa", env.Keys);
            Assert.DoesNotContain(env.Values, v => v is string s && s.Contains("client-secret", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task A_project_gets_the_issuer_the_client_id_and_the_secret()
    {
        var builder = NewBuilder();
        var umbral = builder.AddUmbral("umbral");
        var app = umbral.AddApp("web");
        var web = AddWeb(builder, "web", 5000).WithReference(app);

        var env = await RunEnvAsync(web.Resource);

        Assert.Equal("http://localhost:8080", env["UMBRAL_ISSUER"]);
        Assert.Equal("web", env["UMBRAL_CLIENT_ID"]);
        Assert.Same(app.Resource.ClientSecret, env["UMBRAL_CLIENT_SECRET"]);
    }

    [Fact]
    public async Task A_project_of_a_spa_app_gets_no_secret()
    {
        var builder = NewBuilder();
        var app = builder.AddUmbral("umbral").AddApp("spa", UmbralAppType.Spa).WithRedirectUri("https://spa.example/cb");
        var web = AddWeb(builder, "spa", 5173).WithReference(app);

        var env = await RunEnvAsync(web.Resource);

        Assert.Equal("spa", env["UMBRAL_CLIENT_ID"]);
        Assert.Equal("http://localhost:8080", env["UMBRAL_ISSUER"]);
        Assert.DoesNotContain("UMBRAL_CLIENT_SECRET", env.Keys);
    }

    [Fact]
    public async Task The_issuer_of_an_app_reference_is_the_one_of_the_umbral_reference()
    {
        var builder = NewBuilder();
        var umbral = builder.AddUmbral("umbral").WithPublicOrigin("https://auth.example");
        var app = umbral.AddApp("web").WithRedirectUri("https://app.example/cb");
        var viaUmbral = builder.AddContainer("a", "img").WithReference(umbral);
        var viaApp = builder.AddContainer("b", "img").WithReference(app);

        foreach (var operation in new[] { DistributedApplicationOperation.Run, DistributedApplicationOperation.Publish })
        {
            Assert.Equal(
                (await EnvAsync(viaUmbral.Resource, operation))["UMBRAL_ISSUER"],
                (await EnvAsync(viaApp.Resource, operation))["UMBRAL_ISSUER"]);
        }
    }

    [Fact]
    public async Task A_web_app_with_no_redirect_URI_takes_the_callback_paths_from_the_project_endpoint()
    {
        var builder = NewBuilder();
        var umbral = builder.AddUmbral("umbral");
        var app = umbral.AddApp("web");
        AddWeb(builder, "web", 5000).WithReference(app);

        var web = Declaration(await RunEnvAsync(umbral.Resource)).GetProperty("apps")[0];

        Assert.Equal(["http://localhost:5000/signin-oidc"], Strings(web.GetProperty("redirectUris")));
        Assert.Equal(["http://localhost:5000/signout-callback-oidc"], Strings(web.GetProperty("postLogoutRedirectUris")));
    }

    [Fact]
    public async Task WithCallbackPaths_sets_the_paths_and_adds_to_the_explicit_URIs()
    {
        var builder = NewBuilder();
        var umbral = builder.AddUmbral("umbral");
        var app = umbral.AddApp("spa", UmbralAppType.Spa)
            .WithRedirectUri("https://spa.example/cb")
            .WithCallbackPaths("/callback", null);
        AddWeb(builder, "spa", 5173).WithReference(app);

        var spa = Declaration(await RunEnvAsync(umbral.Resource)).GetProperty("apps")[0];

        Assert.Equal(["https://spa.example/cb", "http://localhost:5173/callback"], Strings(spa.GetProperty("redirectUris")));
        Assert.False(spa.TryGetProperty("postLogoutRedirectUris", out _));
    }

    [Fact]
    public async Task An_explicit_redirect_URI_turns_the_default_callback_paths_off()
    {
        var builder = NewBuilder();
        var umbral = builder.AddUmbral("umbral");
        var app = umbral.AddApp("web").WithRedirectUri("https://app.example/cb");
        AddWeb(builder, "web", 5000).WithReference(app);

        var web = Declaration(await RunEnvAsync(umbral.Resource)).GetProperty("apps")[0];

        Assert.Equal(["https://app.example/cb"], Strings(web.GetProperty("redirectUris")));
    }

    [Fact]
    public async Task Publish_mode_refuses_URIs_derived_from_an_endpoint_and_says_what_to_call()
    {
        var builder = NewBuilder();
        var umbral = builder.AddUmbral("umbral").WithPublicOrigin("https://auth.example");
        var app = umbral.AddApp("web");
        AddWeb(builder, "web", 5000).WithReference(app);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => PublishEnvAsync(umbral.Resource));

        Assert.Contains("WithRedirectUri", ex.Message);
        Assert.Contains("'web'", ex.Message);
    }

    [Fact]
    public async Task Publish_mode_works_with_explicit_URIs()
    {
        var builder = NewBuilder();
        var umbral = builder.AddUmbral("umbral").WithPublicOrigin("https://auth.example");
        var app = umbral.AddApp("web")
            .WithRedirectUri("https://app.example/signin-oidc")
            .WithPostLogoutRedirectUri("https://app.example/signout-callback-oidc");
        AddWeb(builder, "web", 5000).WithReference(app);

        var web = Declaration(await PublishEnvAsync(umbral.Resource)).GetProperty("apps")[0];

        Assert.Equal(["https://app.example/signin-oidc"], Strings(web.GetProperty("redirectUris")));
    }

    [Fact]
    public async Task An_app_with_no_redirect_URI_at_all_is_an_error_naming_it()
    {
        var umbral = NewBuilder().AddUmbral("umbral");
        umbral.AddApp("spa", UmbralAppType.Spa);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RunEnvAsync(umbral.Resource));

        Assert.Contains("'spa'", ex.Message);
        Assert.Contains("redirect URI", ex.Message);
    }

    [Fact]
    public async Task A_web_app_nothing_references_is_an_error_naming_it()
    {
        var umbral = NewBuilder().AddUmbral("umbral");
        umbral.AddApp("web");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RunEnvAsync(umbral.Resource));

        Assert.Contains("'web'", ex.Message);
    }

    [Fact]
    public async Task An_app_referenced_by_two_resources_that_relies_on_callback_paths_is_an_error()
    {
        var builder = NewBuilder();
        var umbral = builder.AddUmbral("umbral");
        var app = umbral.AddApp("web");
        AddWeb(builder, "one", 5000).WithReference(app);
        AddWeb(builder, "two", 5001).WithReference(app);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RunEnvAsync(umbral.Resource));

        Assert.Contains("'web'", ex.Message);
        Assert.Contains("2 resources", ex.Message);
    }

    [Fact]
    public async Task An_app_referenced_by_two_resources_is_fine_with_explicit_URIs()
    {
        var builder = NewBuilder();
        var umbral = builder.AddUmbral("umbral");
        var app = umbral.AddApp("web").WithRedirectUri("https://app.example/cb");
        AddWeb(builder, "one", 5000).WithReference(app);
        AddWeb(builder, "two", 5001).WithReference(app);

        var web = Declaration(await RunEnvAsync(umbral.Resource)).GetProperty("apps")[0];

        Assert.Equal(["https://app.example/cb"], Strings(web.GetProperty("redirectUris")));
    }

    [Fact]
    public async Task WithClientSecret_replaces_the_generated_parameter_and_removes_it_from_the_model()
    {
        var builder = NewBuilder();
        var umbral = builder.AddUmbral("umbral");
        var own = builder.AddParameter("my-secret", "x".PadRight(40, 'x'), secret: true);
        var app = umbral.AddApp("web").WithRedirectUri("https://app.example/cb").WithClientSecret(own);

        var env = await RunEnvAsync(umbral.Resource);

        Assert.Same(own.Resource, app.Resource.ClientSecret);
        Assert.Same(own.Resource, env["UMBRAL__APP_SECRETS__web"]);
        Assert.DoesNotContain(builder.Resources, r => r.Name == "umbral-web-client-secret");
    }

    [Fact]
    public void WithClientSecret_on_a_spa_app_throws()
    {
        var builder = NewBuilder();
        var own = builder.AddParameter("my-secret", "x", secret: true);
        var app = builder.AddUmbral("umbral").AddApp("spa", UmbralAppType.Spa);

        Assert.Throws<InvalidOperationException>(() => app.WithClientSecret(own));
    }

    [Fact]
    public async Task The_publish_manifest_has_the_declaration_in_plain_text_and_the_secret_only_as_a_placeholder()
    {
        var builder = NewBuilder();
        var umbral = builder.AddUmbral("umbral").WithPublicOrigin("https://auth.example");
        umbral.AddApp("web").WithRedirectUri("https://app.example/signin-oidc");

        var executionContext = new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish);
        using var stream = new MemoryStream();
        await using (var writer = new Utf8JsonWriter(stream))
        {
            var manifest = new ManifestPublishingContext(executionContext, Path.Combine(Path.GetTempPath(), "manifest.json"), writer);
            writer.WriteStartObject();
            await manifest.WriteContainerAsync(umbral.Resource);
            writer.WriteEndObject();
        }

        var root = JsonDocument.Parse(Encoding.UTF8.GetString(stream.ToArray())).RootElement;
        var env = root.GetProperty("env");

        Assert.Equal("{umbral-web-client-secret.value}", env.GetProperty("UMBRAL__APP_SECRETS__web").GetString());
        Assert.Contains("https://app.example/signin-oidc", env.GetProperty("UMBRAL__DECLARATION").GetString());
        Assert.Equal(1, JsonDocument.Parse(env.GetProperty("UMBRAL__DECLARATION").GetString()!).RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public void An_app_is_named_after_Umbral_and_its_key_and_is_its_child_without_a_manifest_entry()
    {
        var builder = NewBuilder();
        var umbral = builder.AddUmbral("auth");
        var app = umbral.AddApp("web");

        Assert.Equal("auth-web", app.Resource.Name);
        Assert.Equal("web", app.Resource.Key);
        Assert.Same(umbral.Resource, app.Resource.Parent);
        Assert.Contains(app.Resource, umbral.Resource.Apps);
        Assert.Contains(app.Resource, builder.Resources);
        Assert.Contains(app.Resource.Annotations, a => a is ManifestPublishingCallbackAnnotation { Callback: null });
    }

    [Theory]
    [InlineData("")]
    [InlineData("Web")]
    [InlineData("my_app")]
    [InlineData("a b")]
    [InlineData("this-key-is-longer-than-thirty-two-characters")]
    [InlineData("web\n")]
    public void A_key_outside_the_rule_throws(string key)
    {
        var umbral = NewBuilder().AddUmbral("umbral");

        Assert.Throws<ArgumentException>(() => umbral.AddApp(key));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("my-app-2")]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345")]
    public void A_key_inside_the_rule_is_accepted(string key)
    {
        var umbral = NewBuilder().AddUmbral("umbral");

        Assert.Equal(key, umbral.AddApp(key).Resource.Key);
    }

    [Fact]
    public void A_repeated_key_throws()
    {
        var umbral = NewBuilder().AddUmbral("umbral");
        umbral.AddApp("web");

        Assert.Throws<ArgumentException>(() => umbral.AddApp("web", UmbralAppType.Spa));
    }

    [Fact]
    public async Task Umbral_has_a_second_URL_to_claim_it()
    {
        var umbral = NewBuilder().AddUmbral("umbral").Resource;

        var urls = umbral.Annotations.OfType<ResourceUrlsCallbackAnnotation>().ToList();

        Assert.NotEmpty(urls);

        var context = new ResourceUrlsCallbackContext(
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run), umbral, []);
        foreach (var annotation in urls)
        {
            await annotation.Callback(context);
        }

        Assert.Contains(context.Urls, u => u.DisplayText == "Claim (first run)" && u.Url.EndsWith("/admin/setup", StringComparison.Ordinal));
        Assert.Contains(context.Urls, u => u.DisplayText == "Admin" && u.Url.EndsWith("/admin", StringComparison.Ordinal));
    }
}
