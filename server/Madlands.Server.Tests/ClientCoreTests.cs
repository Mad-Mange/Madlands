using Madlands.ClientCore;
using Madlands.Shared;
using Madlands.Shared.Movement;

namespace Madlands.Server.Tests;

public class InterpolationBufferTests
{
    private static MovementState At(float z, float speed = 0) =>
        new(new Vec3(0, 0, z), 0, new Vec3(0, 0, speed), speed > 0 ? MovementFlags.Forward : MovementFlags.None);

    [Fact]
    public void Interpolates_between_samples()
    {
        var buffer = new InterpolationBuffer();
        buffer.Add(1000, At(0));
        buffer.Add(1200, At(10));
        Assert.Equal(5f, buffer.Sample(1100).Position.Z, 3);
        Assert.False(buffer.Sample(1100).Extrapolated);
    }

    [Fact]
    public void Holds_oldest_before_first_sample()
    {
        var buffer = new InterpolationBuffer();
        buffer.Add(1000, At(3));
        buffer.Add(1100, At(4));
        Assert.Equal(3f, buffer.Sample(500).Position.Z);
    }

    [Fact]
    public void Dead_reckons_past_newest_and_caps_it()
    {
        var buffer = new InterpolationBuffer();
        buffer.Add(1000, At(0, speed: 7));
        var r = buffer.Sample(1500);
        Assert.True(r.Extrapolated);
        Assert.Equal(3.5f, r.Position.Z, 3);
        // capped at MaxExtrapolationMs (1 s => 7 m)
        Assert.Equal(7f, buffer.Sample(5000).Position.Z, 3);
    }

    [Fact]
    public void Ignores_out_of_order_and_duplicate_samples()
    {
        var buffer = new InterpolationBuffer();
        Assert.True(buffer.Add(1000, At(0)));
        Assert.False(buffer.Add(1000, At(5)));
        Assert.False(buffer.Add(900, At(5)));
        Assert.Equal(1, buffer.Count);
    }

    [Fact]
    public void Ring_buffer_keeps_newest_samples()
    {
        var buffer = new InterpolationBuffer();
        for (int i = 0; i < 40; i++) buffer.Add(i * 100, At(i));
        Assert.Equal(16, buffer.Count);
        Assert.Equal(3900, buffer.NewestTime);
        Assert.Equal(38.5f, buffer.Sample(3850).Position.Z, 3);
    }
}

public class ClockSyncTests
{
    [Fact]
    public void Estimates_offset_from_round_trips()
    {
        var clock = new ClockSync();
        // Server is 5000 ms ahead; one-way delay 20 ms.
        clock.AddSample(sentLocalMs: 100, receivedLocalMs: 140, serverTimeMs: 5120);
        Assert.Equal(5000, clock.OffsetMs, 1);
        Assert.Equal(5140, clock.ServerTimeAt(140), 1);
    }

    [Fact]
    public void Prefers_lowest_rtt_sample_and_slews_gradually()
    {
        var clock = new ClockSync();
        clock.AddSample(0, 40, 5020);    // rtt 40, offset 5000
        clock.AddSample(100, 400, 5300); // rtt 300 (queued), offset 5050 -> ignored, best is still rtt 40
        Assert.Equal(5000, clock.OffsetMs, 1);

        clock.AddSample(500, 510, 5535); // rtt 10, offset 5030 -> slew at most 10 ms per sample
        Assert.Equal(5010, clock.OffsetMs, 1);
    }
}

public class MovementSenderTests
{
    private static MovementState Idle => MovementState.Idle(Vec3.Zero, 0);

    private static MovementState Running(float z) =>
        new(new Vec3(0, 0, z), 0, new Vec3(0, 0, GameConstants.RunSpeed), MovementFlags.Forward);

    [Fact]
    public void Sends_first_state_and_flag_changes_immediately()
    {
        var sender = new MovementSender();
        Assert.True(sender.ShouldSend(Idle, 0));
        sender.MarkSent(Idle, 0);
        Assert.False(sender.ShouldSend(Idle, 10));
        Assert.True(sender.ShouldSend(Running(0), 10));
    }

    [Fact]
    public void Heartbeats_while_moving_but_not_when_idle()
    {
        var sender = new MovementSender();
        sender.MarkSent(Running(0), 0);
        // On the dead-reckoned path: nothing to send until the heartbeat.
        Assert.False(sender.ShouldSend(Running(GameConstants.RunSpeed * 0.3f), 300));
        Assert.True(sender.ShouldSend(Running(GameConstants.RunSpeed * 0.5f), MovementSender.HeartbeatMs));

        sender.MarkSent(Idle, 0);
        Assert.False(sender.ShouldSend(Idle, 5000));
    }

    [Fact]
    public void Sends_when_position_drifts_from_prediction()
    {
        var sender = new MovementSender();
        sender.MarkSent(Running(0), 0);
        var blocked = Running(0); // e.g. stuck against a wall while still pressing forward
        Assert.True(sender.ShouldSend(blocked, 200));
    }

    [Fact]
    public void Throttles_mouse_turning()
    {
        var sender = new MovementSender();
        sender.MarkSent(Idle, 0);
        var turned = MovementState.Idle(Vec3.Zero, 0.5f);
        Assert.False(sender.ShouldSend(turned, 50));
        Assert.True(sender.ShouldSend(turned, MovementSender.MinIntervalMs));
    }
}
