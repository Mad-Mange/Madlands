using System.Threading.Channels;
using Serilog;

namespace Madlands.Server.Persistence;

/// <summary>
/// Write-behind persistence of character state (ADR 0007). The game loop only enqueues snapshots;
/// a background worker writes them, coalescing multiple snapshots of the same character so only
/// the newest is written. The game tick never waits for the database.
/// </summary>
public sealed class CharacterSaver
{
    private static readonly ILogger Log = Serilog.Log.ForContext<CharacterSaver>();

    private readonly IGameDatabase _database;
    private readonly Channel<CharacterSnapshot> _channel =
        Channel.CreateUnbounded<CharacterSnapshot>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _worker;
    private long _saved;
    private long _failed;

    public CharacterSaver(IGameDatabase database)
    {
        _database = database;
        _worker = Task.Run(RunAsync);
    }

    public long SavedCount => Interlocked.Read(ref _saved);
    public long FailedCount => Interlocked.Read(ref _failed);

    public void Enqueue(CharacterSnapshot snapshot) => _channel.Writer.TryWrite(snapshot);

    /// <summary>Stops accepting snapshots and waits until everything queued has been written.</summary>
    public async Task FlushAndStopAsync()
    {
        _channel.Writer.TryComplete();
        await _worker;
    }

    private async Task RunAsync()
    {
        var pending = new Dictionary<long, CharacterSnapshot>();
        while (await _channel.Reader.WaitToReadAsync())
        {
            while (_channel.Reader.TryRead(out var snapshot)) pending[snapshot.CharacterId] = snapshot;
            foreach (var snapshot in pending.Values)
            {
                try
                {
                    await _database.SaveCharacterAsync(snapshot);
                    Interlocked.Increment(ref _saved);
                }
                catch (Exception ex)
                {
                    // The next periodic save (or logout) retries with fresher data.
                    Interlocked.Increment(ref _failed);
                    Log.Error(ex, "Saving character {CharacterId} failed", snapshot.CharacterId);
                }
            }
            pending.Clear();
        }
    }
}
