using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Oxira.Web.Application.Common;

namespace Oxira.Web.Tests;

public partial class TestApp
{
    public FakeWordPress Wp { get; } = new();
    public FakeMeta Meta { get; } = new();
    public FakeProbe Probe { get; } = new();

    partial void LaterFakes(IServiceCollection s)
    {
        s.RemoveAll<IWordPressClient>();
        s.AddSingleton<IWordPressClient>(Wp);
        s.RemoveAll<IMetaClient>();
        s.AddSingleton<IMetaClient>(Meta);
        s.RemoveAll<IUptimeProbe>();
        s.AddSingleton<IUptimeProbe>(Probe);
    }

    public HttpClient NoRedirect() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}

public class FakeWordPress : IWordPressClient
{
    public string Password { get; set; } = "abcd efgh ijkl mnop";
    public ConcurrentQueue<(string Title, string Html)> Posts { get; } = new();
    public Task<WpResult> VerifyAsync(string siteUrl, string user, string appPassword, CancellationToken ct = default) =>
        Task.FromResult(appPassword == Password ? new WpResult(true) : new WpResult(false, "wp_auth"));
    public Task<WpResult> PublishAsync(string siteUrl, string user, string appPassword, string title, string html, string excerpt, CancellationToken ct = default)
    {
        if (appPassword != Password) return Task.FromResult(new WpResult(false, "wp_auth"));
        Posts.Enqueue((title, html));
        return Task.FromResult(new WpResult(true, null, $"{siteUrl}/?p={Posts.Count}"));
    }
}

public class FakeMeta : IMetaClient
{
    public bool Enabled { get; set; } = true;
    public ConcurrentQueue<(string Page, string Image, string Caption)> Published { get; } = new();
    public string AuthorizeUrl(string state, string redirectUri) => $"https://www.facebook.test/dialog/oauth?state={Uri.EscapeDataString(state)}&redirect_uri={Uri.EscapeDataString(redirectUri)}";
    public Task<MetaAccount> ConnectAsync(string code, string redirectUri, CancellationToken ct = default) =>
        Task.FromResult(new MetaAccount("Owner Name", "user-token", [new MetaPage("p1", "Rose Page", "page-token", "ig1", "rose.ig")], [new MetaAdAccount("act_1", "Rose Ads", "SAR")]));
    public Task<MetaPublishResult> PublishAsync(MetaPage page, string imageUrl, string caption, IReadOnlyCollection<string> platforms, CancellationToken ct = default)
    {
        Published.Enqueue((page.Id, imageUrl, caption));
        return Task.FromResult(new MetaPublishResult(true, "fb_1", platforms.Contains("instagram") ? "ig_1" : null, null));
    }
}

public class FakeProbe : IUptimeProbe
{
    public ConcurrentDictionary<string, bool> Down { get; } = new();
    public Task<(bool Ok, int Ms)> ProbeAsync(string url, CancellationToken ct = default) => Task.FromResult((!Down.ContainsKey(url), 120));
}
