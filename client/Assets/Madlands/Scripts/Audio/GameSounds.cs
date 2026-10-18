using Madlands.ClientCore;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Net;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// Turns game events into sounds: swings and hits, spells by school, loot and money, quest and
    /// level jingles, window sounds, and the music and ambience of the area you are in (from the
    /// map's <see cref="MapArea"/>s). Footsteps are played by each <see cref="UnitView"/>.
    /// </summary>
    public sealed class GameSounds
    {
        private readonly GameClient _client;
        private readonly WorldView _world;
        private byte _lastLevel;
        private bool _wasDead;
        private float _errorAt = -10f;

        private static SoundSystem Sound => SoundSystem.Instance;

        public GameSounds(GameClient client, WorldView world)
        {
            _client = client;
            _world = world;
            client.CombatEventReceived += OnCombatEvent;
            client.SpellLaunched += OnSpellLaunched;
            client.CastStarted += OnCastStarted;
            client.MoneyChanged += (delta, source) =>
            {
                if (delta > 0 && (source == ReceiveSource.Loot || source == ReceiveSource.Vendor)) Sound.Play("coins", 0.7f);
                else if (delta < 0 && source == ReceiveSource.Vendor) Sound.Play("coins", 0.5f);
            };
            client.ItemReceived += received =>
            {
                if (received.Source == ReceiveSource.Loot) Sound.Play("loot_item", 0.7f, 0.05f);
            };
            client.QuestStateChanged += OnQuestStateChanged;
            client.QuestCompleted += _ => Sound.Play("jingle_quest_complete", 0.8f);
            client.UnitChanged += OnUnitChanged;
            GameWindow.Toggled += OnWindowToggled;
        }

        /// <summary>Music and ambience follow the area you stand in.</summary>
        public void Update()
        {
            var map = MapInfo.Current;
            if (map == null || _client.State != ClientState.InWorld) return;
            var me = _client.LocalMovement.Position;
            string music = map.music, ambience = map.ambience;
            if (map.FindArea(me.X, me.Z, out var area))
            {
                if (!string.IsNullOrEmpty(area.music)) music = area.music;
                if (!string.IsNullOrEmpty(area.ambience)) ambience = area.ambience;
            }
            Sound.SetMusic(string.IsNullOrEmpty(music) ? null : music);
            Sound.SetAmbience(string.IsNullOrEmpty(ambience) ? null : ambience);
        }

        public void Error()
        {
            if (Time.time - _errorAt < 0.6f) return;
            _errorAt = Time.time;
            Sound.Play("ui_error", 0.5f);
        }

        private void OnCombatEvent(CombatEvent e)
        {
            if (!_world.TryGetView(e.Target, out var target)) return;
            bool hasSource = _world.TryGetView(e.Source, out var source);
            SpellInfo spell = null;
            if (e.SpellId != 0) _client.Data.TryGetSpell(e.SpellId, out spell);
            bool melee = e.SpellId == 0 || spell != null && SpellEffects.IsMelee(spell);

            if (e.Has(CombatFlags.Heal))
            {
                if (!e.Has(CombatFlags.Periodic)) Sound.PlayAt("heal", target.ChestPosition, 0.7f);
                return;
            }
            if (melee && hasSource && !e.Has(CombatFlags.Periodic))
                Sound.PlayAt(e.Has(CombatFlags.Crit) ? "swing_heavy" : "swing", source.ChestPosition, 0.6f, 0.1f);
            if (e.Has(CombatFlags.Miss) || e.Has(CombatFlags.Dodge) || e.Has(CombatFlags.Evade) || e.Has(CombatFlags.Immune)) return;
            if (e.Has(CombatFlags.Parry))
            {
                Sound.PlayAt("hit_metal", target.ChestPosition, 0.7f);
                return;
            }
            if (e.Has(CombatFlags.Absorb) && e.Amount == 0)
            {
                Sound.PlayAt("block", target.ChestPosition, 0.6f);
                return;
            }
            if (melee)
            {
                bool wooden = target.DisplayId == EntityVisuals.DummyDisplay;
                string hit = wooden ? "hit_wood" : e.Has(CombatFlags.Crit) ? "hit_flesh_heavy" : "hit_flesh";
                Sound.PlayAt(hit, target.ChestPosition, e.Has(CombatFlags.Periodic) ? 0.3f : 0.8f);
            }
            else if (!e.Has(CombatFlags.Periodic))
                Sound.PlayAt("impact_" + SchoolName(e.School), target.ChestPosition, 0.8f);
            if (e.Has(CombatFlags.KillingBlow)) Sound.PlayAt("fall", target.transform.position, 0.8f);
        }

        private void OnSpellLaunched(SpellGo go)
        {
            if (!_client.Data.TryGetSpell(go.SpellId, out var spell) || !_world.TryGetView(go.Caster, out var caster)) return;
            if ((spell.Flags & (SpellFlags.AutoAttack | SpellFlags.NotInCombat)) != 0 || SpellEffects.IsMelee(spell)) return;
            if (spell.School == SpellSchool.Physical)
            {
                Sound.PlayAt("swing_heavy", caster.ChestPosition, 0.7f);
                return;
            }
            Sound.PlayAt("release_" + SchoolName(spell.School), caster.ChestPosition, 0.8f);
        }

        private void OnCastStarted(EntityGuid caster, CastInfo cast)
        {
            if (!_client.Data.TryGetSpell(cast.SpellId, out var spell) || !_world.TryGetView(caster, out var view)) return;
            if (spell.School == SpellSchool.Physical || (spell.Flags & SpellFlags.NotInCombat) != 0) return;
            Sound.PlayAt("cast_" + SchoolName(spell.School), view.ChestPosition, 0.6f, 0.03f);
        }

        private void OnQuestStateChanged(uint questId, QuestLogState state)
        {
            if (state == QuestLogState.Active && _client.Quests.Find(questId) != null) Sound.Play("jingle_quest_accept", 0.7f);
            else if (state == QuestLogState.Complete) Sound.Play("jingle_objective", 0.7f);
        }

        private void OnUnitChanged(EntityGuid guid, UnitFieldMask mask)
        {
            if (guid != _client.LocalGuid) return;
            var values = _client.Self.Values;
            if ((mask & UnitFieldMask.Level) != 0)
            {
                if (_lastLevel > 0 && values.Level > _lastLevel) Sound.Play("jingle_level_up", 0.9f);
                _lastLevel = values.Level;
            }
            if (values.IsDead != _wasDead)
            {
                if (values.IsDead) Sound.Play("jingle_death", 0.8f);
                _wasDead = values.IsDead;
            }
        }

        private static void OnWindowToggled(string kind, bool open)
        {
            switch (kind)
            {
                case "bags": Sound.Play(open ? "bag_open" : "bag_close", 0.6f); break;
                case "questlog": Sound.Play(open ? "book_open" : "book_close", 0.6f); break;
                case "character": Sound.Play("cloth", 0.6f); break;
                case "dialog":
                case "worldmap":
                    if (open) Sound.Play("page", 0.5f);
                    break;
                case "loot":
                    if (open) Sound.Play("bag_open", 0.5f);
                    break;
                default: Sound.Play(open ? "ui_open" : "ui_close", 0.4f); break;
            }
        }

        public static string SchoolName(SpellSchool school)
        {
            switch (school)
            {
                case SpellSchool.Fire: return "fire";
                case SpellSchool.Frost: return "frost";
                case SpellSchool.Holy: return "holy";
                case SpellSchool.Nature: return "nature";
                case SpellSchool.Shadow: return "shadow";
                default: return "arcane";
            }
        }

        public void Dispose()
        {
            _client.CombatEventReceived -= OnCombatEvent;
            _client.SpellLaunched -= OnSpellLaunched;
            _client.CastStarted -= OnCastStarted;
            _client.QuestStateChanged -= OnQuestStateChanged;
            _client.UnitChanged -= OnUnitChanged;
            GameWindow.Toggled -= OnWindowToggled;
        }
    }
}
