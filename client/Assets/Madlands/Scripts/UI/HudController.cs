using System.Collections.Generic;
using System.Globalization;
using Madlands.ClientCore;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Movement;
using Madlands.Shared.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Madlands.Client
{
    /// <summary>
    /// The in-game HUD (UI Toolkit, the "hud" part of FrontEnd.uxml): player and target frames,
    /// auras, cast bars, the action bar (keys 1-0, -, =), experience bar, error texts, chat,
    /// nameplates and floating combat text, and the item and quest windows (bags B, character C,
    /// quest log L, loot, vendors, NPC dialogs, quest tracker). Everything shown comes from
    /// ClientCore state; actions only ask the server (which checks everything again).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class HudController : MonoBehaviour
    {
        private const int SlotCount = 12;
        private const float ErrorSeconds = 2f;
        private const float NoticeSeconds = 3f;


        private sealed class Slot
        {
            public VisualElement Root;
            public VisualElement Icon;
            public VisualElement Shade;
            public Label Cooldown;
            public Label Key;
            public uint SpellId;
        }

        private sealed class AuraIcon
        {
            public VisualElement Root;
            public Label Time;
            public Label Stacks;
            public VisualElement Icon;
        }

        [SerializeField] private NetworkController network;
        [SerializeField] private TargetingController targeting;

        private VisualElement _hud, _playerFrame, _targetFrame, _playerHealthFill, _playerPower, _playerPowerFill;
        private VisualElement _stamina, _staminaFill;
        private VisualElement _targetHealthFill, _targetPower, _targetPowerFill, _playerAuras, _targetAuras;
        private VisualElement _playerCast, _playerCastFill, _targetCast, _targetCastFill, _actionBar, _xpFill, _tooltip, _deathPanel;
        private Label _playerName, _playerLevel, _playerHealthText, _playerPowerText, _targetName, _targetLevel, _targetHealthText;
        private Label _targetPowerText, _playerCastText, _targetCastText, _xpText, _errorText, _noticeText, _tooltipTitle, _tooltipMeta, _tooltipBody;
        private ChatPanel _chat;
        private WorldOverlay _overlay;
        private ItemTooltip _itemTooltip;
        private InventoryUi _inventory;
        private LootUi _loot;
        private VendorUi _vendor;
        private QuestUi _quests;
        private MapUi _map;
        private GameSounds _sounds;
        private Button _characterButton, _bagsButton, _questsButton;
        private readonly Slot[] _slots = new Slot[SlotCount];
        private readonly List<AuraIcon> _playerAuraIcons = new List<AuraIcon>();
        private readonly List<AuraIcon> _targetAuraIcons = new List<AuraIcon>();
        private float _errorUntil;
        private float _noticeUntil;
        private float _playerCastFailedUntil;
        private bool _selfAurasDirty = true;
        private bool _targetAurasDirty = true;
        private EntityGuid _shownTarget;
        private int _lastLevel;
        private bool _subscribed;

        private GameClient Client => network.Client;
        public ChatPanel Chat => _chat;
        public QuestUi Quests => _quests;

        private void Awake()
        {
            if (network == null) network = GetComponent<NetworkController>();
            if (targeting == null) targeting = GetComponent<TargetingController>();
        }

        private void OnEnable()
        {
            var document = GetComponent<UIDocument>();
            UiPointer.Register(document);
            var r = document.rootVisualElement;
            Loc.Apply(r);
            _hud = r.Q("hud");
            // Only buttons, aura icons and the chat line take the mouse; the rest lets clicks through to the world.
            _hud.Query<VisualElement>().ForEach(e => e.pickingMode = PickingMode.Ignore);

            _playerFrame = r.Q("player-frame");
            _playerName = r.Q<Label>("player-name");
            _playerLevel = r.Q<Label>("player-level");
            _playerHealthFill = r.Q("player-health-fill");
            _playerHealthText = r.Q<Label>("player-health-text");
            _playerPower = r.Q("player-power");
            _playerPowerFill = r.Q("player-power-fill");
            _playerPowerText = r.Q<Label>("player-power-text");
            _stamina = r.Q("player-stamina");
            _staminaFill = r.Q("player-stamina-fill");
            _targetFrame = r.Q("target-frame");
            _targetName = r.Q<Label>("target-name");
            _targetLevel = r.Q<Label>("target-level");
            _targetHealthFill = r.Q("target-health-fill");
            _targetHealthText = r.Q<Label>("target-health-text");
            _targetPower = r.Q("target-power");
            _targetPowerFill = r.Q("target-power-fill");
            _targetPowerText = r.Q<Label>("target-power-text");
            _playerAuras = r.Q("player-auras");
            _targetAuras = r.Q("target-auras");
            _playerCast = r.Q("player-cast");
            _playerCastFill = r.Q("player-cast-fill");
            _playerCastText = r.Q<Label>("player-cast-text");
            _targetCast = r.Q("target-cast");
            _targetCastFill = r.Q("target-cast-fill");
            _targetCastText = r.Q<Label>("target-cast-text");
            _actionBar = r.Q("action-bar");
            _xpFill = r.Q("xp-fill");
            _xpText = r.Q<Label>("xp-text");
            _errorText = r.Q<Label>("error-text");
            _noticeText = r.Q<Label>("notice-text");
            _tooltip = r.Q("tooltip");
            _tooltipTitle = r.Q<Label>("tooltip-title");
            _tooltipMeta = r.Q<Label>("tooltip-meta");
            _tooltipBody = r.Q<Label>("tooltip-body");
            _deathPanel = r.Q("death-panel");
            _deathPanel.pickingMode = PickingMode.Position;
            var release = r.Q<Button>("release-button");
            release.pickingMode = PickingMode.Position;
            release.clicked += () => Client.ReleaseSpirit();

            var chatInput = r.Q<TextField>("chat-input");
            chatInput.pickingMode = PickingMode.Position;
            chatInput.Query<VisualElement>().ForEach(e => e.pickingMode = PickingMode.Position);
            _chat = new ChatPanel(r.Q("chat-log"), chatInput, text => Client.SendChat(text));
            _overlay = new WorldOverlay(r.Q("nameplates"), r.Q("combat-text"));
            _overlay.Clicked += (guid, use) => targeting.Select(guid, use);
            BuildActionBar();
            BuildWindows();
            KeyBindings.Changed += RelabelKeys;
        }

        /// <summary>Key labels on the action bar and the micro menu follow the bindings.</summary>
        private void RelabelKeys()
        {
            for (int i = 0; i < SlotCount; i++) _slots[i].Key.text = KeyBindings.ShortcutText(KeyBindings.ActionButton(i));
            _characterButton.text = MicroText(Loc.T("micro.character"), GameAction.ToggleCharacter);
            _bagsButton.text = MicroText(Loc.T("micro.bags"), GameAction.ToggleBags);
            _questsButton.text = MicroText(Loc.T("micro.quests"), GameAction.ToggleQuestLog);
        }

        private static string MicroText(string name, GameAction action)
        {
            string key = KeyBindings.ShortcutText(action);
            return key.Length > 0 ? name + "\n(" + key + ")" : name;
        }

        /// <summary>Windows and the tracker in their own layer; tooltips and the cursor item above them.</summary>
        private void BuildWindows()
        {
            var windows = Layer("windows");
            var overlay = Layer("window-overlay");
            _map = new MapUi(windows, Client);
            _map.AreaEntered += area => ShowNotice(area, small: true);
            _itemTooltip = new ItemTooltip(overlay);
            _inventory = new InventoryUi(windows, overlay, Client, _itemTooltip, ShowError);
            _loot = new LootUi(windows, Client, _itemTooltip);
            _vendor = new VendorUi(windows, Client, _itemTooltip, _inventory);
            _quests = new QuestUi(windows, Client, _itemTooltip);

            var menu = new VisualElement { pickingMode = PickingMode.Ignore };
            menu.AddToClassList("micro-menu");
            _characterButton = MicroButton(menu, MicroText(Loc.T("micro.character"), GameAction.ToggleCharacter), () => _inventory.ToggleCharacter());
            _bagsButton = MicroButton(menu, MicroText(Loc.T("micro.bags"), GameAction.ToggleBags), () => _inventory.ToggleBags());
            _questsButton = MicroButton(menu, MicroText(Loc.T("micro.quests"), GameAction.ToggleQuestLog), () => _quests.ToggleLog());
            windows.Add(menu);
        }

        private VisualElement Layer(string name)
        {
            var layer = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            layer.AddToClassList("layer");
            _hud.Add(layer);
            return layer;
        }

        private static Button MicroButton(VisualElement menu, string text, System.Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("micro-button");
            menu.Add(button);
            return button;
        }

        /// <summary>Shows the tooltip of an item in the open bags (scripted screenshot runs).</summary>
        public void ShowItemTooltip(uint itemId) => _inventory.ShowTooltipFor(itemId);

        /// <summary>Opens a window by name (bags, character, questlog): used by scripted screenshot runs.</summary>
        public void OpenWindow(string name)
        {
            switch (name.Trim().ToLowerInvariant())
            {
                case "bags":
                    if (!_inventory.BagsOpen) _inventory.ToggleBags();
                    break;
                case "character":
                    if (!_inventory.CharacterOpen) _inventory.ToggleCharacter();
                    break;
                case "questlog":
                    if (!_quests.LogOpen) _quests.ToggleLog();
                    break;
                case "map":
                    if (!_map.WorldMapOpen) _map.ToggleWorldMap();
                    break;
            }
        }

        private void Start()
        {
            var c = Client;
            c.GameDataReceived += RefreshActionBar;
            c.SpellbookChanged += RefreshActionBar;
            c.SpellFailed += OnSpellFailed;
            c.CombatEventReceived += OnCombatEvent;
            c.ChatReceived += OnChat;
            c.ExperienceReceived += OnExperience;
            c.AurasChanged += guid =>
            {
                if (guid == c.LocalGuid) _selfAurasDirty = true;
                if (guid == c.Target) _targetAurasDirty = true;
            };
            c.UnitChanged += OnUnitChanged;
            network.EnteredWorld += () => _map.SetMap(FindAnyObjectByType<MapInfo>());
            _sounds = new GameSounds(Client, network.WorldView);
            c.EnteredWorld += _ =>
            {
                _lastLevel = 0;
                _selfAurasDirty = _targetAurasDirty = true;
                CloseWindows();
            };
            c.InventoryChanged += () => _inventory.Refresh();
            c.StatsChanged += () => _inventory.Refresh();
            c.MoneyChanged += OnMoneyChanged;
            c.ItemReceived += OnItemReceived;
            c.ActionFailed += (error, value) => ShowError(ActionErrorText(error, value));
            c.LootChanged += () => _loot.Refresh();
            c.GossipOpened += menu =>
            {
                _vendor.Close();
                _quests.ShowGossip(menu);
            };
            c.VendorOpened += wares =>
            {
                _quests.CloseDialog();
                _vendor.Open(wares);
            };
            c.QuestLogChanged += () => _quests.RefreshLog();
            c.QuestProgressed += OnQuestProgressed;
            c.QuestStateChanged += OnQuestStateChanged;
            c.QuestCompleted += OnQuestCompleted;
            _subscribed = true;
        }

        private void OnDisable()
        {
            InputGate.Typing = false;
            KeyBindings.Changed -= RelabelKeys;
        }

        // ------------------------------------------------------------------ per frame

        private void Update()
        {
            if (!_subscribed) return;
            bool inWorld = network.InWorld;
            _hud.EnableInClassList("hidden", !inWorld || network.Settings != null && network.Settings.HideHud);
            if (!inWorld)
            {
                if (_chat.IsOpen) _chat.Close();
                _overlay.Clear();
                CloseWindows();
                return;
            }

            if (_lastLevel == 0) _lastLevel = Client.Self.Values.Level;
            UpdatePlayerFrame();
            UpdateTargetFrame();
            UpdateCastBars();
            UpdateActionBar();
            UpdateXp();
            UpdateAuras();
            UpdateTexts();
            _chat.Update();
            _deathPanel.EnableInClassList("hidden", !Client.Self.Values.IsDead);
            _inventory.Update(_hud.panel);
            _map.Update();
            _sounds.Update();
            _inventory.PlaceCharacter(_vendor.IsOpen || _loot.IsOpen || _quests.DialogOpen);
            _vendor.Update();
            _quests.Update();
            _characterButton.EnableInClassList("active", _inventory.CharacterOpen);
            _bagsButton.EnableInClassList("active", _inventory.BagsOpen);
            _questsButton.EnableInClassList("active", _quests.LogOpen);
            HandleKeys();
        }

        private void CloseWindows()
        {
            _inventory.CloseAll();
            _loot.Close();
            _vendor.Close();
            _quests.CloseDialog();
            if (_quests.LogOpen) _quests.ToggleLog();
            _map.Close();
            _itemTooltip.Hide();
        }

        private void LateUpdate()
        {
            if (!network.InWorld) return;
            _overlay.Update(Client, network.WorldView, Camera.main, _hud.panel);
        }

        private void HandleKeys()
        {
            if (InputGate.KeyboardBlocked) return;
            if (KeyBindings.WasPressed(GameAction.OpenChat) && !_chat.ClosedThisFrame)
            {
                _chat.Open();
                return;
            }
            if (KeyBindings.WasPressed(GameAction.ChatCommand))
            {
                _chat.Open(".");
                return;
            }
            if (KeyBindings.WasPressed(GameAction.ToggleBags)) _inventory.ToggleBags();
            if (KeyBindings.WasPressed(GameAction.ToggleCharacter)) _inventory.ToggleCharacter();
            if (KeyBindings.WasPressed(GameAction.ToggleQuestLog)) _quests.ToggleLog();
            if (KeyBindings.WasPressed(GameAction.ToggleMap)) _map.ToggleWorldMap();
            for (int i = 0; i < SlotCount; i++)
                if (KeyBindings.WasPressed(KeyBindings.ActionButton(i))) UseSpell(_slots[i].SpellId);
        }

        /// <summary>Esc: closes the chat line or clears the target. False if nothing was there (the menu may open).</summary>
        public bool HandleEscape()
        {
            if (_chat.IsOpen || _chat.ClosedThisFrame)
            {
                _chat.Close();
                return true;
            }
            if (_inventory.IsHolding) return _inventory.HandleEscape();
            if (_loot.IsOpen)
            {
                _loot.Close();
                return true;
            }
            if (_vendor.IsOpen)
            {
                _vendor.Close();
                return true;
            }
            if (_map.HandleEscape() || _quests.HandleEscape() || _inventory.HandleEscape()) return true;
            if (!Client.Target.IsEmpty)
            {
                Client.SetTarget(EntityGuid.Empty);
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ frames

        private void UpdatePlayerFrame()
        {
            var v = Client.Self.Values;
            _playerName.text = Client.LocalName;
            _playerLevel.text = v.Level.ToString(CultureInfo.InvariantCulture);
            SetBar(_playerHealthFill, _playerHealthText, v.Health, v.MaxHealth, false);
            SetPower(_playerPower, _playerPowerFill, _playerPowerText, v);
            _playerFrame.EnableInClassList("combat", v.Has(UnitFlags.InCombat));
            _playerFrame.EnableInClassList("dead", v.IsDead);

            var stamina = network.LocalPlayer != null ? network.LocalPlayer.Stamina : null;
            if (stamina != null)
            {
                _staminaFill.style.width = Length.Percent(stamina.Fraction * 100f);
                _stamina.EnableInClassList("full", stamina.Value >= Stamina.Max);
                _stamina.EnableInClassList("low", !stamina.Sprinting && stamina.Value < Stamina.ToStart);
            }
        }

        private void UpdateTargetFrame()
        {
            var guid = Client.Target;
            if (guid != _shownTarget)
            {
                _shownTarget = guid;
                _targetAurasDirty = true;
            }
            if (guid.IsEmpty || !Client.TryGetUnit(guid, out var unit))
            {
                _targetFrame.AddToClassList("hidden");
                return;
            }
            _targetFrame.RemoveFromClassList("hidden");
            var v = unit.Values;
            var reaction = guid == Client.LocalGuid ? Reaction.Friendly : Client.GetReaction(guid);
            _targetFrame.EnableInClassList("hostile", reaction == Reaction.Hostile);
            _targetFrame.EnableInClassList("neutral", reaction == Reaction.Neutral);
            _targetFrame.EnableInClassList("friendly", reaction == Reaction.Friendly);
            _targetFrame.EnableInClassList("dead", v.IsDead);
            _targetFrame.EnableInClassList("combat", v.Has(UnitFlags.InCombat));
            _targetName.text = guid == Client.LocalGuid ? Client.LocalName
                : Client.World.TryGet(guid, out var entity) ? entity.Name : "";
            _targetLevel.text = v.Level.ToString(CultureInfo.InvariantCulture);
            SetBar(_targetHealthFill, _targetHealthText, v.Health, v.MaxHealth, true);
            SetPower(_targetPower, _targetPowerFill, _targetPowerText, v);
        }

        private static void SetBar(VisualElement fill, Label text, int value, int max, bool percent)
        {
            float fraction = max > 0 ? Mathf.Clamp01((float)value / max) : 0f;
            fill.style.width = Length.Percent(fraction * 100f);
            text.text = max <= 0 ? "" : percent ? Mathf.CeilToInt(fraction * 100f) + "%" : value + " / " + max;
        }

        private static void SetPower(VisualElement bar, VisualElement fill, Label text, UnitValues v)
        {
            bar.style.display = v.MaxPower > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            bar.EnableInClassList("rage", v.PowerType == PowerType.Rage);
            bar.EnableInClassList("energy", v.PowerType == PowerType.Energy);
            SetBar(fill, text, v.Power, v.MaxPower, false);
        }

        // ------------------------------------------------------------------ cast bars

        private void UpdateCastBars()
        {
            double now = Client.LocalTimeMs;
            var cast = Client.Self.Cast;
            if (cast != null && Client.Data.TryGetSpell(cast.SpellId, out var spell))
            {
                _playerCast.RemoveFromClassList("hidden");
                _playerCast.RemoveFromClassList("failed");
                _playerCastFill.style.width = Length.Percent(cast.Progress(now) * 100f);
                double left = System.Math.Max(0, cast.EndLocalMs - now) / 1000.0;
                _playerCastText.text = spell.Name + "  " + left.ToString("0.0", CultureInfo.InvariantCulture);
            }
            else if (Time.time > _playerCastFailedUntil)
            {
                _playerCast.AddToClassList("hidden");
            }

            UnitState target = null;
            if (!Client.Target.IsEmpty && Client.Target != Client.LocalGuid) Client.TryGetUnit(Client.Target, out target);
            var tcast = target?.Cast;
            if (tcast != null && Client.Data.TryGetSpell(tcast.SpellId, out var tspell))
            {
                _targetCast.RemoveFromClassList("hidden");
                _targetCastFill.style.width = Length.Percent(tcast.Progress(now) * 100f);
                _targetCastText.text = tspell.Name;
            }
            else
            {
                _targetCast.AddToClassList("hidden");
            }
        }

        // ------------------------------------------------------------------ action bar

        private void BuildActionBar()
        {
            _actionBar.Clear();
            for (int i = 0; i < SlotCount; i++)
            {
                var root = new VisualElement { pickingMode = PickingMode.Position };
                root.AddToClassList("action-button");
                root.AddToClassList("empty");
                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("icon");
                icon.Add(new Label { pickingMode = PickingMode.Ignore });
                icon.Q<Label>().AddToClassList("icon-letters");
                var shade = new VisualElement { pickingMode = PickingMode.Ignore };
                shade.AddToClassList("cooldown-shade");
                var cooldown = new Label { pickingMode = PickingMode.Ignore };
                cooldown.AddToClassList("cooldown-text");
                var key = new Label(KeyBindings.ShortcutText(KeyBindings.ActionButton(i))) { pickingMode = PickingMode.Ignore };
                key.AddToClassList("action-key");
                foreach (var ring in new[] { "glow-outer", "glow-inner" })
                {
                    var glow = new VisualElement { pickingMode = PickingMode.Ignore };
                    glow.AddToClassList(ring);
                    root.Add(glow);
                }
                root.Add(icon);
                root.Add(shade);
                root.Add(cooldown);
                root.Add(key);
                var slot = new Slot { Root = root, Icon = icon, Shade = shade, Cooldown = cooldown, Key = key };
                root.RegisterCallback<ClickEvent>(_ => UseSpell(slot.SpellId));
                root.RegisterCallback<PointerEnterEvent>(_ => ShowTooltip(slot.SpellId, root, 0));
                root.RegisterCallback<PointerLeaveEvent>(_ => HideTooltip());
                _slots[i] = slot;
                _actionBar.Add(root);
            }
        }

        /// <summary>Auto-attack first, then the class's spells in the order they are learned.</summary>
        private void RefreshActionBar()
        {
            var spells = new List<SpellInfo>();
            foreach (var id in Client.KnownSpells)
                if (Client.Data.TryGetSpell(id, out var s) && !s.Has(SpellFlags.Hidden)) spells.Add(s);
            spells.Sort((a, b) =>
            {
                if (a.IsAutoAttack != b.IsAutoAttack) return a.IsAutoAttack ? -1 : 1;
                int byLevel = a.LearnLevel.CompareTo(b.LearnLevel);
                return byLevel != 0 ? byLevel : a.Id.CompareTo(b.Id);
            });
            for (int i = 0; i < SlotCount; i++)
            {
                var slot = _slots[i];
                var spell = i < spells.Count ? spells[i] : null;
                slot.SpellId = spell?.Id ?? 0;
                slot.Root.EnableInClassList("empty", spell == null);
                if (spell != null) SpellIcons.Apply(slot.Icon, spell);
            }
        }

        private void UpdateActionBar()
        {
            var self = Client.Self;
            foreach (var slot in _slots)
            {
                if (slot.SpellId == 0 || !Client.Data.TryGetSpell(slot.SpellId, out var spell))
                {
                    slot.Shade.style.display = DisplayStyle.None;
                    slot.Cooldown.text = "";
                    slot.Root.EnableInClassList("ready", false);
                    slot.Root.EnableInClassList("busy", false);
                    continue;
                }
                double remaining = 0, duration = 0;
                bool global = false;
                if (Client.TryGetCooldown(spell.Id, out var cdLeft, out var cdTotal))
                {
                    remaining = cdLeft;
                    duration = cdTotal;
                }
                if (spell.GcdMs > 0 && !spell.IsAutoAttack && Client.GcdRemainingMs > remaining)
                {
                    remaining = Client.GcdRemainingMs;
                    duration = Client.GcdDurationMs;
                    global = true;
                }
                // The shade has a bright top edge, so it is hidden (not just zero high) when nothing is cooling down.
                slot.Shade.style.display = remaining > 0 && duration > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                slot.Shade.style.height = Length.Percent(duration > 0 ? (float)(remaining / duration) * 100f : 0f);
                // The global cooldown is a light sweep on every button; a spell's own cooldown is dark and counts down.
                slot.Shade.EnableInClassList("global", global);
                slot.Cooldown.text = !global && remaining > 0 ? Seconds(remaining) : "";
                slot.Root.EnableInClassList("counting", slot.Cooldown.text.Length > 0);

                bool unusable = spell.Cost > 0 && (self.Values.PowerType != spell.PowerType || self.Values.Power < spell.Cost);
                slot.Root.EnableInClassList("unusable", unusable || self.Values.IsDead);
                bool outOfRange = spell.Target == SpellTarget.Enemy && !Client.Target.IsEmpty && Client.CheckCast(spell.Id, Client.Target) == CastResult.OutOfRange;
                slot.Root.EnableInClassList("out-of-range", outOfRange);
                bool active = spell.IsAutoAttack ? self.Values.Has(UnitFlags.AutoAttacking) : self.Cast != null && self.Cast.SpellId == spell.Id;
                slot.Root.EnableInClassList("active", active);
                // Orange glow while busy (cooling down, global cooldown or casting), green when usable right now.
                bool busy = !spell.IsAutoAttack && (remaining > 0 || self.Cast != null);
                bool ready = !spell.IsAutoAttack && !busy && Client.CheckCast(spell.Id, Client.Target) == CastResult.Success;
                slot.Root.EnableInClassList("busy", busy);
                slot.Root.EnableInClassList("ready", ready);
            }
        }

        /// <summary>Uses a spell the way a key press does: checks what the client can, then asks the server.</summary>
        public void UseSpell(uint spellId)
        {
            if (spellId == 0 || !Client.Data.TryGetSpell(spellId, out var spell)) return;
            var self = Client.Self.Values;
            var target = Client.Target;

            if (spell.IsAutoAttack)
            {
                if (self.Has(UnitFlags.AutoAttacking))
                {
                    Client.StopAttack();
                    return;
                }
                if (target.IsEmpty) ShowError(CastResult.NoTarget);
                else if (!Client.CanAttack(target)) ShowError(CastResult.InvalidTarget);
                else Client.CastSpell(spellId, target);
                return;
            }

            switch (spell.Target)
            {
                case SpellTarget.Self:
                case SpellTarget.None:
                    target = Client.LocalGuid;
                    break;
                case SpellTarget.Friend:
                    if (target.IsEmpty || Client.GetReaction(target) != Reaction.Friendly) target = Client.LocalGuid;
                    break;
            }

            var result = Client.CheckCast(spellId, target);
            if (result == CastResult.Success && spell.CastTimeMs > 0 && Client.LocalMovement.IsMoving) result = CastResult.Moving;
            if (result != CastResult.Success)
            {
                ShowError(result);
                return;
            }
            Client.CastSpell(spellId, target);
            // Melee abilities start swinging too, like in classic MMOs.
            if (spell.Target == SpellTarget.Enemy && spell.MaxRange <= GameConstants.MeleeRange && !self.Has(UnitFlags.AutoAttacking))
                targeting.Attack(target);
        }

        // ------------------------------------------------------------------ auras

        private void UpdateAuras()
        {
            if (_selfAurasDirty)
            {
                _selfAurasDirty = false;
                RebuildAuras(_playerAuras, _playerAuraIcons, Client.Self, ownBuffs: true);
            }
            if (_targetAurasDirty)
            {
                _targetAurasDirty = false;
                UnitState target = null;
                if (!Client.Target.IsEmpty) Client.TryGetUnit(Client.Target, out target);
                RebuildAuras(_targetAuras, _targetAuraIcons, target, ownBuffs: false);
            }
            UpdateAuraTimes(_playerAuraIcons, Client.Self);
            if (!Client.Target.IsEmpty && Client.TryGetUnit(Client.Target, out var t)) UpdateAuraTimes(_targetAuraIcons, t);
        }

        private void RebuildAuras(VisualElement container, List<AuraIcon> icons, UnitState unit, bool ownBuffs)
        {
            container.Clear();
            icons.Clear();
            if (unit == null) return;
            foreach (var aura in unit.Auras)
            {
                var spell = Client.Data.GetSpell(aura.SpellId);
                var root = new VisualElement { pickingMode = PickingMode.Position };
                root.AddToClassList("aura");
                bool harmful = spell != null && spell.Has(SpellFlags.HarmfulAura);
                root.EnableInClassList("harmful", harmful);
                var icon = spell != null ? SpellIcons.Create(spell) : new VisualElement();
                var time = new Label { pickingMode = PickingMode.Ignore };
                time.AddToClassList("aura-time");
                var stacks = new Label(aura.Stacks > 1 ? aura.Stacks.ToString(CultureInfo.InvariantCulture) : "") { pickingMode = PickingMode.Ignore };
                stacks.AddToClassList("aura-stacks");
                root.Add(icon);
                root.Add(stacks);
                root.Add(time);
                uint spellId = aura.SpellId;
                root.RegisterCallback<PointerEnterEvent>(_ => ShowTooltip(spellId, root, 1));
                root.RegisterCallback<PointerLeaveEvent>(_ => HideTooltip());
                if (ownBuffs && !harmful)
                {
                    root.RegisterCallback<PointerDownEvent>(e =>
                    {
                        if (e.button == 1) Client.CancelAura(spellId);
                    });
                }
                container.Add(root);
                icons.Add(new AuraIcon { Root = root, Time = time, Stacks = stacks, Icon = icon });
            }
        }

        private void UpdateAuraTimes(List<AuraIcon> icons, UnitState unit)
        {
            double now = Client.LocalTimeMs;
            for (int i = 0; i < icons.Count && i < unit.Auras.Count; i++)
                icons[i].Time.text = unit.Auras[i].IsPermanent ? "" : Seconds(unit.Auras[i].RemainingMs(now));
        }

        // ------------------------------------------------------------------ experience

        private void UpdateXp()
        {
            int level = Client.Self.Values.Level;
            int needed = Client.Data.XpToNext(level);
            int xp = Client.Stats.Xp;
            _xpFill.style.width = Length.Percent(needed > 0 ? Mathf.Clamp01((float)xp / needed) * 100f : 100f);
            _xpText.text = needed > 0 ? Loc.T("hud.xp", level, xp, needed) : Loc.T("hud.xpMax", level);
        }

        // ------------------------------------------------------------------ events

        private void OnUnitChanged(EntityGuid guid, UnitFieldMask mask)
        {
            if (guid != Client.LocalGuid || (mask & UnitFieldMask.Level) == 0) return;
            int level = Client.Self.Values.Level;
            if (_lastLevel > 0 && level > _lastLevel) ShowNotice(Loc.T("hud.levelUp", level));
            _lastLevel = level;
        }

        private void OnSpellFailed(SpellFailed failed)
        {
            if (failed.Caster != Client.LocalGuid) return;
            if (failed.Result == CastResult.Interrupted || failed.Result == CastResult.Stunned)
            {
                _playerCast.RemoveFromClassList("hidden");
                _playerCast.AddToClassList("failed");
                _playerCastFill.style.width = Length.Percent(100f);
                _playerCastText.text = Loc.T("cast.interrupted");
                _playerCastFailedUntil = Time.time + 0.6f;
            }
            if (failed.Result != CastResult.Cancelled && failed.Result != CastResult.Success) ShowError(failed.Result);
        }

        private void OnCombatEvent(CombatEvent e)
        {
            var world = network.WorldView;
            bool heal = e.Has(CombatFlags.Heal);
            if (!heal && e.Amount > 0 && world.TryGetView(e.Target, out var hitView)) hitView.PlayHit();
            if (!heal && world.TryGetView(e.Source, out var sourceView) && (e.SpellId == 0 || IsMeleeSpell(e.SpellId))) sourceView.PlayLunge();

            var me = Client.LocalGuid;
            bool mine = e.Source == me;
            bool atMe = e.Target == me;
            if (!mine && !atMe) return;
            var anchor = atMe ? me : e.Target;

            string text;
            string css;
            if (e.Has(CombatFlags.Miss)) { text = Loc.T("combat.miss"); css = "miss"; }
            else if (e.Has(CombatFlags.Dodge)) { text = Loc.T("combat.dodge"); css = "miss"; }
            else if (e.Has(CombatFlags.Parry)) { text = Loc.T("combat.parry"); css = "miss"; }
            else if (e.Has(CombatFlags.Evade)) { text = Loc.T("combat.evade"); css = "miss"; }
            else if (e.Has(CombatFlags.Immune)) { text = Loc.T("combat.immune"); css = "miss"; }
            else if (heal)
            {
                text = "+" + e.Amount;
                css = "heal";
            }
            else
            {
                text = e.Amount > 0 ? e.Amount.ToString(CultureInfo.InvariantCulture) : Loc.T("combat.absorbed");
                if (e.Absorbed > 0 && e.Amount > 0) text += " (" + e.Absorbed + " absorbed)";
                css = atMe ? "taken" : e.SpellId != 0 ? "spell" : null;
            }
            if (e.Has(CombatFlags.Crit)) css = css == null ? "crit" : css + " crit";
            _overlay.Show(anchor, text, css);
        }

        private bool IsMeleeSpell(uint spellId) => Client.Data.TryGetSpell(spellId, out var s) && SpellEffects.IsMelee(s);

        private void OnExperience(int amount, EntityGuid victim)
        {
            _overlay.Show(Client.LocalGuid, "+" + amount + " XP", "xp");
            string from = !victim.IsEmpty && Client.World.TryGet(victim, out var entity) ? Loc.T("chat.xpFrom", entity.Name) : "";
            _chat.AddLine(Loc.T("chat.xpGained", amount, from), "combat");
        }

        // ------------------------------------------------------------------ items and quests

        private void OnMoneyChanged(long delta, ReceiveSource source)
        {
            _inventory.Refresh();
            if (delta <= 0 || source == ReceiveSource.Quest) return;
            string money = Money.Format(delta);
            _chat.AddLine(Loc.T(source == ReceiveSource.Loot ? "chat.lootMoney" : "chat.receiveMoney", money), "loot");
        }

        private void OnItemReceived(ItemReceived received)
        {
            string item = ItemTooltip.Describe(Client.Data, received.ItemId, received.Count);
            switch (received.Source)
            {
                case ReceiveSource.Loot: _chat.AddLine(Loc.T("chat.receiveLoot", item), "loot"); break;
                case ReceiveSource.Vendor: _chat.AddLine(Loc.T("chat.purchase", item), "loot"); break;
                default: _chat.AddLine(Loc.T("chat.receiveItem", item), "loot"); break;
            }
        }

        private void OnQuestProgressed(uint questId, int objective, int count)
        {
            var quest = Client.Data.GetQuest(questId);
            if (quest == null || objective >= quest.ObjectiveList.Count) return;
            var text = QuestUi.ObjectiveLine(quest.ObjectiveList[objective], count);
            _chat.AddLine(text, "quest");
            ShowNotice(text, small: true);
        }

        private void OnQuestStateChanged(uint questId, QuestLogState state)
        {
            var quest = Client.Data.GetQuest(questId);
            if (quest == null) return;
            switch (state)
            {
                case QuestLogState.Active:
                    if (Client.Quests.Find(questId) != null) _chat.AddLine(Loc.T("chat.questAccepted", quest.Title), "quest");
                    break;
                case QuestLogState.Complete:
                    _chat.AddLine(Loc.T("quest.completeSuffix", quest.Title), "quest");
                    ShowNotice(Loc.T("quest.completeSuffix", quest.Title), small: true);
                    break;
                case QuestLogState.Removed:
                    if (!Client.Quests.IsCompleted(questId)) _chat.AddLine(Loc.T("chat.questAbandoned", quest.Title), "quest");
                    break;
            }
        }

        private void OnQuestCompleted(QuestCompleted completed)
        {
            var quest = Client.Data.GetQuest(completed.QuestId);
            _chat.AddLine(Loc.T("chat.questCompleted", quest != null ? quest.Title : Loc.T("quest.generic")), "quest");
            if (completed.Xp > 0) _chat.AddLine(Loc.T("chat.experienceGained", completed.Xp), "quest");
            if (completed.Money > 0) _chat.AddLine(Loc.T("chat.received", Money.Format(completed.Money)), "quest");
        }

        private static string ActionErrorText(ActionError error, int value)
        {
            switch (error)
            {
                case ActionError.InventoryFull: return Loc.T("error.inventoryFull");
                case ActionError.ItemNotFound: return Loc.T("error.itemNotFound");
                case ActionError.CantEquipThere: return Loc.T("error.cantEquipThere");
                case ActionError.LevelTooLow: return Loc.T("error.levelTooLow", value);
                case ActionError.WrongClass: return Loc.T("error.wrongClass");
                case ActionError.BagNotEmpty: return Loc.T("error.bagNotEmpty");
                case ActionError.BagInItself: return Loc.T("error.bagInItself");
                case ActionError.NotABag: return Loc.T("error.notABag");
                case ActionError.NotEnoughMoney: return Loc.T("error.notEnoughMoney");
                case ActionError.CantSellItem: return Loc.T("error.cantSellItem");
                case ActionError.TooFarAway: return Loc.T("error.tooFarAway");
                case ActionError.NotUsable: return Loc.T("error.notUsable");
                case ActionError.TwoHandEquipped: return Loc.T("error.twoHandEquipped");
                case ActionError.LootNotYours: return Loc.T("error.lootNotYours");
                case ActionError.QuestLogFull: return Loc.T("error.questLogFull");
                case ActionError.QuestNotAvailable: return Loc.T("error.questNotAvailable");
                case ActionError.QuestNotComplete: return Loc.T("error.questNotComplete");
                case ActionError.ChooseReward: return Loc.T("error.chooseReward");
                case ActionError.CantInteract: return Loc.T("error.cantInteract");
                case ActionError.NoRoomForReward: return Loc.T("error.noRoomForReward");
                case ActionError.CantDestroyItem: return Loc.T("error.cantDestroyItem");
                default: return Loc.T("error.default");
            }
        }

        private void OnChat(ChatMessage message)
        {
            switch (message.Channel)
            {
                case ChatChannel.Error:
                    ShowError(message.Text);
                    break;
                case ChatChannel.Say:
                    _chat.AddLine(Loc.T("chat.says", message.SenderName, message.Text));
                    break;
                default:
                    _chat.AddLine(message.Text, "system");
                    break;
            }
        }

        // ------------------------------------------------------------------ texts and tooltips

        public void ShowError(CastResult result)
        {
            var text = ErrorText(result);
            if (text != null) ShowError(text);
        }

        public void ShowError(string text)
        {
            _sounds?.Error();
            _errorText.text = text;
            _errorUntil = Time.time + ErrorSeconds;
        }

        private void ShowNotice(string text, bool small = false)
        {
            _noticeText.text = text;
            _noticeText.EnableInClassList("small", small);
            _noticeUntil = Time.time + NoticeSeconds;
        }

        private void UpdateTexts()
        {
            float now = Time.time;
            _errorText.style.opacity = Mathf.Clamp01((_errorUntil - now) / 0.5f);
            _noticeText.style.opacity = Mathf.Clamp01((_noticeUntil - now) / 0.8f);
        }

        private string ErrorText(CastResult result)
        {
            switch (result)
            {
                case CastResult.Interrupted: return Loc.T("cast.interrupted");
                case CastResult.NotKnown: return Loc.T("cast.notKnown");
                case CastResult.OnCooldown: return Loc.T("cast.onCooldown");
                case CastResult.NotReady: return Loc.T("cast.notReady");
                case CastResult.NoPower:
                    return Loc.T(Client.Self.Values.PowerType == PowerType.Rage ? "cast.noRage"
                        : Client.Self.Values.PowerType == PowerType.Energy ? "cast.noEnergy" : "cast.noMana");
                case CastResult.NoTarget: return Loc.T("cast.noTarget");
                case CastResult.InvalidTarget: return Loc.T("cast.invalidTarget");
                case CastResult.OutOfRange: return Loc.T("cast.outOfRange");
                case CastResult.TooClose: return Loc.T("cast.tooClose");
                case CastResult.NotInLineOfSight: return Loc.T("cast.notInLineOfSight");
                case CastResult.NotFacing: return Loc.T("cast.notFacing");
                case CastResult.Moving: return Loc.T("cast.moving");
                case CastResult.CasterDead: return Loc.T("cast.casterDead");
                case CastResult.Stunned: return Loc.T("cast.stunned");
                case CastResult.AlreadyCasting: return Loc.T("cast.alreadyCasting");
                case CastResult.TargetDead: return Loc.T("cast.targetDead");
                case CastResult.InCombat: return Loc.T("cast.inCombat");
                default: return null;
            }
        }

        private void ShowTooltip(uint spellId, VisualElement anchor, int placement)
        {
            if (spellId == 0 || !Client.Data.TryGetSpell(spellId, out var spell)) return;
            _tooltipTitle.text = spell.Name;
            var meta = new List<string>();
            if (spell.Cost > 0) meta.Add(spell.Cost + " " + spell.PowerType.ToString().ToLowerInvariant());
            meta.Add(spell.IsAutoAttack ? Loc.T("spell.melee") : spell.CastTimeMs > 0 ? Loc.T("spell.castTime", (spell.CastTimeMs / 1000f).ToString("0.#", CultureInfo.InvariantCulture)) : Loc.T("spell.instant"));
            if (spell.MaxRange > GameConstants.MeleeRange) meta.Add(Loc.T("spell.range", spell.MaxRange.ToString("0", CultureInfo.InvariantCulture)));
            else if (spell.MaxRange > 0) meta.Add(Loc.T("spell.meleeRange"));
            if (spell.CooldownMs > 0) meta.Add(Loc.T("spell.cooldown", Seconds(spell.CooldownMs)));
            _tooltipMeta.text = string.Join("   ", meta);
            _tooltipBody.text = spell.Description;
            _tooltip.RemoveFromClassList("hidden");

            var bound = anchor.worldBound;
            var root = _hud.layout;
            _tooltip.style.left = Mathf.Clamp(bound.x + bound.width * 0.5f - 160f, 8f, Mathf.Max(8f, root.width - 330f));
            if (placement == 0)
            {
                _tooltip.style.top = StyleKeyword.Auto;
                _tooltip.style.bottom = root.height - bound.y + 8f;
            }
            else
            {
                _tooltip.style.bottom = StyleKeyword.Auto;
                _tooltip.style.top = bound.yMax + 18f;
            }
        }

        private void HideTooltip() => _tooltip.AddToClassList("hidden");

        private static string Seconds(double ms)
        {
            double s = ms / 1000.0;
            if (s >= 90) return Mathf.CeilToInt((float)(s / 60)) + "m";
            return s >= 10 ? Mathf.CeilToInt((float)s) + "s" : s.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
