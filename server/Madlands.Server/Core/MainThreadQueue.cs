using System.Collections.Concurrent;
using Serilog;

namespace Madlands.Server.Core;

/// <summary>
/// Hands work from background tasks (database lookups) back to the game loop thread, which
/// drains the queue at the start of every tick. The only way async results touch game state.
/// </summary>
public sealed class MainThreadQueue
{
    private static readonly ILogger Log = Serilog.Log.ForContext<MainThreadQueue>();
    private readonly ConcurrentQueue<Action> _queue = new();

    public void Enqueue(Action action) => _queue.Enqueue(action);

    public void Drain()
    {
        while (_queue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Main-thread work item failed");
            }
        }
    }
}
