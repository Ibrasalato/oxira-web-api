using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Oxira.Web.Application.Common;

namespace Oxira.Web.Infrastructure.Services;

/// <summary>
/// Outbound requests to addresses visitors type (site audit, agent demo, WordPress sites).
/// The socket only connects to public IP addresses: the check runs on the resolved address at connect time,
/// so DNS rebinding and redirects to internal hosts are refused. No proxy (it would hide the real target).
/// </summary>
public static class PublicNetwork
{
    public const string ClientName = "public-web";

    public static SocketsHttpHandler Handler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectTimeout = TimeSpan.FromSeconds(8),
        ConnectCallback = ConnectAsync,
    };

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext ctx, CancellationToken ct)
    {
        var host = ctx.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, ct);
        if (addresses.Length == 0 || addresses.Any(a => !IsPublic(a))) throw new HttpRequestException($"blocked address for {host}");
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, ctx.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>false for loopback, private, link-local, carrier-grade NAT, multicast, documentation and reserved ranges</summary>
    public static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return false;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return !(b[0] == 0 || b[0] == 10 || b[0] == 127 || b[0] >= 224 ||
                     b[0] == 100 && b[1] >= 64 && b[1] <= 127 ||
                     b[0] == 169 && b[1] == 254 ||
                     b[0] == 172 && b[1] >= 16 && b[1] <= 31 ||
                     b[0] == 192 && b[1] == 0 && (b[2] == 0 || b[2] == 2) ||
                     b[0] == 192 && b[1] == 168 ||
                     b[0] == 198 && (b[1] == 18 || b[1] == 19) ||
                     b[0] == 198 && b[1] == 51 && b[2] == 100 ||
                     b[0] == 203 && b[1] == 0 && b[2] == 113);
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.IPv6Loopback) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return false;
            var b = ip.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return false; // fc00::/7 unique local
            if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8) return false; // 2001:db8::/32 documentation
            if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B) return false; // 64:ff9b::/96 NAT64
            return true;
        }
        return false;
    }

    /// <summary>http(s) on the default ports, a real host name, no credentials</summary>
    public static bool IsAllowedUrl(Uri u) =>
        u.IsAbsoluteUri && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) && u.IsDefaultPort && string.IsNullOrEmpty(u.UserInfo) &&
        !string.IsNullOrEmpty(u.Host) && (u.Host.Contains('.') || IPAddress.TryParse(u.Host, out _));
}

public class SafeFetcher(IHttpClientFactory factory) : ISafeFetcher
{
    private const int MaxRedirects = 4;

    public async Task<FetchResult> GetAsync(Uri url, int maxBytes = 2_000_000, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        var http = factory.CreateClient(PublicNetwork.ClientName);
        var current = url;
        try
        {
            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                if (!PublicNetwork.IsAllowedUrl(current)) return Fail(current, sw, "blocked");
                using var req = new HttpRequestMessage(HttpMethod.Get, current);
                req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (compatible; OxiraBot/1.0; +https://oxira.sa)");
                req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,text/plain;q=0.9,*/*;q=0.5");
                using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                var status = (int)res.StatusCode;
                if (status is >= 300 and < 400 && res.Headers.Location is { } loc)
                {
                    current = loc.IsAbsoluteUri ? loc : new Uri(current, loc);
                    continue;
                }
                await using var stream = await res.Content.ReadAsStreamAsync(cts.Token);
                var buffer = new MemoryStream();
                var chunk = new byte[16384];
                long total = 0;
                int n;
                while ((n = await stream.ReadAsync(chunk, cts.Token)) > 0)
                {
                    total += n;
                    if (buffer.Length < maxBytes) buffer.Write(chunk, 0, (int)Math.Min(n, maxBytes - buffer.Length));
                    if (total > maxBytes * 4L) break; // stop reading huge pages
                }
                var charset = res.Content.Headers.ContentType?.CharSet;
                Encoding enc;
                try { enc = string.IsNullOrEmpty(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset.Trim('"')); } catch { enc = Encoding.UTF8; }
                return new FetchResult(status is >= 200 and < 300, status, current.ToString(), res.Content.Headers.ContentType?.MediaType,
                    enc.GetString(buffer.GetBuffer(), 0, (int)buffer.Length), total, (int)sw.ElapsedMilliseconds);
            }
            return Fail(current, sw, "redirects");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Fail(current, sw, "timeout"); }
        catch (HttpRequestException e) { return Fail(current, sw, e.Message.Contains("blocked") ? "blocked" : "unreachable"); }
    }

    private static FetchResult Fail(Uri u, Stopwatch sw, string error) => new(false, 0, u.ToString(), null, "", 0, (int)sw.ElapsedMilliseconds, error);
}
