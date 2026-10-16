using LiteNetLib;
using Madlands.Server.World;
using Madlands.Shared.Net;

namespace Madlands.Server.Net;

public enum SessionState
{
    AwaitingHello,
    /// <summary>Hello received; the session token and character are being loaded off-thread.</summary>
    Authenticating,
    InWorld,
}

/// <summary>One connected client. Lives from UDP connect to disconnect.</summary>
public sealed class ClientSession
{
    public ClientSession(NetPeer peer, long connectedAtMs)
    {
        Peer = peer;
        ConnectedAtMs = connectedAtMs;
    }

    public NetPeer Peer { get; }
    public int Id => Peer.Id;
    public long ConnectedAtMs { get; }
    public SessionState State { get; set; } = SessionState.AwaitingHello;
    public long AccountId { get; set; }
    public Player? Player { get; set; }

    public RateLimiter MoveLimiter { get; } = new(ratePerSecond: 40, burst: 80);
    public RateLimiter PingLimiter { get; } = new(ratePerSecond: 5, burst: 10);
    public int Violations { get; set; }

    public int MaxUnreliablePacketSize => Peer.GetMaxSinglePacketSize(DeliveryMethod.Unreliable);

    /// <summary>Encodes into <paramref name="writer"/> (owned by the calling thread) and sends.</summary>
    public void Send<T>(PacketWriter writer, T message, DeliveryMethod method) where T : IMessage
    {
        Packet.Encode(writer, message);
        Peer.Send(writer.AsSpan(), method);
    }

    public override string ToString() => Player is null ? $"session {Id} ({Peer})" : $"{Player.Name} (session {Id})";
}

/// <summary>Token bucket. Messages beyond the rate are dropped and counted as violations.</summary>
public sealed class RateLimiter
{
    private readonly float _ratePerSecond;
    private readonly float _burst;
    private float _tokens;
    private long _lastMs = -1;

    public RateLimiter(float ratePerSecond, float burst)
    {
        _ratePerSecond = ratePerSecond;
        _burst = burst;
        _tokens = burst;
    }

    public bool TryConsume(long nowMs)
    {
        Refill(nowMs);
        if (_tokens < 1f) return false;
        _tokens -= 1f;
        return true;
    }

    /// <summary>True if a token is available, without consuming it.</summary>
    public bool Peek(long nowMs)
    {
        Refill(nowMs);
        return _tokens >= 1f;
    }

    private void Refill(long nowMs)
    {
        if (_lastMs >= 0) _tokens = Math.Min(_burst, _tokens + (nowMs - _lastMs) * _ratePerSecond / 1000f);
        _lastMs = nowMs;
    }
}
