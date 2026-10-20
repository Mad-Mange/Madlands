using System.Collections.Generic;
using System.Globalization;
using Madlands.ClientCore;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Net;
using UnityEngine.UIElements;

namespace Madlands.Client
{
    /// <summary>
    /// NPC dialogs and quests (ADR 0011): the NPC's greeting with its quests ("!" to take, "?" to turn
    /// in), a quest's story with Accept, its progress text, and the reward screen with a choice. Plus
    /// the quest log (L) and the objective tracker on the right. Quest texts come from GameData; the
    /// server decides what is offered and whether a turn-in counts.
    /// </summary>
    public sealed class QuestUi
    {
        private const float CloseDistance = GameConstants.InteractRange + 3f;

        private readonly GameClient _client;
        private readonly ItemTooltip _tooltip;
        private readonly GameWindow _dialog;
        private readonly GameWindow _log;
        private readonly VisualElement _logList;
        private readonly ScrollView _logDetails;
        private readonly VisualElement _tracker;
        private EntityGuid _npc;
        private uint _selectedQuest;
        private int _choice = -1;
        private readonly List<VisualElement> _choiceRows = new List<VisualElement>();

        public QuestUi(VisualElement layer, GameClient client, ItemTooltip tooltip)
        {
            _client = client;
            _tooltip = tooltip;
            _dialog = new GameWindow(layer, "", "dialog");
            _dialog.Closed += () =>
            {
                _npc = EntityGuid.Empty;
                _tooltip.Hide();
            };

            _log = new GameWindow(layer, Loc.T("quest.window"), "questlog");
            var body = new VisualElement { pickingMode = PickingMode.Ignore };
            body.AddToClassList("questlog-body");
            _logList = new ScrollView { pickingMode = PickingMode.Position };
            _logList.AddToClassList("questlog-list");
            _logDetails = new ScrollView { pickingMode = PickingMode.Position };
            _logDetails.AddToClassList("questlog-details");
            body.Add(_logList);
            body.Add(_logDetails);
            _log.Body.Add(body);
            _log.AddFooterButton(Loc.T("quest.abandon"), () =>
            {
                if (_selectedQuest != 0) _client.AbandonQuest(_selectedQuest);
            }, "danger");
            _log.Closed += () => _tooltip.Hide();

            _tracker = new VisualElement { pickingMode = PickingMode.Ignore };
            _tracker.AddToClassList("quest-tracker");
            layer.Add(_tracker);
        }

        public bool DialogOpen => _dialog.IsOpen;
        public bool LogOpen => _log.IsOpen;
        /// <summary>The NPC the dialog belongs to (Empty when closed).</summary>
        public EntityGuid Npc => _dialog.IsOpen ? _npc : EntityGuid.Empty;

        public void ToggleLog()
        {
            _log.Toggle();
            RefreshLog();
        }

        public void CloseDialog() => _dialog.Close();

        public bool HandleEscape()
        {
            if (_dialog.IsOpen)
            {
                _dialog.Close();
                return true;
            }
            if (_log.IsOpen)
            {
                _log.Close();
                return true;
            }
            return false;
        }

        public void Update()
        {
            if (!_dialog.IsOpen) return;
            if (!_client.TryGetPosition(_npc, out var p) || Vec3.Distance(p, _client.LocalMovement.Position) > CloseDistance) _dialog.Close();
        }

        // ------------------------------------------------------------------ the NPC dialog

        /// <summary>The server's answer to talking to an NPC.</summary>
        public void ShowGossip(GossipMenu menu)
        {
            _npc = menu.Npc;
            // A single quest and nothing else to say: go straight to it, like classic quest givers.
            if (menu.Quests.Count == 1 && string.IsNullOrEmpty(menu.Text) && (menu.Flags & NpcFlags.Vendor) == 0)
            {
                ShowQuest(menu.Quests[0].QuestId, menu.Quests[0].Status);
                return;
            }
            Begin(NpcName(menu.Npc));
            if (!string.IsNullOrEmpty(menu.Text)) GameWindow.Text(_dialog.Body, menu.Text, "window-text");
            if (menu.Quests.Count > 0) GameWindow.Text(_dialog.Body, Loc.T("quest.quests"), "section-title");
            foreach (var q in menu.Quests)
            {
                var info = _client.Data.GetQuest(q.QuestId);
                if (info == null) continue;
                uint id = q.QuestId;
                var status = q.Status;
                var option = Option(Mark(status), info.Title, () => ShowQuest(id, status));
                option.EnableInClassList("incomplete", status == QuestGiverStatus.Incomplete);
            }
            if ((menu.Flags & NpcFlags.Vendor) != 0)
                Option("$", Loc.T("quest.browse"), () => _client.Interact(_npc, InteractOption.Vendor));
            _dialog.AddFooterButton(Loc.T("quest.goodbye"), _dialog.Close);
            _dialog.Open();
        }

