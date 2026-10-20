using Madlands.ClientCore;
using Madlands.Shared;
using Madlands.Shared.Game;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// Scripted play for automated verification runs: sends the -exec chat lines (usually GM
    /// commands) after entering the world, and with -autofight targets the nearest enemy, faces it
    /// and fights with the action bar's spells. -open opens windows, -talkto talks to an NPC (and
    /// -quest picks one of its quests), -loot opens the loot of corpses in reach. Does nothing in
    /// normal play.
    /// </summary>
    public sealed class AutoPlay : MonoBehaviour
    {
        private const float FightRange = 40f;

        [SerializeField] private NetworkController network;
        [SerializeField] private HudController hud;
        [SerializeField] private TargetingController targeting;

        private float _enteredAt = -1f;
        private int _execIndex;
        private float _nextExecAt;
        private float _nextFightAt;
        private int _spellCursor;
        private bool _windowsOpened;
        private int _equipped;
        private int _pressed;
        private bool _faced;
        private float _nextPressAt;
        private bool _talked;
        private float _nextTalkAt;
        private float _nextGatherAt;
        private EntityGuid _lootedCorpse;

        private GameClient Client => network.Client;

        private void Awake()
        {
            if (network == null) network = GetComponent<NetworkController>();
            if (hud == null) hud = GetComponent<HudController>();
            if (targeting == null) targeting = GetComponent<TargetingController>();
        }

        private void Update()
        {
            var settings = network.Settings;
            if (settings == null || (settings.Exec.Length == 0 && !settings.AutoFight && settings.OpenWindows.Length == 0
                                     && settings.TalkTo == null && !settings.AutoLoot && !settings.AutoGather && settings.TooltipItem == 0
                                     && settings.Equip.Length == 0 && settings.BarItems.Length == 0 && settings.PressBar.Length == 0 && settings.FaceYaw == null)) return;
            if (!network.InWorld)
            {
                _enteredAt = -1f;
                _windowsOpened = _talked = _faced = false;
                _equipped = _pressed = 0;
                return;
            }
            if (_enteredAt < 0f)
            {
                _enteredAt = Time.time;
                _execIndex = 0;
                _nextExecAt = Time.time + 1f;
            }

            if (_execIndex < settings.Exec.Length || Time.time < _nextExecAt)
            {
                // Each line gets time to take effect (teleports, spawns) before the next one, and before fighting.
                if (Time.time < _nextExecAt) return;
                Client.SendChat(settings.Exec[_execIndex++].Trim());
                _nextExecAt = Time.time + 0.7f;
                return;
            }

            if (_equipped < settings.Equip.Length) EquipNext(settings.Equip[_equipped]);
            if (settings.FaceYaw != null && !_faced && network.LocalPlayer != null)
            {
                _faced = true;
                float yaw = settings.FaceYaw.Value * Mathf.Deg2Rad;
                network.LocalPlayer.FaceTowards(network.LocalPlayer.transform.position + new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)));
            }
            if (!_windowsOpened && Time.time > _enteredAt + 1.5f)
            {
                _windowsOpened = true;
                foreach (var window in settings.OpenWindows) hud.OpenWindow(window);
                foreach (var (button, item) in settings.BarItems) hud.PutItemOnBar(button - 1, item);
                _nextPressAt = Time.time + 1f;
            }
            if (_windowsOpened && _pressed < settings.PressBar.Length && Time.time >= _nextPressAt)
            {
                hud.PressButton(settings.PressBar[_pressed++] - 1);
                _nextPressAt = Time.time + 1f;
            }
            if (settings.TalkTo != null && !_talked && Time.time >= _nextTalkAt) TalkTo(settings);
            if (settings.TooltipItem != 0 && _windowsOpened && Time.time > _enteredAt + 3f) hud.ShowItemTooltip(settings.TooltipItem);
            if (settings.AutoLoot) LootNearby();
            if (settings.AutoGather && Time.time >= _nextGatherAt) GatherNearby();

            if (settings.AutoFight && Time.time >= _nextFightAt)
            {
                _nextFightAt = Time.time + 0.35f;
                Fight();
            }
        }

        /// <summary>Equips one -equip item once it is in the bags (the -exec lines usually put it there).</summary>
        private void EquipNext(uint itemId)
        {
            foreach (var (pos, item) in Client.Inventory.All)
            {
                if (item.ItemId != itemId) continue;
                if (!pos.IsEquipment) Client.AutoEquip(pos);
                _equipped++;
                return;
            }
        }

        /// <summary>Talks to the named NPC (retrying until its dialog opens), then opens the -quest page.</summary>
        private void TalkTo(ClientSettings settings)
        {
            _nextTalkAt = Time.time + 1f;
            if (hud.Quests.DialogOpen)
            {
                _talked = true;
                return;
            }
            foreach (var entity in Client.World.Entities)
            {
                if (entity.Name != settings.TalkTo) continue;
                Client.SetTarget(entity.Guid);
                if (settings.QuestId != 0) Client.GossipOpened += OpenQuestOnce;
                Client.Interact(entity.Guid);
                return;
            }
        }

        private void OpenQuestOnce(Shared.Net.GossipMenu menu)
        {
            Client.GossipOpened -= OpenQuestOnce;
            hud.Quests.ShowQuest(network.Settings.QuestId);
        }

        /// <summary>Right-clicks the nearest gatherable object in reach, once a second.</summary>
        private void GatherNearby()
        {
            _nextGatherAt = Time.time + 1f;
            if (network.LocalPlayer == null) return;
            foreach (var entity in Client.World.Entities)
            {
                if (!entity.IsObject || !(entity.View is GameObject go)) continue;
                if (Vector3.Distance(go.transform.position, network.LocalPlayer.transform.position) > GameConstants.InteractRange) continue;
                Client.Interact(entity.Guid);
                return;
            }
        }

        private void LootNearby()
        {
            if (Client.Loot.IsOpen || network.LocalPlayer == null) return;
            foreach (var entity in Client.World.Entities)
            {
                if (entity.Guid == _lootedCorpse || !Client.IsLootable(entity.Guid) || !(entity.View is GameObject go)) continue;
                if (Vector3.Distance(go.transform.position, network.LocalPlayer.transform.position) > GameConstants.InteractRange) continue;
                _lootedCorpse = entity.Guid;
                Client.OpenLoot(entity.Guid);
                return;
            }
        }

        private void Fight()
        {
            var player = network.LocalPlayer;
            if (player == null || Client.Self.Values.IsDead) return;
            if (Client.Target.IsEmpty || !Client.CanAttack(Client.Target)) Client.SetTarget(NearestEnemy(player.transform.position));
            var target = Client.Target;
            if (target.IsEmpty || !network.WorldView.TryGetView(target, out var view)) return;

            player.FaceTowards(view.transform.position);
            if (Vector3.Distance(player.transform.position, view.transform.position) <= GameConstants.MeleeRange)
                targeting.Attack(target);
            if (Client.Self.Cast != null || Client.GcdRemainingMs > 0) return;

            // Round-robin over the usable spells, so the screenshots show a bit of everything.
            var known = Client.KnownSpells;
            for (int i = 0; i < known.Count; i++)
            {
                uint id = known[(_spellCursor + i) % known.Count];
                if (!Client.Data.TryGetSpell(id, out var spell) || spell.IsAutoAttack || spell.Has(SpellFlags.Hidden)) continue;
                var spellTarget = spell.Target == SpellTarget.Enemy ? target : Client.LocalGuid;
                if (Client.CheckCast(id, spellTarget) != CastResult.Success) continue;
                hud.UseSpell(id);
                _spellCursor = (_spellCursor + i + 1) % known.Count;
                return;
            }
        }

        private EntityGuid NearestEnemy(Vector3 from)
        {
            var best = EntityGuid.Empty;
            float bestDistance = FightRange;
            foreach (var entity in Client.World.Entities)
            {
                if (entity.Unit == null || entity.Guid.Type != EntityType.Creature || !Client.CanAttack(entity.Guid)) continue;
                if (!(entity.View is GameObject go)) continue;
                float d = Vector3.Distance(from, go.transform.position);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = entity.Guid;
                }
            }
            return best;
        }
    }
}
