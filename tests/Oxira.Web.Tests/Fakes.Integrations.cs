using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Oxira.Web.Application.Common;

namespace Oxira.Web.Tests;

public partial class TestApp
{
    public FakeDns Dns { get; } = new();

    partial void IntegrationFakes(IServiceCollection s)
    {
        s.RemoveAll<IDnsTxt>();
        s.AddSingleton<IDnsTxt>(Dns);
        LaterFakes(s);
    }

    partial void LaterFakes(IServiceCollection s);
}

public class FakeDns : IDnsTxt
{
    public ConcurrentDictionary<string, string[]> Txt { get; } = new();
    public Task<IReadOnlyList<string>> LookupAsync(string name, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(Txt.TryGetValue(name, out var v) ? v : []);
}
