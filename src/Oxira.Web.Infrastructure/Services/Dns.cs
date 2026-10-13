using System.Text.Json.Nodes;
using Oxira.Web.Application.Common;

namespace Oxira.Web.Infrastructure.Services;

/// <summary>TXT records through DNS over HTTPS (Cloudflare 1.1.1.1, JSON API).</summary>
public class DohTxt(HttpClient http) : IDnsTxt
{
    public async Task<IReadOnlyList<string>> LookupAsync(string name, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://cloudflare-dns.com/dns-query?name={Uri.EscapeDataString(name)}&type=TXT");
        req.Headers.Accept.ParseAdd("application/dns-json");
        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct));
        return (json?["Answer"] as JsonArray ?? []).Select(a => (string?)a?["data"] ?? "").Where(s => s != "")
            // long TXT values come split into quoted chunks: "abc" "def"
            .Select(s => string.Concat(s.Split('"', StringSplitOptions.RemoveEmptyEntries).Where(p => p.Trim() != ""))).ToList();
    }
}
