using Madlands.ClientCore;
using Madlands.Shared;
using Madlands.Shared.Game;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// Scripted play for automated verification runs: sends the -exec chat lines (usually GM
    /// commands) after entering the world, and with -autofight targets the nearest enemy, faces it
    /// and fights with the action bar's spells. Does nothing in normal play.
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
            if (settings == null || (settings.Exec.Length == 0 && !settings.AutoFight)) return;
            if (!network.InWorld)
            {
                _enteredAt = -1f;
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

            if (settings.AutoFight && Time.time >= _nextFightAt)
            {
                _nextFightAt = Time.time + 0.35f;
                Fight();
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
