using System.Collections.Generic;
using Madlands.ClientCore;
using Madlands.Shared;
using Madlands.Shared.Game;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Madlands.Client
{
    /// <summary>
    /// WoW-style targeting: left-click a unit to select it, right-click to select and attack (or talk
    /// to a friendly NPC, or loot a corpse; shift-right-click takes everything), Tab cycles through
    /// enemies in front of you. A click is a short press without dragging (dragging
    /// turns the camera). The camera locks the cursor at the screen center while a button is held,
    /// so the click is judged by how far the mouse moved and picked where the press started.
    /// A ring under the target shows its reaction color.
    /// </summary>
    public sealed class TargetingController : MonoBehaviour
    {
        private const float ClickMaxPixels = 6f;
        private const float ClickMaxSeconds = 0.35f;
        private const float TabRange = 45f;

        [SerializeField] private NetworkController network;

        private Vector2 _pressPosition;
        private float _pressTime;
        private float _movedPixels;
        private bool _pressing;
        private Transform _ring;
        private Color _ringColor;
        private readonly List<EntityGuid> _tabOrder = new List<EntityGuid>();
        private int _tabIndex;

        private GameClient Client => network.Client;

        private void Awake()
        {
            if (network == null) network = GetComponent<NetworkController>();
        }

        private void Update()
        {
            if (!network.InWorld || InputGate.Blocked)
            {
                _pressing = false;
                return;
            }

            var mouse = Mouse.current;
            if (mouse != null) HandleMouse(mouse);
            var keyboard = Keyboard.current;
            if (keyboard != null && !InputGate.KeyboardBlocked && keyboard.tabKey.wasPressedThisFrame) CycleEnemies();
        }

        private void HandleMouse(Mouse mouse)
        {
            bool down = mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame;
            if (down && !_pressing)
            {
                _pressing = true;
                _pressPosition = mouse.position.ReadValue();
                _pressTime = Time.unscaledTime;
                _movedPixels = 0f;
            }
            else if (_pressing)
            {
                _movedPixels += mouse.delta.ReadValue().magnitude;
            }
            bool leftUp = mouse.leftButton.wasReleasedThisFrame;
            bool rightUp = mouse.rightButton.wasReleasedThisFrame;
            if (!_pressing || !(leftUp || rightUp)) return;
            if (mouse.leftButton.isPressed || mouse.rightButton.isPressed) return; // wait for both buttons
            _pressing = false;

            bool click = _movedPixels <= ClickMaxPixels && Time.unscaledTime - _pressTime <= ClickMaxSeconds;
            if (!click || UiPointer.IsOverInteractive(_pressPosition)) return;

            var unit = PickUnit(_pressPosition);
            if (unit == null) return;
            Client.SetTarget(unit.Guid);
            if (rightUp) Use(unit.Guid);
        }

        /// <summary>Right-click on a unit: loot a corpse, attack an enemy, or talk to an NPC.</summary>
        private void Use(EntityGuid guid)
        {
            if (!Client.TryGetUnit(guid, out var unit) || guid == Client.LocalGuid) return;
            if (unit.Values.IsDead)
            {
                if (!Client.IsLootable(guid)) return;
                Client.OpenLoot(guid);
                var keyboard = Keyboard.current;
                if (keyboard != null && keyboard.shiftKey.isPressed) Client.TakeLoot(Shared.Net.LootTake.AllSlots);
                return;
            }
            if (Client.CanAttack(guid)) Attack(guid);
            else if (unit.Values.NpcFlags != NpcFlags.None) Client.Interact(guid);
        }

        /// <summary>Starts auto-attack on <paramref name="guid"/> (the Attack spell).</summary>
        public void Attack(EntityGuid guid)
        {
            foreach (var id in Client.KnownSpells)
            {
                if (!Client.Data.TryGetSpell(id, out var spell) || !spell.IsAutoAttack) continue;
                if (!Client.Self.Values.Has(UnitFlags.AutoAttacking)) Client.CastSpell(id, guid);
                return;
            }
        }

        private static UnitView PickUnit(Vector2 screenPosition)
        {
            var cam = Camera.main;
            if (cam == null) return null;
            var ray = cam.ScreenPointToRay(screenPosition);
            // Nearest hit of anything (terrain included, so units behind hills cannot be clicked).
            if (!Physics.Raycast(ray, out var hit, 300f, ~0, QueryTriggerInteraction.Collide)) return null;
            return hit.collider.GetComponentInParent<UnitView>();
        }

        /// <summary>Nearest living enemies in front of the camera, closest first; each press moves to the next.</summary>
        private void CycleEnemies()
        {
            var cam = Camera.main;
            if (cam == null || network.LocalPlayer == null) return;
            var origin = network.LocalPlayer.transform.position;
            var candidates = new List<(EntityGuid Guid, float Distance)>();
            foreach (var entity in Client.World.Entities)
            {
                if (entity.Unit == null || !Client.CanAttack(entity.Guid) || !(entity.View is GameObject go)) continue;
                var p = go.transform.position;
                float distance = Vector3.Distance(origin, p);
                if (distance > TabRange) continue;
                var viewport = cam.WorldToViewportPoint(p + Vector3.up);
                if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f) continue;
                candidates.Add((entity.Guid, distance));
            }
            if (candidates.Count == 0) return;
            candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            bool sameSet = candidates.Count == _tabOrder.Count;
            for (int i = 0; sameSet && i < candidates.Count; i++) sameSet = _tabOrder.Contains(candidates[i].Guid);
            if (!sameSet)
            {
                _tabOrder.Clear();
                foreach (var c in candidates) _tabOrder.Add(c.Guid);
                _tabIndex = _tabOrder[0] == Client.Target && _tabOrder.Count > 1 ? 1 : 0;
            }
            else
            {
                _tabIndex = (_tabOrder.IndexOf(Client.Target) + 1) % _tabOrder.Count;
            }
            Client.SetTarget(_tabOrder[_tabIndex]);
        }

        private void LateUpdate()
        {
            UnitView view = null;
            bool show = network.InWorld && !Client.Target.IsEmpty && network.WorldView.TryGetView(Client.Target, out view);
            if (!show)
            {
                if (_ring != null) _ring.gameObject.SetActive(false);
                return;
            }
            var reaction = view.Guid == Client.LocalGuid ? Reaction.Friendly : Client.GetReaction(view.Guid);
            var color = reaction == Reaction.Hostile ? new Color(0.95f, 0.2f, 0.15f)
                : reaction == Reaction.Neutral ? new Color(1f, 0.85f, 0.2f)
                : new Color(0.3f, 0.95f, 0.35f);
            if (_ring == null || color != _ringColor)
            {
                if (_ring != null) Destroy(_ring.gameObject);
                _ring = EntityVisuals.Glow(null, Vector3.zero, Vector3.one, color, PrimitiveType.Cylinder);
                _ring.name = "TargetRing";
                _ringColor = color;
            }
            _ring.gameObject.SetActive(true);
            float size = view.Shape.Radius * 2.6f + 0.4f;
            _ring.localScale = new Vector3(size, 0.012f, size);
            _ring.position = view.transform.position + Vector3.up * 0.04f;
        }
    }
}