        /// <summary>Shows one of the open dialog's quests by id (scripted screenshot runs).</summary>
        public void ShowQuest(uint questId)
        {
            foreach (var q in _client.Gossip.Quests)
                if (q.QuestId == questId) ShowQuest(questId, q.Status);
        }

        private void ShowQuest(uint questId, QuestGiverStatus status)
        {
            var info = _client.Data.GetQuest(questId);
            if (info == null) return;
            switch (status)
            {
                case QuestGiverStatus.Available: ShowDetails(info); break;
                case QuestGiverStatus.Incomplete: ShowProgress(info); break;
                case QuestGiverStatus.Reward: ShowReward(info); break;
            }
        }

        private void ShowDetails(QuestInfo quest)
        {
            Begin(NpcName(_npc));
            GameWindow.Text(_dialog.Body, quest.Title, "quest-title");
            GameWindow.Text(_dialog.Body, quest.Details, "window-text");
            GameWindow.Text(_dialog.Body, Loc.T("quest.objectivesTitle"), "section-title");
            GameWindow.Text(_dialog.Body, quest.Objectives, "window-text");
            AddRewards(_dialog.Body, quest, selectable: false);
            uint id = quest.Id;
            _dialog.AddFooterButton(Loc.T("quest.accept"), () =>
            {
                _client.AcceptQuest(_npc, id);
                _dialog.Close();
            }, "primary");
            _dialog.AddFooterButton(Loc.T("quest.decline"), _dialog.Close);
            _dialog.Open();
        }

        private void ShowProgress(QuestInfo quest)
        {
            Begin(NpcName(_npc));
            GameWindow.Text(_dialog.Body, quest.Title, "quest-title");
            GameWindow.Text(_dialog.Body, string.IsNullOrEmpty(quest.Progress) ? Loc.T("quest.isItDone") : quest.Progress, "window-text");
            AddObjectives(_dialog.Body, quest, _client.Quests.Find(quest.Id));
            var complete = _dialog.AddFooterButton(Loc.T("quest.complete"), () => { }, "primary");
            complete.SetEnabled(false);
            _dialog.AddFooterButton(Loc.T("quest.goodbye"), _dialog.Close);
            _dialog.Open();
        }

        private void ShowReward(QuestInfo quest)
        {
            Begin(NpcName(_npc));
            GameWindow.Text(_dialog.Body, quest.Title, "quest-title");
            GameWindow.Text(_dialog.Body, quest.Completion, "window-text");
            AddRewards(_dialog.Body, quest, selectable: true);
            uint id = quest.Id;
            _dialog.AddFooterButton(Loc.T("quest.complete"), () =>
            {
                if (quest.RewardChoices.Count > 0 && _choice < 0)
                {
                    _tooltip.ShowText(Loc.T("quest.chooseReward"), Loc.T("quest.chooseRewardHint"), _dialog.Footer);
                    return;
                }
                _client.TurnInQuest(_npc, id, quest.RewardChoices.Count > 0 ? (byte)_choice : QuestTurnIn.NoChoice);
                _dialog.Close();
            }, "primary");
            _dialog.Open();
        }

        private void Begin(string title)
        {
            _tooltip.Hide();
            _dialog.Title = title;
            _dialog.Body.Clear();
            _dialog.Footer.Clear();
            _dialog.Footer.style.display = DisplayStyle.None;
            _choice = -1;
            _choiceRows.Clear();
        }

        private VisualElement Option(string mark, string text, System.Action onClick)
        {
            var row = new VisualElement { pickingMode = PickingMode.Position };
            row.AddToClassList("quest-option");
            var markLabel = GameWindow.Text(row, mark, "quest-mark");
            GameWindow.Text(row, text, "quest-option-name");
            row.RegisterCallback<ClickEvent>(_ => onClick());
            _dialog.Body.Add(row);
            return markLabel;
        }

