using Madlands.Server.Persistence;
using Madlands.Shared.Net;

namespace Madlands.Server.Tests;

/// <summary>What a public deployment relies on: no plain-HTTP passwords from the internet, client addresses behind a proxy, session cleanup.</summary>
public class DeploymentTests
{
    [Fact]
    public void A_public_realm_refuses_plain_http_from_the_internet()
    {
        var problem = Assert.Throws<ArgumentException>(() => ServerOptions.Parse(new[] { "--public-host", "realm.example.com" }));
        Assert.Contains("TLS reverse proxy", problem.Message);
        Assert.Throws<ArgumentException>(() => ServerOptions.Parse(new[] { "--public-host", "203.0.113.7", "--bind", "0.0.0.0", "--trust-proxy" }));

        // Fine: the API behind a proxy on this machine, a LAN or local realm, or an explicit opt-out.
        Assert.Null(ServerOptions.Parse(new[] { "--public-host", "realm.example.com", "--api-bind", "127.0.0.1", "--trust-proxy" }).PlainHttpProblem());
        Assert.Null(ServerOptions.Parse(new[] { "--public-host", "192.168.1.20" }).PlainHttpProblem());
        Assert.Null(ServerOptions.Parse(Array.Empty<string>()).PlainHttpProblem());
        Assert.Null(ServerOptions.Parse(new[] { "--public-host", "realm.example.com", "--allow-plain-http" }).PlainHttpProblem());
        Assert.Equal("127.0.0.1", ServerOptions.Parse(new[] { "--bind", "0.0.0.0", "--api-bind", "127.0.0.1" }).ApiAddress);
        Assert.Equal("10.0.0.5", ServerOptions.Parse(new[] { "--bind", "10.0.0.5" }).ApiAddress);
    }

    [Fact]
    public void Private_hosts_are_recognized()
    {
        foreach (var host in new[] { "127.0.0.1", "localhost", "10.1.2.3", "172.16.0.1", "172.31.255.1", "192.168.0.9", "::1" })
            Assert.True(ServerOptions.IsPrivateHost(host), host);
        foreach (var host in new[] { "8.8.8.8", "172.32.0.1", "realm.example.com", "0.0.0.0" })
            Assert.False(ServerOptions.IsPrivateHost(host), host);
    }

    [Fact]
    public Task Expired_sessions_are_deleted_in_memory() => SessionCleanupChecks.Run(new InMemoryGameDatabase());

    [Fact]
    public async Task Behind_a_proxy_the_rate_limit_counts_forwarded_addresses()
    {
        await using var realm = await TestRealm.StartAsync(configure: o =>
        {
            o.TrustProxy = true;
            o.ApiRateLimitPerMinute = 3;
        });
        using var http = new HttpClient();
        async Task<ApiResultCode> Post(string forwardedFor)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, realm.Host.HttpBaseUrl + "/api/v1/login") { Content = new ByteArrayContent(Array.Empty<byte>()) };
            request.Headers.Add("X-Forwarded-For", forwardedFor);
            using var response = await http.SendAsync(request);
            var body = await response.Content.ReadAsByteArrayAsync();
            var reader = new PacketReader(body);
            Assert.Equal(Opcode.ApiResult, Packet.ReadOpcode(ref reader));
            var result = new ApiResult();
            result.Read(ref reader);
            return result.Result;
        }

        // One address runs out of its burst (at least 10 requests)...
        int allowed = 0;
        while (await Post("198.51.100.1") != ApiResultCode.RateLimited && allowed < 50) allowed++;
        Assert.InRange(allowed, 10, 12);
        Assert.NotEqual(ApiResultCode.RateLimited, await Post("198.51.100.2")); // another player behind the same proxy
    }
}

internal static class SessionCleanupChecks
{
    public static async Task Run(IGameDatabase db)
    {
        long account = (await db.CreateAccountAsync("sleepy", "h"))!.Value;
        var now = DateTime.UtcNow;
        await db.CreateSessionAsync(new byte[] { 7 }, account, now.AddHours(-2));
        await db.CreateSessionAsync(new byte[] { 8 }, account, now.AddMinutes(-1));
        await db.CreateSessionAsync(new byte[] { 9 }, account, now.AddHours(5));

        Assert.Equal(2, await db.DeleteExpiredSessionsAsync(now));
        Assert.Equal(account, await db.FindSessionAccountAsync(new byte[] { 9 }, now));
        Assert.Equal(0, await db.DeleteExpiredSessionsAsync(now));
    }
}
