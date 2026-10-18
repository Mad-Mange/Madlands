using Madlands.Content;
using Madlands.Server.Combat;
using Madlands.Server.Items;
using Madlands.Server.World;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Net;

namespace Madlands.Server.Quests;

/// <summary>
/// NPC dialogs and quests for one map instance (ADR 0011): who offers what, taking quests, counting
/// kills, items, conversations and places, turning in for rewards, and the "!" and "?" marks each
/// player sees above NPCs. All rules run here; the client only shows what it is told.
/// </summary>
public sealed class QuestSystem
{
    public const long ExploreCheckIntervalMs = 1000;

    private readonly MapInstance _map;
    private readonly GossipMenu _gossipMsg = new();
    private readonly QuestLogUpdate _logMsg = new();
    private readonly QuestGiverStatusUpdate _statusMsg = new();
    private readonly QuestCompleted _completedMsg = new();
    private readonly List<QuestProgress> _changedScratch = new();

    public QuestSystem(MapInstance map)
    {
        _map = map;
    }

    private ContentDatabase Content => _map.Content;
    private ItemSystem Items => _map.Items;

    // ------------------------------------------------------------------ rules

    /// <summary>Why the player cannot take the quest now (None = they can).</summary>
    public ActionError CanTake(Player player, QuestDefinition quest)
    {
        var log = player.Quests;
        if (log.Find(quest.Id) is not null || log.IsRewarded(quest.Id)) return ActionError.QuestNotAvailable;
        if (player.Level < quest.MinLevel || !quest.AllowsClass(player.ClassId)) return ActionError.QuestNotAvailable;
        foreach (var required in quest.Requires)
            if (!log.IsRewarded(required)) return ActionError.QuestNotAvailable;
        return log.IsFull ? ActionError.QuestLogFull : ActionError.None;
    }

    /// <summary>Shown as available ("!"): takeable, or only blocked by a full quest log.</summary>
    private bool IsOffered(Player player, QuestDefinition quest) => CanTake(player, quest) is ActionError.None or ActionError.QuestLogFull;

    public QuestGiverStatus StatusFor(Player player, Creature npc)
    {
        var status = QuestGiverStatus.None;
        foreach (var quest in Content.QuestsEndedBy(npc.Template.Id))
        {
            if (player.Quests.Find(quest.Id) is not { } progress) continue;
            if (progress.IsComplete) return QuestGiverStatus.Reward;
            status = QuestGiverStatus.Incomplete;
        }
        foreach (var quest in Content.QuestsGivenBy(npc.Template.Id))
            if (IsOffered(player, quest)) return QuestGiverStatus.Available;
        return status;
    }