        private static string Mark(QuestGiverStatus status) => status == QuestGiverStatus.Available ? "!" : "?";

        private string NpcName(EntityGuid npc) => _client.World.TryGet(npc, out var e) ? e.Name : "";

        // ------------------------------------------------------------------ shared pieces

        private void AddObjectives(VisualElement parent, QuestInfo quest, ClientQuest progress)
        {
            if (quest.ObjectiveList.Count == 0) return;
            GameWindow.Text(parent, Loc.T("quest.objectives"), "section-title");
            for (int i = 0; i < quest.ObjectiveList.Count; i++)
            {
                var o = quest.ObjectiveList[i];
                int done = progress != null && i < progress.Progress.Length ? progress.Progress[i] : 0;
                var line = GameWindow.Text(parent, ObjectiveLine(o, done), "objective-line");
                line.EnableInClassList("done", done >= o.Count);
            }
        }

        public static string ObjectiveLine(QuestObjectiveInfo o, int done) =>
            o.Count > 1 ? o.Text + ": " + done + "/" + o.Count : o.Text + (done >= o.Count ? Loc.T("quest.done") : "");

        private void AddRewards(VisualElement parent, QuestInfo quest, bool selectable)
        {
            if (quest.RewardItems.Count == 0 && quest.RewardChoices.Count == 0 && quest.RewardMoney == 0 && quest.RewardXp == 0) return;
            GameWindow.Text(parent, Loc.T("quest.rewards"), "section-title");
            if (quest.RewardChoices.Count > 0)
            {
                GameWindow.Text(parent, Loc.T("quest.chooseOne"), "window-hint");
                var row = RewardRow(parent);
                for (int i = 0; i < quest.RewardChoices.Count; i++)
                {
                    int index = i;
                    var entry = RewardEntry(row, quest.RewardChoices[i]);
                    _choiceRows.Add(entry);
                    if (!selectable) continue;
                    entry.RegisterCallback<ClickEvent>(_ =>
                    {
                        _choice = index;
                        for (int c = 0; c < _choiceRows.Count; c++) _choiceRows[c].EnableInClassList("selected", c == index);
                    });
                }
            }
            if (quest.RewardItems.Count > 0)
            {
                GameWindow.Text(parent, Loc.T("quest.alsoReceive"), "window-hint");
                var row = RewardRow(parent);
                foreach (var reward in quest.RewardItems) RewardEntry(row, reward);
            }
            if (quest.RewardMoney > 0)
            {
                var money = MoneyView.Create(quest.RewardMoney);
                money.style.justifyContent = Justify.FlexStart;
                parent.Add(money);
            }
            if (quest.RewardXp > 0) GameWindow.Text(parent, quest.RewardXp.ToString(CultureInfo.InvariantCulture) + " experience", "window-hint");
        }

        private static VisualElement RewardRow(VisualElement parent)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("reward-row");
            parent.Add(row);
            return row;
        }

        private VisualElement RewardEntry(VisualElement parent, QuestItemReward reward)
        {
            var item = _client.Data.GetItem(reward.Item);
            var entry = new VisualElement { pickingMode = PickingMode.Position };
            entry.AddToClassList("list-row");
            var view = new ItemSlotView(null, "small");
            view.Set(item, reward.Count);
            view.Root.pickingMode = PickingMode.Ignore;
            entry.Add(view.Root);
            var name = GameWindow.Text(entry, item != null ? item.Name : "?", "list-name");
            if (item != null) ItemIcons.SetQuality(name, item.Quality, text: true);
            entry.RegisterCallback<PointerEnterEvent>(_ => _tooltip.Show(item, reward.Count, entry, _client));
            entry.RegisterCallback<PointerLeaveEvent>(_ => _tooltip.Hide());
            parent.Add(entry);
            return entry;
        }

        // ------------------------------------------------------------------ quest log and tracker

