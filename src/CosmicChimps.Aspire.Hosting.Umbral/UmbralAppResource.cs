using System;
using System.Collections.Generic;
using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>Who can sign up with a passkey on the Umbral instance.</summary>
public enum PasskeySignUp
{
    /// <summary>Only people with an invitation can sign up (Umbral's own default).</summary>
    InvitationsOnly,

    /// <summary>Anyone can sign up.</summary>
    Open,
}

/// <summary>The kind of an app declared to Umbral.</summary>
public enum UmbralAppType
{
    /// <summary>A confidential app that holds a client secret, such as a server-rendered site.</summary>
    Web,

    /// <summary>A public app that has no secret and signs in with PKCE only, such as a single-page app.</summary>
    Spa,
}

/// <summary>
/// An app declared to Umbral at start: the OIDC client that a project signs in through. It is part of the
/// model only, it has no process of its own.
/// </summary>
public sealed class UmbralAppResource : Resource, IResourceWithParent<UmbralResource>
{
    private readonly List<string> _redirectUris = [];
    private readonly List<string> _postLogoutRedirectUris = [];
    private readonly HashSet<IResource> _referrers = [];

    internal UmbralAppResource(string name, string key, UmbralAppType type, UmbralResource parent)
        : base(name)
    {
        Key = key;
        Type = type;
        Parent = parent;
    }

    /// <summary>The key of the app, which is its client ID.</summary>
    public string Key { get; }

    /// <summary>Whether the app is confidential (<see cref="UmbralAppType.Web"/>) or public (<see cref="UmbralAppType.Spa"/>).</summary>
    public UmbralAppType Type { get; }

    /// <summary>The Umbral resource the app is declared to.</summary>
    public UmbralResource Parent { get; }

    /// <summary>The client secret, for a <see cref="UmbralAppType.Web"/> app; <see langword="null"/> for a <see cref="UmbralAppType.Spa"/> app.</summary>
    public ParameterResource? ClientSecret { get; internal set; }

    /// <summary>The display name set with <c>WithDisplayName</c>, or <see langword="null"/> when Umbral should use the key.</summary>
    public string? DisplayName { get; internal set; }

    /// <summary>The redirect URIs given with <c>WithRedirectUri</c>, not the ones derived from an endpoint.</summary>
    public IReadOnlyList<string> RedirectUris => _redirectUris;

    /// <summary>The post-logout redirect URIs given with <c>WithPostLogoutRedirectUri</c>, not the ones derived from an endpoint.</summary>
    public IReadOnlyList<string> PostLogoutRedirectUris => _postLogoutRedirectUris;

    /// <summary>The connector slugs set with <c>WithConnectors</c>, or <see langword="null"/> for every shared connector.</summary>
    public IReadOnlyList<string>? Connectors { get; internal set; }

    /// <summary>The sign-in callback path set with <c>WithCallbackPaths</c>, or <see langword="null"/> when it was not called.</summary>
    public string? SignInPath { get; internal set; }

    /// <summary>The sign-out callback path set with <c>WithCallbackPaths</c>, or <see langword="null"/> for none.</summary>
    public string? SignOutPath { get; internal set; }

    /// <summary>Whether <c>WithCallbackPaths</c> was called.</summary>
    internal bool CallbackPathsGiven { get; set; }

    /// <summary>The resources that reference the app with <c>WithReference</c>.</summary>
    internal IReadOnlyCollection<IResource> Referrers => _referrers;

    internal void AddRedirectUri(string uri) => _redirectUris.Add(uri);

    internal void AddPostLogoutRedirectUri(string uri) => _postLogoutRedirectUris.Add(uri);

    internal void AddReferrer(IResource referrer) => _referrers.Add(referrer);

    /// <summary>
    /// The callback paths that apply: the ones given, or for a <see cref="UmbralAppType.Web"/> app with no
    /// explicit redirect URI the usual ASP.NET Core OpenID Connect ones; otherwise none.
    /// </summary>
    internal (string SignIn, string? SignOut)? EffectiveCallbackPaths =>
        CallbackPathsGiven ? (SignInPath!, SignOutPath)
        : Type == UmbralAppType.Web && _redirectUris.Count == 0 ? ("/signin-oidc", "/signout-callback-oidc")
        : null;
}
