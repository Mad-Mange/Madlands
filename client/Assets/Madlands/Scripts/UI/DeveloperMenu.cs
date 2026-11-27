using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Madlands.ClientCore;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace Madlands.Client
{
    /// <summary>
    /// The developer GM menu (docs/GMMenu): a left-docked panel for GM sessions. Every button runs a command
    /// through <see cref="GmCommandClient"/> (the same path automation uses); the server checks rights and
    /// arguments and answers with a result shown at the bottom. Toggles show the server's
    /// <see cref="GameClient.GmStatus"/> or the overlay's real state, never a value of their own. Only shown
    /// when the server reports GM rights; Esc opens and closes it (see <see cref="HudController.HandleEscape"/>).
    /// </summary>
    public sealed class DeveloperMenu
    {
        private const float RefreshSeconds = 0.2f;
        private const int HistoryRows = 6;
        private const int MaxChoices = 60;
        private const string EscapeSetting = "menu.escape";
        private const string CollapsedSetting = "menu.collapsed";

        /// <summary>A searchable selector over real game data: a filter field above a dropdown.</summary>
        private sealed class Picker
        {
            public TextField Search;
            public DropdownField Dropdown;
            public List<(string Id, string Label)> All = new List<(string, string)>();
            public readonly List<string> ShownIds = new List<string>();
            public Func<(string Id, string Label), bool> Filter;

            public string SelectedId => Dropdown.index >= 0 && Dropdown.index < ShownIds.Count ? ShownIds[Dropdown.index] : null;

            public void Apply()
            {
                string previous = SelectedId;
                string text = Search?.value?.Trim() ?? "";
                ShownIds.Clear();
                var labels = new List<string>();
                foreach (var entry in All)
                {
                    if (Filter != null && !Filter(entry)) continue;
                    if (text.Length > 0 && entry.Label.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0 && entry.Id != text) continue;
                    if (ShownIds.Count >= MaxChoices) break;
                    ShownIds.Add(entry.Id);
                    labels.Add(entry.Label);
                }
                Dropdown.choices = labels;
                int keep = previous != null ? ShownIds.IndexOf(previous) : -1;
                Dropdown.index = keep >= 0 ? keep : labels.Count > 0 ? 0 : -1;
                if (labels.Count == 0) Dropdown.SetValueWithoutNotify(Loc.T("gm.noMatch"));
            }
        }

        private sealed class ActionEntry
        {
            public Button Button;
            public string CommandId;
            public Func<string> Unavailable;
            public Label Hint;
        }

        private readonly GameClient _client;
        private readonly DebugGeometryView _geometry;
        private readonly VisualElement _root;
        private readonly Label _badge, _target, _feedback, _keyHint;
        private Label _inspect, _spawnCount;
        private readonly VisualElement _history;
        private readonly List<System.Action> _bindings = new List<System.Action>();
        private readonly List<ActionEntry> _actions = new List<ActionEntry>();
        private readonly HashSet<string> _collapsed;
        private Picker _destinations, _items, _creatures, _quests;
        private TextField _level, _itemCount, _gold, _xp, _x, _z, _radius, _spawnCountField;
        private SliderInt _speed, _damage;
        private Toggle _escapeToggle;
        private bool _friendlyTab;
        private bool _openWhenReady;
        private float _refreshAt;

        /// <summary>The header's "Game menu" button (the normal Esc menu stays one click away).</summary>
        public event System.Action GameMenuRequested;

        /// <summary>"Pick on map": the world map should open and report clicks to <see cref="SetCoordinates"/>.</summary>
        public event System.Action MapPickRequested;

        public bool IsOpen { get; private set; }

        /// <summary>The server reports GM rights for this session.</summary>
        public bool IsAvailable => _client.Gm.IsAvailable;

        /// <summary>
        /// Whether Esc (with nothing else to close) also opens this menu instead of the game menu. Off by default:
        /// the menu has its own key (<see cref="GameAction.ToggleDeveloperMenu"/>, § / `), Esc only closes it.
        /// </summary>
        public bool OpensOnEscape => Preferences.GetDeveloperSetting(EscapeSetting, "off") == "on";

        public DeveloperMenu(VisualElement layer, GameClient client, bool openWhenReady)
        {
            _client = client;
            _openWhenReady = openWhenReady;
            _geometry = new DebugGeometryView(client);
            _collapsed = new HashSet<string>((Preferences.GetDeveloperSetting(CollapsedSetting, "") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            RegisterLocalCommands();

            var sheet = Resources.Load<StyleSheet>("DeveloperMenu");
            if (sheet != null) layer.styleSheets.Add(sheet);

            _root = new VisualElement { name = "developer-menu", pickingMode = PickingMode.Position };
            _root.AddToClassList("dev-menu");
            _root.AddToClassList("hidden");
            layer.Add(_root);

            var stripe = new VisualElement { pickingMode = PickingMode.Ignore };
            stripe.AddToClassList("dev-stripe");
            stripe.style.backgroundImage = new StyleBackground(DeveloperMenuArt.HazardStripes());
            _root.Add(stripe);

            var frame = new VisualElement();
            frame.AddToClassList("dev-frame");
            frame.style.backgroundImage = new StyleBackground(DeveloperMenuArt.Metal());
            _root.Add(frame);

            // Header: brand, title, DEV/GM badge, game menu, Esc hint, close.
            var brand = new VisualElement();
            brand.AddToClassList("dev-brand");
            DeveloperMenuArt.AddRivets(brand);
            var logo = new Label(Loc.T("gm.brand"));
            logo.AddToClassList("dev-logo");
            brand.Add(logo);
            _badge = new Label();
            _badge.AddToClassList("dev-badge");
            brand.Add(_badge);
            frame.Add(brand);

            var titleBar = new VisualElement();
            titleBar.AddToClassList("dev-titlebar");
            var code = new Label("</>");
            code.AddToClassList("dev-code-icon");
            titleBar.Add(code);
            var title = new Label(Loc.T("gm.title"));
            title.AddToClassList("dev-title");
            titleBar.Add(title);
            var gameMenu = MakeButton(Loc.T("gm.gameMenu"), () =>
            {
                Close();
                GameMenuRequested?.Invoke();
            });
            gameMenu.AddToClassList("dev-small");
            titleBar.Add(gameMenu);
            _keyHint = new Label();
            _keyHint.AddToClassList("dev-key");
            titleBar.Add(_keyHint);
            var close = MakeButton("×", Close);
            close.AddToClassList("dev-close");
            titleBar.Add(close);
            frame.Add(titleBar);

            _target = new Label();
            _target.AddToClassList("dev-target");
            frame.Add(_target);

            var scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.AddToClassList("dev-scroll");
            frame.Add(scroll);

            BuildGmSection(scroll);
            BuildPlayerSection(scroll);
            BuildTeleportSection(scroll);
            BuildWorldSection(scroll);
            BuildSpawnSection(scroll);
            BuildQuestSection(scroll);
            BuildDebugSection(scroll);
            BuildDeveloperSection(scroll);

            _feedback = new Label(Loc.T("gm.ready"));
            _feedback.AddToClassList("dev-feedback");
            frame.Add(_feedback);
            _history = new VisualElement();
            _history.AddToClassList("dev-history");
            frame.Add(_history);

            // Typing in the menu's fields must not move the character or fire the action bar.
            _root.RegisterCallback<FocusInEvent>(e => InputGate.Typing = IsTextInput(e.target as VisualElement));
            _root.RegisterCallback<FocusOutEvent>(e => InputGate.Typing = false);

            client.Gm.Completed += OnCompleted;
            client.GmStatusChanged += OnStatus;
            client.GmCatalogReceived += RebuildCatalogs;
            client.GameDataReceived += RebuildCatalogs;
            RebuildCatalogs();
        }

        // ------------------------------------------------------------------ open, close, Esc

        public void Open()
        {
            if (!IsAvailable) return;
            IsOpen = true;
            _root.RemoveFromClassList("hidden");
            // Above the HUD panels added later (the F3 panel, windows): the menu was asked for.
            _root.parent?.BringToFront();
            Refresh(force: true);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            BlurFields();
            _root.AddToClassList("hidden");
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        /// <summary>Esc while typing in one of the menu's fields leaves the field (the next Esc closes the menu).</summary>
        public bool HandleEscapeFocus()
        {
            if (!IsOpen || !InputGate.Typing) return false;
            var focused = _root.panel?.focusController?.focusedElement as VisualElement;
            if (focused == null || !_root.Contains(focused)) return false;
            BlurFields();
            return true;
        }

        private void BlurFields()
        {
            var focused = _root.panel?.focusController?.focusedElement as VisualElement;
            if (focused != null && _root.Contains(focused)) focused.Blur();
            InputGate.Typing = false;
        }

        private static bool IsTextInput(VisualElement element)
        {
            for (var e = element; e != null; e = e.parent)
                if (e is TextField) return true;
            return false;
        }

        /// <summary>Every frame while in the world: overlays follow the player, the panel refreshes now and then.</summary>
        public void Update(Vector3? playerPosition)
        {
            if (playerPosition.HasValue) _geometry.Update(playerPosition.Value);
            if (_openWhenReady && IsAvailable)
            {
                _openWhenReady = false;
                Open();
            }
            if (IsOpen && !IsAvailable) Close();
            if (IsOpen && Time.unscaledTime >= _refreshAt) Refresh(force: false);
        }

        // ------------------------------------------------------------------ sections

        private void BuildGmSection(VisualElement parent)
        {
            var body = Section(parent, "gm", DeveloperMenuArt.Icon.Crown, Loc.T("gm.section.gm"));
            var toggles = Row(body);
            BoundToggle(toggles, Loc.T("gm.godMode"), GmCommands.GodMode, () => _client.GmStatus.Overrides.HasFlag(GmOverrides.GodMode));
            BoundToggle(toggles, Loc.T("gm.noCooldown"), GmCommands.NoCooldown, () => _client.GmStatus.Overrides.HasFlag(GmOverrides.NoCooldowns));
            var combat = Row(body);
            ActionButton(combat, Loc.T("gm.killTarget"), GmCommands.KillTarget, null, () => EnemyTarget() == null ? Loc.T("gm.needEnemy") : null);
            ActionButton(combat, Loc.T("gm.revive"), GmCommands.Revive, null, () => _client.Self.Values.IsDead ? null : Loc.T("gm.notDead"));
            var health = Row(body);
            ActionButton(health, Loc.T("gm.heal"), GmCommands.HealFull, null, () => _client.Self.Values.IsDead ? Loc.T("gm.dead") : null);
            ActionButton(health, Loc.T("gm.restore"), GmCommands.RestoreResources, null,
                () => _client.Self.Values.IsDead ? Loc.T("gm.dead") : _client.Self.Values.PowerType == PowerType.Rage ? Loc.T("gm.rage") : null);

            var level = Row(body, Loc.T("gm.setLevel"), GmCommands.SetLevel);
            _level = Field(level, "10", () => Run(GmCommands.SetLevel, ("level", _level.value)));
            ActionButton(level, Loc.T("gm.set"), GmCommands.SetLevel, () => Args(("level", _level.value)));

            var teleport = Row(body, Loc.T("gm.teleportTo"));
            _destinations = MakePicker(body, teleport, withSearch: false);
            ActionButton(teleport, Loc.T("gm.go"), GmCommands.TeleportLocation, () => Args(("location", _destinations.SelectedId ?? "")),
                () => _destinations.SelectedId == null ? Loc.T("gm.noDestinations") : null);
        }

        private void BuildPlayerSection(VisualElement parent)
        {
            var body = Section(parent, "player", DeveloperMenuArt.Icon.Player, Loc.T("gm.section.player"));
            var itemRow = Row(body, Loc.T("gm.addItem"), GmCommands.AddItem);
            _items = MakePicker(body, itemRow, withSearch: true);
            var itemAdd = Row(body);
            itemAdd.Add(SmallLabel(Loc.T("gm.count")));
            _itemCount = Field(itemAdd, "1", () => Run(GmCommands.AddItem, ("item", _items.SelectedId ?? ""), ("count", _itemCount.value)));
            ActionButton(itemAdd, Loc.T("gm.add"), GmCommands.AddItem, () => Args(("item", _items.SelectedId ?? ""), ("count", _itemCount.value)),
                () => _items.SelectedId == null ? Loc.T("gm.pickItem") : null);

            var gold = Row(body, Loc.T("gm.addGold"), GmCommands.AddGold);
            _gold = Field(gold, "100", () => RunGold());
            ActionButton(gold, Loc.T("gm.add"), GmCommands.AddGold, GoldArgs);
            var xp = Row(body, Loc.T("gm.addXp"), GmCommands.AddXp);
            _xp = Field(xp, "100", () => Run(GmCommands.AddXp, ("amount", _xp.value)));
            ActionButton(xp, Loc.T("gm.add"), GmCommands.AddXp, () => Args(("amount", _xp.value)));

            var speed = Row(body, Loc.T("gm.speed"));
            _speed = Slider(speed, 25, 500, 100);
            ActionButton(speed, Loc.T("gm.apply"), GmCommands.SpeedMultiplier, () => Args(("percent", GmCommands.Format((long)_speed.value))));
            ActionButton(speed, Loc.T("gm.reset"), GmCommands.SpeedMultiplier, () => Args(("percent", "100")));
            _bindings.Add(() => SyncSlider(_speed, _client.GmStatus.SpeedPercent));
        }

        private void BuildTeleportSection(VisualElement parent)
        {
            var body = Section(parent, "teleport", DeveloperMenuArt.Icon.Pin, Loc.T("gm.section.teleport"));
            var coordinates = Row(body, Loc.T("gm.coordinates"));
            coordinates.Add(SmallLabel("X"));
            _x = Field(coordinates, "0", () => Run(GmCommands.TeleportCoordinates, ("x", _x.value), ("z", _z.value)));
            coordinates.Add(SmallLabel("Z"));
            _z = Field(coordinates, "0", () => Run(GmCommands.TeleportCoordinates, ("x", _x.value), ("z", _z.value)));
            ActionButton(coordinates, Loc.T("gm.go"), GmCommands.TeleportCoordinates, () => Args(("x", _x.value), ("z", _z.value)));
            var pick = Row(body);
            var pickButton = MakeButton(Loc.T("gm.pickOnMap"), PickOnMap);
            pickButton.AddToClassList("primary");
            pick.Add(pickButton);
            pick.Add(MakeButton(Loc.T("gm.here"), FillCoordinates));
            var target = Row(body);
            ActionButton(target, Loc.T("gm.toTarget"), GmCommands.TeleportTarget, null, () => TargetUnit() == null ? Loc.T("gm.needTarget") : null);
        }

        private void BuildWorldSection(VisualElement parent)
        {
            var body = Section(parent, "world", DeveloperMenuArt.Icon.Gear, Loc.T("gm.section.world"));
            var respawn = Row(body, Loc.T("gm.respawnNearby"), GmCommands.WorldRespawn);
            respawn.Add(SmallLabel(Loc.T("gm.radius")));
            _radius = Field(respawn, "60", () => Run(GmCommands.WorldRespawn, ("radius", _radius.value)));
            ActionButton(respawn, Loc.T("gm.respawn"), GmCommands.WorldRespawn, () => Args(("radius", _radius.value)));
            var damage = Row(body, Loc.T("gm.damage"));
            _damage = Slider(damage, 0, 1000, 100);
            ActionButton(damage, Loc.T("gm.apply"), GmCommands.DamageMultiplier, () => Args(("percent", GmCommands.Format((long)_damage.value))));
            ActionButton(damage, Loc.T("gm.reset"), GmCommands.DamageMultiplier, () => Args(("percent", "100")));
            _bindings.Add(() => SyncSlider(_damage, _client.GmStatus.DamagePercent));
            Unsupported(body, Loc.T("gm.timeOfDay"), Loc.T("gm.why.worldClock"));
            Unsupported(body, Loc.T("gm.weather"), Loc.T("gm.why.weather"));
            Unsupported(body, Loc.T("gm.timeScale"), Loc.T("gm.why.timeScale"));
        }

        private void BuildSpawnSection(VisualElement parent)
        {
            var body = Section(parent, "spawn", DeveloperMenuArt.Icon.Paw, Loc.T("gm.section.spawn"));
            var tabs = Row(body);
            tabs.AddToClassList("dev-tabs");
            Button creaturesTab = null, npcsTab = null;
            creaturesTab = MakeButton(Loc.T("gm.creatures"), () => SelectTab(false, creaturesTab, npcsTab));
            npcsTab = MakeButton(Loc.T("gm.npcs"), () => SelectTab(true, creaturesTab, npcsTab));
            creaturesTab.AddToClassList("dev-tab");
            npcsTab.AddToClassList("dev-tab");
            creaturesTab.AddToClassList("selected");
            tabs.Add(creaturesTab);
            tabs.Add(npcsTab);

            var pick = Row(body);
            _creatures = MakePicker(body, pick, withSearch: true);
            _creatures.Filter = e => IsFriendlyCreature(e.Id) == _friendlyTab;
            var spawn = Row(body);
            spawn.Add(SmallLabel(Loc.T("gm.count")));
            _spawnCountField = Field(spawn, "1", () => Run(GmCommands.SpawnEntity, ("creature", _creatures.SelectedId ?? ""), ("count", _spawnCountField.value)));
            ActionButton(spawn, Loc.T("gm.spawn"), GmCommands.SpawnEntity, () => Args(("creature", _creatures.SelectedId ?? ""), ("count", _spawnCountField.value)),
                () => _creatures.SelectedId == null ? Loc.T("gm.pickCreature") : null).Button.AddToClassList("primary");
            var debugSpawns = Row(body);
            _spawnCount = SmallLabel("");
            _spawnCount.AddToClassList("dev-grow");
            debugSpawns.Add(_spawnCount);
            ActionButton(debugSpawns, Loc.T("gm.despawn"), GmCommands.DespawnDebug, null,
                () => _client.GmStatus.DebugSpawns == 0 ? Loc.T("gm.noDebugSpawns") : null);
            _bindings.Add(() => _spawnCount.text = string.Format(Loc.T("gm.debugSpawns"), _client.GmStatus.DebugSpawns, GmCommands.MaxDebugSpawns));
        }

        private void SelectTab(bool friendly, Button creatures, Button npcs)
        {
            _friendlyTab = friendly;
            creatures.EnableInClassList("selected", !friendly);
            npcs.EnableInClassList("selected", friendly);
            _creatures.Apply();
        }

        private void BuildQuestSection(VisualElement parent)
        {
            var body = Section(parent, "quest", DeveloperMenuArt.Icon.Scroll, Loc.T("gm.section.quest"));
            var pick = Row(body, Loc.T("gm.quest"), GmCommands.QuestStart);
            _quests = MakePicker(body, pick, withSearch: true);
            var actions = Row(body);
            Func<string> none = () => _quests.SelectedId == null ? Loc.T("gm.pickQuest") : null;
            ActionButton(actions, Loc.T("gm.questStart"), GmCommands.QuestStart, () => Args(("quest", _quests.SelectedId ?? "")), none);
            ActionButton(actions, Loc.T("gm.questComplete"), GmCommands.QuestComplete, () => Args(("quest", _quests.SelectedId ?? "")), none);
            ActionButton(actions, Loc.T("gm.questReset"), GmCommands.QuestReset, () => Args(("quest", _quests.SelectedId ?? "")), none);
        }

        private void BuildDebugSection(VisualElement parent)
        {
            var body = Section(parent, "debug", DeveloperMenuArt.Icon.Bug, Loc.T("gm.section.debug"));
            BoundToggle(Row(body), Loc.T("gm.showFps"), GmCommands.ShowFps, () => DebugOverlay != null && DebugOverlay.Visible, KeyBindings.ShortcutText(GameAction.ToggleDebug));
            BoundToggle(Row(body), Loc.T("gm.showPerformance"), GmCommands.ShowPerformance, () => DebugOverlay != null && DebugOverlay.Detailed);
            BoundToggle(Row(body), Loc.T("gm.showCollision"), GmCommands.ShowCollision, () => _geometry.IsEnabled(GmCommands.ShowCollision), Stats(GmCommands.ShowCollision));
            BoundToggle(Row(body), Loc.T("gm.showNavMesh"), GmCommands.ShowNavMesh, () => _geometry.IsEnabled(GmCommands.ShowNavMesh), Stats(GmCommands.ShowNavMesh));
            BoundToggle(Row(body), Loc.T("gm.showSpawnPoints"), GmCommands.ShowSpawnPoints, () => _geometry.IsEnabled(GmCommands.ShowSpawnPoints), Stats(GmCommands.ShowSpawnPoints));
            var inspect = Row(body);
            ActionButton(inspect, Loc.T("gm.inspect"), GmCommands.InspectTarget, null, () => TargetUnit() == null ? Loc.T("gm.needTarget") : null);
            _inspect = new Label();
            _inspect.AddToClassList("dev-inspect");
            _inspect.AddToClassList("hidden");
            body.Add(_inspect);
            Unsupported(body, Loc.T("gm.aiPath"), Loc.T("gm.why.aiPath"));
        }

        private Func<string> Stats(string commandId) => () =>
        {
            var (segments, truncated) = _geometry.Stats(commandId);
            return _geometry.IsEnabled(commandId) ? string.Format(Loc.T(truncated ? "gm.segmentsCut" : "gm.segments"), segments) : null;
        };

        private void BuildDeveloperSection(VisualElement parent)
        {
            var body = Section(parent, "developer", DeveloperMenuArt.Icon.Wrench, Loc.T("gm.section.developer"));
            var reset = Row(body);
            var resetButton = MakeButton(Loc.T("gm.resetOverrides"), ResetOverrides);
            resetButton.AddToClassList("primary");
            reset.Add(resetButton);
            ActionButton(reset, Loc.T("gm.despawn"), GmCommands.DespawnDebug, null, () => _client.GmStatus.DebugSpawns == 0 ? Loc.T("gm.noDebugSpawns") : null);
            var escape = Row(body);
            _escapeToggle = new Toggle(Loc.T("gm.escapeOpens")) { focusable = false, value = OpensOnEscape };
            _escapeToggle.AddToClassList("dev-toggle");
            _escapeToggle.RegisterValueChangedCallback(e => Preferences.SetDeveloperSetting(EscapeSetting, e.newValue ? "on" : "off"));
            escape.Add(_escapeToggle);
            Unsupported(body, Loc.T("gm.worldSaves"), Loc.T("gm.why.worldSaves"));
        }

        /// <summary>Server overrides back to normal and the overlays off (the F3 panel is the player's own choice).</summary>
        private void ResetOverrides()
        {
            Run(GmCommands.ResetOverrides);
            foreach (var id in new[] { GmCommands.ShowCollision, GmCommands.ShowNavMesh, GmCommands.ShowSpawnPoints, GmCommands.ShowPerformance })
                if (IsLocalOn(id)) Run(id, ("enabled", "false"));
        }

        private bool IsLocalOn(string commandId) =>
            commandId == GmCommands.ShowPerformance ? DebugOverlay != null && DebugOverlay.Detailed : _geometry.IsEnabled(commandId);

        // ------------------------------------------------------------------ client-side commands

        private DebugOverlay _debugOverlay;

        private DebugOverlay DebugOverlay
        {
            get
            {
                if (_debugOverlay == null) _debugOverlay = UnityEngine.Object.FindAnyObjectByType<DebugOverlay>();
                return _debugOverlay;
            }
        }

        private void RegisterLocalCommands()
        {
            _client.Gm.RegisterLocal(GmCommands.ShowFps, args =>
            {
                if (DebugOverlay == null) return GmCommandClient.Local(GmCommands.ShowFps, GmResultCode.NotAvailable, "The debug panel is not loaded.");
                DebugOverlay.Visible = args.Bool("enabled");
                return GmCommandClient.Local(GmCommands.ShowFps, GmResultCode.Ok, DebugOverlay.Visible ? "FPS and stats panel shown." : "FPS and stats panel hidden.");
            });
            _client.Gm.RegisterLocal(GmCommands.ShowPerformance, args =>
            {
                if (DebugOverlay == null) return GmCommandClient.Local(GmCommands.ShowPerformance, GmResultCode.NotAvailable, "The debug panel is not loaded.");
                DebugOverlay.Detailed = args.Bool("enabled");
                if (DebugOverlay.Detailed) DebugOverlay.Visible = true;
                return GmCommandClient.Local(GmCommands.ShowPerformance, GmResultCode.Ok,
                    DebugOverlay.Detailed ? "Frame time, memory and units shown in the stats panel." : "Performance details hidden.");
            });
        }

        // ------------------------------------------------------------------ running commands

        private static List<KeyValuePair<string, string>> Args(params (string Key, string Value)[] args) =>
            args.Select(a => new KeyValuePair<string, string>(a.Key, a.Value?.Trim() ?? "")).ToList();

        private void Run(string commandId, params (string Key, string Value)[] args)
        {
            _feedback.text = string.Format(Loc.T("gm.sending"), commandId);
            _feedback.RemoveFromClassList("ok");
            _feedback.RemoveFromClassList("fail");
            _client.Gm.Execute(commandId, Args(args));
            Refresh(force: true);
        }

        private void RunGold()
        {
            var args = GoldArgs();
            if (args == null) return;
            _client.Gm.Execute(GmCommands.AddGold, args);
        }

        /// <summary>The field holds gold; the command takes copper.</summary>
        private List<KeyValuePair<string, string>> GoldArgs()
        {
            string text = _gold.value?.Trim() ?? "";
            if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long gold) || gold < 1 || gold > 100000)
            {
                ShowLocalError(GmCommands.AddGold, Loc.T("gm.goldRange"));
                return null;
            }
            return Args(("copper", GmCommands.Format(gold * Money.CopperPerGold)));
        }

        private void ShowLocalError(string commandId, string message)
        {
            var result = GmCommandClient.Local(commandId, GmResultCode.InvalidArgument, message);
            OnCompleted(result);
        }

        private void OnCompleted(GmCommandResult result)
        {
            bool ok = result.Ok;
            _feedback.text = $"{(ok ? "✓" : "✗")} {result.Code}: {result.Message}";
            _feedback.EnableInClassList("ok", ok);
            _feedback.EnableInClassList("fail", !ok);
            if (result.CommandId == GmCommands.InspectTarget && ok)
            {
                _inspect.text = string.Join("\n", result.Data.Select(d => $"{d.Key}: {d.Value}"));
                _inspect.RemoveFromClassList("hidden");
            }
            RebuildHistory();
            Refresh(force: true);
        }

        private void RebuildHistory()
        {
            _history.Clear();
            var entries = _client.Gm.History;
            for (int i = entries.Count - 1, shown = 0; i >= 0 && shown < HistoryRows; i--, shown++)
            {
                var r = entries[i];
                var row = new Label($"{(r.Ok ? "✓" : "✗")} {r.CommandId}  {r.Code}");
                row.AddToClassList("dev-history-row");
                row.EnableInClassList("fail", !r.Ok);
                _history.Add(row);
            }
        }

        private void OnStatus() => Refresh(force: true);

        // ------------------------------------------------------------------ state

        private void Refresh(bool force)
        {
            if (!IsOpen && !force) return;
            _refreshAt = Time.unscaledTime + RefreshSeconds;
            var status = _client.GmStatus;
            _badge.text = status.GmLevel > 0 ? string.Format(Loc.T("gm.badge"), status.GmLevel) : Loc.T("gm.noRights");
            _target.text = TargetText();
            string key = KeyBindings.ShortcutText(GameAction.ToggleDeveloperMenu);
            _keyHint.text = key.Length > 0 ? key + " / " + Loc.T("gm.escKey") : Loc.T("gm.escKey");
            foreach (var bind in _bindings) bind();
            foreach (var action in _actions)
            {
                string reason = action.Unavailable?.Invoke();
                action.Button.SetEnabled(reason == null);
                action.Button.EnableInClassList("pending", _client.Gm.IsPending(action.CommandId));
            }
            // Each row says why its buttons are off.
            foreach (var group in _actions.Where(a => a.Hint != null).GroupBy(a => a.Hint))
            {
                var reasons = group.Select(a => a.Unavailable?.Invoke()).Where(r => r != null).Distinct().ToList();
                group.Key.text = string.Join("  ", reasons);
                group.Key.EnableInClassList("hidden", reasons.Count == 0);
            }
            if (_escapeToggle != null) _escapeToggle.SetValueWithoutNotify(OpensOnEscape);
        }

        private string TargetText()
        {
            var unit = TargetUnit();
            if (unit == null) return Loc.T("gm.noTarget");
            var values = unit.Unit.Values;
            string reaction = Loc.T("gm.reaction." + _client.GetReaction(unit.Guid).ToString().ToLowerInvariant());
            return string.Format(Loc.T("gm.target"), unit.Name, values.Level, values.Health, values.MaxHealth, reaction);
        }

        private RemoteEntity TargetUnit()
        {
            var guid = _client.Target;
            if (guid.IsEmpty || guid == _client.LocalGuid) return null;
            return _client.World.TryGet(guid, out var entity) && entity.Unit != null ? entity : null;
        }

        private RemoteEntity EnemyTarget()
        {
            var unit = TargetUnit();
            return unit != null && !unit.Unit.Values.IsDead && _client.GetReaction(unit.Guid) != Reaction.Friendly ? unit : null;
        }

        private bool IsFriendlyCreature(string id)
        {
            foreach (var c in _client.GmCatalog.Creatures)
                if (GmCommands.Format((long)c.Id) == id) return c.Reaction == Reaction.Friendly;
            return false;
        }

        private void RebuildCatalogs()
        {
            if (_items == null) return;
            _items.All = _client.Data.Items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .Select(i => (GmCommands.Format((long)i.Id), $"{i.Name}  #{i.Id}")).ToList();
            _quests.All = _client.Data.Quests.OrderBy(q => q.Id)
                .Select(q => (GmCommands.Format((long)q.Id), $"{q.Title}  #{q.Id}")).ToList();
            _creatures.All = _client.GmCatalog.Creatures
                .Select(c => (GmCommands.Format((long)c.Id), $"{c.Name} ({LevelRange(c)})  #{c.Id}")).ToList();
            var map = MapInfo.Current;
            _destinations.All = _client.GmCatalog.Destinations.Select(d =>
            {
                string area = map != null && map.mapId == _client.GmCatalog.MapId ? map.AreaAt(d.X, d.Z) : null;
                string kind = Loc.T("gm.destination." + d.Kind.ToString().ToLowerInvariant());
                string label = area != null && area != d.Name ? $"{kind}: {d.Name}  ({area})" : $"{kind}: {d.Name}";
                return (d.Id, label);
            }).ToList();
            foreach (var p in new[] { _items, _quests, _creatures, _destinations }) p.Apply();
        }

        private static string LevelRange(GmCreatureEntry c) => c.MinLevel == c.MaxLevel ? $"{c.MinLevel}" : $"{c.MinLevel}-{c.MaxLevel}";

        /// <summary>Opens the world map for picking a teleport destination (the "Pick on map" button).</summary>
        public void PickOnMap()
        {
            _feedback.text = Loc.T("gm.pickStarted");
            _feedback.RemoveFromClassList("ok");
            _feedback.RemoveFromClassList("fail");
            MapPickRequested?.Invoke();
        }

        /// <summary>A point picked on the world map: fills X and Z; <paramref name="teleport"/> goes there at once.</summary>
        public void SetCoordinates(float x, float z, bool teleport)
        {
            _x.value = x.ToString("0.0", CultureInfo.InvariantCulture);
            _z.value = z.ToString("0.0", CultureInfo.InvariantCulture);
            if (teleport) Run(GmCommands.TeleportCoordinates, ("x", _x.value), ("z", _z.value));
            else _feedback.text = string.Format(Loc.T("gm.picked"), _x.value, _z.value);
        }

        /// <summary>The teleport fields as text (automation checks what a map click filled in).</summary>
        public string Coordinates => $"{_x.value},{_z.value}";

        private void FillCoordinates()
        {
            var p = _client.LocalMovement.Position;
            _x.value = p.X.ToString("0.0", CultureInfo.InvariantCulture);
            _z.value = p.Z.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static void SyncSlider(SliderInt slider, int serverValue)
        {
            // Show the server's value unless the user is dragging the slider.
            if (slider.panel?.focusController?.focusedElement == slider) return;
            slider.SetValueWithoutNotify(serverValue);
        }

        // ------------------------------------------------------------------ building blocks

        private VisualElement Section(VisualElement parent, string id, DeveloperMenuArt.Icon icon, string title)
        {
            var section = new VisualElement();
            section.AddToClassList("dev-section");
            var header = new VisualElement();
            header.AddToClassList("dev-section-header");
            DeveloperMenuArt.AddRivets(header);
            header.Add(DeveloperMenuArt.MakeIcon(icon));
            var label = new Label(title);
            label.AddToClassList("dev-section-title");
            header.Add(label);
            var chevron = new Label();
            chevron.AddToClassList("dev-chevron");
            header.Add(chevron);
            var body = new VisualElement();
            body.AddToClassList("dev-section-body");
            section.Add(header);
            section.Add(body);
            parent.Add(section);

            void Apply()
            {
                bool collapsed = _collapsed.Contains(id);
                section.EnableInClassList("collapsed", collapsed);
                chevron.text = collapsed ? "▸" : "▾";
            }
            header.RegisterCallback<ClickEvent>(_ =>
            {
                if (!_collapsed.Remove(id)) _collapsed.Add(id);
                Preferences.SetDeveloperSetting(CollapsedSetting, string.Join(",", _collapsed));
                Apply();
            });
            Apply();
            return body;
        }

        /// <summary>A row; with a label it starts with it, and a "saved" tag if the command changes saved state.</summary>
        private static VisualElement Row(VisualElement body, string label = null, string commandId = null)
        {
            var row = new VisualElement();
            row.AddToClassList("dev-row");
            if (label != null)
            {
                var text = new Label(label);
                text.AddToClassList("dev-label");
                row.Add(text);
            }
            if (commandId != null && GmCommands.Find(commandId)?.Persistent == true)
            {
                var tag = new Label(Loc.T("gm.saved"));
                tag.AddToClassList("dev-tag-saved");
                row.Add(tag);
            }
            body.Add(row);
            return row;
        }

        private static Label SmallLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("dev-small-label");
            return label;
        }

        private static Button MakeButton(string text, System.Action onClick)
        {
            // Not focusable: Space and Enter must keep jumping and chatting after a click.
            var button = new Button(onClick) { text = text, focusable = false };
            button.AddToClassList("dev-button");
            return button;
        }

        private ActionEntry ActionButton(VisualElement row, string text, string commandId, Func<List<KeyValuePair<string, string>>> args = null, Func<string> unavailable = null)
        {
            var button = MakeButton(text, () =>
            {
                var a = args?.Invoke();
                if (args != null && a == null) return; // refused locally, already shown
                _feedback.text = string.Format(Loc.T("gm.sending"), commandId);
                _client.Gm.Execute(commandId, a);
                Refresh(force: true);
            });
            row.Add(button);
            var action = new ActionEntry { Button = button, CommandId = commandId, Unavailable = unavailable };
            // The reason sits under the row (runtime UI has no tooltips).
            if (unavailable != null) action.Hint = HintFor(row);
            _actions.Add(action);
            return action;
        }

        private readonly Dictionary<VisualElement, Label> _hints = new Dictionary<VisualElement, Label>();

        private Label HintFor(VisualElement row)
        {
            if (_hints.TryGetValue(row, out var hint)) return hint;
            hint = new Label();
            hint.AddToClassList("dev-hint");
            hint.AddToClassList("hidden");
            int index = row.parent.IndexOf(row);
            row.parent.Insert(index + 1, hint);
            _hints.Add(row, hint);
            return hint;
        }

        private TextField Field(VisualElement row, string value, System.Action submit)
        {
            var field = new TextField { value = value, isDelayed = false };
            field.AddToClassList("dev-field");
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
                submit();
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
            row.Add(field);
            return field;
        }

        private static SliderInt Slider(VisualElement row, int min, int max, int value)
        {
            var slider = new SliderInt(min, max) { value = value, showInputField = true };
            slider.AddToClassList("dev-slider");
            row.Add(slider);
            return slider;
        }

        private void BoundToggle(VisualElement row, string text, string commandId, Func<bool> state, string keyHint = null) =>
            BoundToggle(row, text, commandId, state, keyHint == null ? null : (Func<string>)(() => keyHint));

        /// <summary>A checkbox that shows real state: clicking asks for the change; the state (not the click) sets it.</summary>
        private void BoundToggle(VisualElement row, string text, string commandId, Func<bool> state, Func<string> note)
        {
            var toggle = new Toggle(text) { focusable = false };
            toggle.AddToClassList("dev-toggle");
            toggle.RegisterValueChangedCallback(e =>
            {
                toggle.SetValueWithoutNotify(state());
                Run(commandId, ("enabled", GmCommands.Format(e.newValue)));
                toggle.SetValueWithoutNotify(state());
            });
            row.Add(toggle);
            Label noteLabel = null;
            if (note != null)
            {
                noteLabel = SmallLabel("");
                noteLabel.AddToClassList("dev-note");
                row.Add(noteLabel);
            }
            _bindings.Add(() =>
            {
                toggle.SetValueWithoutNotify(state());
                toggle.EnableInClassList("pending", _client.Gm.IsPending(commandId));
                if (noteLabel != null) noteLabel.text = note() ?? "";
            });
        }

        private Picker MakePicker(VisualElement body, VisualElement row, bool withSearch)
        {
            var picker = new Picker();
            if (withSearch)
            {
                picker.Search = new TextField { value = "" };
                picker.Search.AddToClassList("dev-field");
                picker.Search.AddToClassList("dev-search");
                picker.Search.RegisterValueChangedCallback(_ => picker.Apply());
                row.Add(picker.Search);
                var below = Row(body);
                picker.Dropdown = new DropdownField { focusable = false };
                picker.Dropdown.AddToClassList("dev-dropdown");
                below.Add(picker.Dropdown);
            }
            else
            {
                picker.Dropdown = new DropdownField { focusable = false };
                picker.Dropdown.AddToClassList("dev-dropdown");
                row.Add(picker.Dropdown);
            }
            return picker;
        }

        private static void Unsupported(VisualElement body, string label, string why)
        {
            var row = Row(body, label);
            row.AddToClassList("dev-unsupported");
            var reason = new Label(why);
            reason.AddToClassList("dev-hint");
            reason.AddToClassList("dev-grow");
            row.Add(reason);
        }
    }
}