        /// <summary>Rebuilds the quest log (if open) and the tracker after a quest log change.</summary>
        public void RefreshLog()
        {
            RefreshTracker();
            if (!_log.IsOpen) return;
            var active = _client.Quests.Active;
            if (_client.Quests.Find(_selectedQuest) == null) _selectedQuest = active.Count > 0 ? active[0].QuestId : 0;
            _log.Title = Loc.T("quest.logTitle", active.Count, GameConstants.MaxQuestLog);
            _logList.Clear();
            foreach (var quest in active)
            {
                var info = _client.Data.GetQuest(quest.QuestId);
                if (info == null) continue;
                uint id = quest.QuestId;
                var row = new VisualElement { pickingMode = PickingMode.Position };
                row.AddToClassList("list-row");
                row.EnableInClassList("selected", id == _selectedQuest);
                var title = GameWindow.Text(row, "[" + info.Level + "] " + info.Title + (quest.IsComplete ? Loc.T("quest.completeTag") : ""), "list-name");
                title.style.color = LevelColor(info.Level);
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    _selectedQuest = id;
                    RefreshLog();
                });
                _logList.Add(row);
            }
            if (active.Count == 0) GameWindow.Text(_logList, Loc.T("quest.logEmpty"), "window-hint");

            _logDetails.Clear();
            var selected = _client.Data.GetQuest(_selectedQuest);
            if (selected == null) return;
            var progress = _client.Quests.Find(_selectedQuest);
            GameWindow.Text(_logDetails, selected.Title, "quest-title");
            GameWindow.Text(_logDetails, selected.Objectives, "window-text");
            AddObjectives(_logDetails, selected, progress);
            if (progress != null && progress.IsComplete && !string.IsNullOrEmpty(selected.EnderName))
                GameWindow.Text(_logDetails, Loc.T("quest.returnTo", selected.EnderName), "window-hint");
            GameWindow.Text(_logDetails, Loc.T("quest.description"), "section-title");
            GameWindow.Text(_logDetails, selected.Details, "window-text");
            AddRewards(_logDetails, selected, selectable: false);
        }

        private void RefreshTracker()
        {
            _tracker.Clear();
            _tracker.style.display = _client.Quests.Active.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var quest in _client.Quests.Active)
            {
                var info = _client.Data.GetQuest(quest.QuestId);
                if (info == null) continue;
                var title = GameWindow.Text(_tracker, info.Title, "tracker-title");
                title.style.color = LevelColor(info.Level);
                if (quest.IsComplete)
                {
                    GameWindow.Text(_tracker, info.EnderName.Length > 0 ? Loc.T("quest.readyToTurnInTo", info.EnderName) : Loc.T("quest.readyToTurnIn"), "tracker-line").AddToClassList("done");
                    continue;
                }
                for (int i = 0; i < info.ObjectiveList.Count; i++)
                {
                    var o = info.ObjectiveList[i];
                    int done = i < quest.Progress.Length ? quest.Progress[i] : 0;
                    var line = GameWindow.Text(_tracker, "- " + ObjectiveLine(o, done), "tracker-line");
                    line.EnableInClassList("done", done >= o.Count);
                }
                if (info.ObjectiveList.Count == 0 && info.EnderName.Length > 0) GameWindow.Text(_tracker, Loc.T("quest.speakWith", info.EnderName), "tracker-line");
            }
        }

        private StyleColor LevelColor(int questLevel) => new StyleColor(DifficultyColor(questLevel, _client.Self.Values.Level));

        /// <summary>A quest's color by its level against the player's: gray (trivial), green, yellow, orange, red.</summary>
        public static UnityEngine.Color DifficultyColor(int questLevel, int playerLevel)
        {
            int diff = questLevel - playerLevel;
            if (diff >= 5) return new UnityEngine.Color(1f, 0.25f, 0.2f);
            if (diff >= 3) return new UnityEngine.Color(1f, 0.55f, 0.2f);
            if (diff >= -2) return new UnityEngine.Color(1f, 0.85f, 0.3f);
            if (diff >= -5) return new UnityEngine.Color(0.35f, 0.9f, 0.35f);
            return new UnityEngine.Color(0.6f, 0.6f, 0.6f);
        }

        /// <summary>"[5]" in the quest's difficulty color (rich text), before its name or objectives on the maps.</summary>
        public static string LevelTag(int questLevel, int playerLevel) =>
            "<color=#" + UnityEngine.ColorUtility.ToHtmlStringRGB(DifficultyColor(questLevel, playerLevel)) + ">[" + questLevel + "]</color>";
    }
}
