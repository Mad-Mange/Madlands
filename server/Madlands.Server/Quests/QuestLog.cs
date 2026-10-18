using Madlands.Content;
using Madlands.Shared;
using Madlands.Shared.Game;

namespace Madlands.Server.Quests;

/// <summary>One quest in a player's quest log.</summary>
public sealed class QuestProgress
{
    public QuestProgress(QuestDefinition quest)
    {
        Quest = quest;
        Counts = new int[quest.Objectives.Count];
    }

    public QuestDefinition Quest { get; }
    /// <summary>Per objective. Collect objectives mirror the bags (capped at the count needed).</summary>
    public int[] Counts { get; }

    public bool IsComplete
    {
        get
        {
            for (int i = 0; i < Counts.Length; i++)
                if (Counts[i] < Quest.Objectives[i].Count) return false;
            return true;
        }
    }

    public ushort[] WireProgress() => Counts.Select(c => (ushort)Math.Clamp(c, 0, ushort.MaxValue)).ToArray();
}

/// <summary>A player's active quests (in the order taken) and the quests they have turned in.</summary>
public sealed class QuestLog
{
    private readonly List<QuestProgress> _active = new();
    private readonly HashSet<uint> _rewarded = new();

    public IReadOnlyList<QuestProgress> Active => _active;
    public IReadOnlyCollection<uint> Rewarded => _rewarded;
    public bool IsFull => _active.Count >= GameConstants.MaxQuestLog;

    public QuestProgress? Find(uint questId)
    {
        foreach (var q in _active) if (q.Quest.Id == questId) return q;
        return null;
    }

    public bool IsRewarded(uint questId) => _rewarded.Contains(questId);

    public QuestProgress Add(QuestDefinition quest)
    {
        var progress = new QuestProgress(quest);
        _active.Add(progress);
        return progress;
    }

    public bool Remove(uint questId) => _active.RemoveAll(q => q.Quest.Id == questId) > 0;

    public void MarkRewarded(uint questId) => _rewarded.Add(questId);

    /// <summary>Forgets a turned-in quest (GM reset).</summary>
    public bool Unreward(uint questId) => _rewarded.Remove(questId);
}