    /// <summary>The player still needs this item for a collect objective (quest items only drop then).</summary>
    public bool NeedsItem(Player player, uint itemId)
    {
        foreach (var progress in player.Quests.Active)
        {
            var objectives = progress.Quest.Objectives;
            for (int i = 0; i < objectives.Count; i++)
                if (objectives[i].Type == ObjectiveType.Collect && objectives[i].Item == itemId && player.Inventory.Count(itemId) < objectives[i].Count)
                    return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ talking

    /// <summary>Right-click on an NPC: counts as talking to it, then opens its dialog or its wares.</summary>
    public void Interact(Player player, EntityGuid npcGuid, InteractOption option)
    {
        var npc = _map.FindInteractable(player, npcGuid, out var error);
        if (npc is not null && npc.NpcFlags == NpcFlags.None) error = ActionError.CantInteract;
        if (error != ActionError.None || npc is null)
        {
            Items.Fail(player, error);
            return;
        }
        OnTalk(player, npc);

        bool vendor = (npc.NpcFlags & NpcFlags.Vendor) != 0;
        if (option == InteractOption.Vendor)
        {
            if (vendor) Items.SendVendor(player, npc);
            else Items.Fail(player, ActionError.CantInteract);
            return;
        }
        FillGossip(player, npc);
        // Nothing to say and no quests: a vendor just opens its wares.
        if (vendor && _gossipMsg.Quests.Count == 0 && _gossipMsg.Text.Length == 0) Items.SendVendor(player, npc);
        else _map.Send(player, _gossipMsg);
    }

    private void FillGossip(Player player, Creature npc)
    {
        _gossipMsg.Npc = npc.Guid;
        _gossipMsg.Text = npc.Template.Gossip;
        _gossipMsg.Flags = npc.NpcFlags;
        _gossipMsg.Quests.Clear();
        foreach (var quest in Content.QuestsEndedBy(npc.Template.Id))
        {
            if (player.Quests.Find(quest.Id) is not { } progress) continue;
            var status = progress.IsComplete ? QuestGiverStatus.Reward : QuestGiverStatus.Incomplete;
            _gossipMsg.Quests.Add(new GossipQuest { QuestId = quest.Id, Status = status });
        }
        foreach (var quest in Content.QuestsGivenBy(npc.Template.Id))
        {
            if (_gossipMsg.Quests.Count >= GossipMenu.MaxQuests) break;
            if (IsOffered(player, quest)) _gossipMsg.Quests.Add(new GossipQuest { QuestId = quest.Id, Status = QuestGiverStatus.Available });
        }
    }

    // ------------------------------------------------------------------ taking, turning in, abandoning

    public void Accept(Player player, EntityGuid npcGuid, uint questId)
    {
        var npc = _map.FindInteractable(player, npcGuid, out var error);
        if (npc is null)
        {
            Items.Fail(player, error);
            return;
        }
        if (!Content.Quests.TryGetValue(questId, out var quest) || quest.Giver != npc.Template.Id)
        {
            Items.Fail(player, ActionError.QuestNotAvailable);
            return;
        }
        error = CanTake(player, quest);
        if (error != ActionError.None)
        {
            Items.Fail(player, error);
            return;
        }
        var progress = player.Quests.Add(quest);
        UpdateCollect(player, progress);
        SendProgress(player, progress);
        RefreshStatuses(player);
    }

    public void TurnIn(Player player, EntityGuid npcGuid, uint questId, byte choice, long now)
    {
        var npc = _map.FindInteractable(player, npcGuid, out var error);
        if (npc is null)
        {
            Items.Fail(player, error);
            return;
        }
        if (!Content.Quests.TryGetValue(questId, out var quest) || quest.Ender != npc.Template.Id || player.Quests.Find(questId) is not { } progress)
        {
            Items.Fail(player, ActionError.QuestNotAvailable);
            return;
        }
        UpdateCollect(player, progress);
        if (!progress.IsComplete)
        {
            Items.Fail(player, ActionError.QuestNotComplete);
            return;
        }

        var rewards = quest.Rewards;
        var items = new List<(ItemDefinition Item, int Count)>();
        foreach (var r in rewards.Items)
            if (Content.Items.TryGetValue(r.Item, out var item)) items.Add((item, r.Count));
        if (rewards.Choices.Count > 0)
        {
            if (choice >= rewards.Choices.Count || !Content.Items.TryGetValue(rewards.Choices[choice].Item, out var chosen))
            {
                Items.Fail(player, ActionError.ChooseReward);
                return;
            }
            items.Add((chosen, rewards.Choices[choice].Count));
        }

        // Hand in the collected items first (that may free a slot), but put them back if the rewards still do not fit.
        var taken = new List<(ItemDefinition Item, int Count)>();
        foreach (var o in quest.Objectives.Where(o => o.Type == ObjectiveType.Collect))
            if (Content.Items.TryGetValue(o.Item, out var item)) taken.Add((item, player.Inventory.Remove(o.Item, o.Count)));
        if (!player.Inventory.CanAddAll(items))
        {
            foreach (var (item, count) in taken) player.Inventory.Add(item, count);
            Items.Flush(player);
            Items.Fail(player, ActionError.NoRoomForReward);
            return;
        }

        player.Quests.Remove(questId);
        player.Quests.MarkRewarded(questId);
        SendRemoved(player, questId);
        int xp = Experience.ForQuest(player.Level, quest.Level, rewards.Xp);
        _completedMsg.QuestId = questId;
        _completedMsg.Xp = player.Level >= Content.Levels.MaxLevel ? 0 : xp;
        _completedMsg.Money = rewards.Money;
        _map.Send(player, _completedMsg);

        foreach (var (item, count) in items) Items.Give(player, item, count, ReceiveSource.Quest);
        if (rewards.Money > 0) Items.GiveMoney(player, rewards.Money, ReceiveSource.Quest);
        Items.Flush(player);
        _map.Combat.GiveXp(player, xp, EntityGuid.Empty, now);
        RefreshStatuses(player);
    }

    public void Abandon(Player player, uint questId)
    {
        if (player.Quests.Find(questId) is not { } progress) return;
        player.Quests.Remove(questId);
        // Quest-only items of the quest go with it, unless another quest still wants them.
        foreach (var o in progress.Quest.Objectives.Where(o => o.Type == ObjectiveType.Collect))
        {
            if (!Content.Items.TryGetValue(o.Item, out var item) || item.Class != ItemClass.Quest || NeedsItem(player, o.Item)) continue;
            player.Inventory.Remove(o.Item, player.Inventory.Count(o.Item));
        }
        SendRemoved(player, questId);
        Items.Flush(player);
        RefreshStatuses(player);
    }

    // ------------------------------------------------------------------ progress

    public void OnKill(Player player, Creature victim) => Count(player, ObjectiveType.Kill, victim.Template.Id);

    public void OnTalk(Player player, Creature npc) => Count(player, ObjectiveType.Talk, npc.Template.Id);

    /// <summary>The bags changed: collect objectives follow the item counts.</summary>
    public void OnItemsChanged(Player player)
    {
        _changedScratch.Clear();
        foreach (var progress in player.Quests.Active)
            if (UpdateCollect(player, progress)) _changedScratch.Add(progress);
        Report(player);
    }

    /// <summary>Checks the player's explore objectives (throttled).</summary>
    public void UpdateExplore(Player player, long now)
    {
        if (now < player.NextExploreCheckMs || player.Quests.Active.Count == 0) return;
        player.NextExploreCheckMs = now + ExploreCheckIntervalMs;
        _changedScratch.Clear();
        foreach (var progress in player.Quests.Active)
        {
            var objectives = progress.Quest.Objectives;
            for (int i = 0; i < objectives.Count; i++)
            {
                var o = objectives[i];
                if (o.Type != ObjectiveType.Explore || progress.Counts[i] >= o.Count || o.Map != _map.MapId) continue;
                float dx = player.Position.X - o.X, dz = player.Position.Z - o.Z;
                if (dx * dx + dz * dz > o.Radius * o.Radius) continue;
                progress.Counts[i] = o.Count;
                if (!_changedScratch.Contains(progress)) _changedScratch.Add(progress);
            }
        }
        Report(player);
    }

    private void Count(Player player, ObjectiveType type, int creatureId)
    {
        _changedScratch.Clear();
        foreach (var progress in player.Quests.Active)
        {
            var objectives = progress.Quest.Objectives;
            for (int i = 0; i < objectives.Count; i++)
            {
                if (objectives[i].Type != type || objectives[i].Creature != creatureId || progress.Counts[i] >= objectives[i].Count) continue;
                progress.Counts[i]++;
                if (!_changedScratch.Contains(progress)) _changedScratch.Add(progress);
            }
        }
        Report(player);
    }

    /// <summary>Sends the quests in the scratch list; recomputes the NPC marks if one became complete (or no longer is).</summary>
    private void Report(Player player)
    {
        if (_changedScratch.Count == 0) return;
        _logMsg.Full = false;
        _logMsg.Entries.Clear();
        _logMsg.Completed.Clear();
        foreach (var progress in _changedScratch) _logMsg.Entries.Add(Entry(progress));
        _map.Send(player, _logMsg);
        RefreshStatuses(player);
    }

    private bool UpdateCollect(Player player, QuestProgress progress)
    {
        bool changed = false;
        var objectives = progress.Quest.Objectives;
        for (int i = 0; i < objectives.Count; i++)
        {
            if (objectives[i].Type != ObjectiveType.Collect) continue;
            int count = Math.Min(player.Inventory.Count(objectives[i].Item), objectives[i].Count);
            if (progress.Counts[i] == count) continue;
            progress.Counts[i] = count;
            changed = true;
        }
        return changed;
    }

    // ------------------------------------------------------------------ sending

    /// <summary>The whole quest log and every quest turned in (entering the world).</summary>
    public void SendLog(Player player)
    {
        foreach (var progress in player.Quests.Active) UpdateCollect(player, progress);
        _logMsg.Full = true;
        _logMsg.Entries.Clear();
        _logMsg.Completed.Clear();
        foreach (var progress in player.Quests.Active) _logMsg.Entries.Add(Entry(progress));
        _logMsg.Completed.AddRange(player.Quests.Rewarded.OrderBy(id => id));
        _map.Send(player, _logMsg);
    }

    private void SendProgress(Player player, QuestProgress progress)
    {
        _logMsg.Full = false;
        _logMsg.Entries.Clear();
        _logMsg.Completed.Clear();
        _logMsg.Entries.Add(Entry(progress));
        _map.Send(player, _logMsg);
    }

    private void SendRemoved(Player player, uint questId)
    {
        _logMsg.Full = false;
        _logMsg.Entries.Clear();
        _logMsg.Completed.Clear();
        _logMsg.Entries.Add(new QuestLogEntry { QuestId = questId, State = QuestLogState.Removed, Progress = Array.Empty<ushort>() });
        _map.Send(player, _logMsg);
    }

    private static QuestLogEntry Entry(QuestProgress progress) => new()
    {
        QuestId = progress.Quest.Id,
        State = progress.IsComplete ? QuestLogState.Complete : QuestLogState.Active,
        Progress = progress.WireProgress(),
    };

    /// <summary>Resends the marks of every quest NPC the player can see (after anything that may change them).</summary>
    public void RefreshStatuses(Player player)
    {
        _statusMsg.Entries.Clear();
        foreach (var entity in player.Known.Values)
        {
            if (entity is not Creature { NpcFlags: var flags } npc || (flags & NpcFlags.QuestGiver) == 0) continue;
            if (_statusMsg.Entries.Count == QuestGiverStatusUpdate.MaxEntries) break;
            _statusMsg.Entries.Add(new QuestGiverEntry { Npc = npc.Guid, Status = StatusFor(player, npc) });
        }
        if (_statusMsg.Entries.Count > 0) _map.Send(player, _statusMsg);
    }

    /// <summary>A quest NPC came into view.</summary>
    public void SendStatus(Player player, Creature npc)
    {
        _statusMsg.Entries.Clear();
        _statusMsg.Entries.Add(new QuestGiverEntry { Npc = npc.Guid, Status = StatusFor(player, npc) });
        _map.Send(player, _statusMsg);
    }
}
