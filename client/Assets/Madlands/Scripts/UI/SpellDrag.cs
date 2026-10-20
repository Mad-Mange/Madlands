using System;
using Madlands.ClientCore;
using UnityEngine;
using UnityEngine.UIElements;

namespace Madlands.Client
{
    /// <summary>
    /// Dragging a spell with the mouse onto the action bar: from the spellbook or from another slot (which may hold an
    /// item). A press that does not move is a click (cast). The icon follows the pointer while it is dragged.
    /// </summary>
    public sealed class SpellDrag
    {
        private const float StartPixels = 6f;
        private const float GhostSize = 44f;

        private readonly VisualElement _layer;
        private readonly GameClient _client;
        private VisualElement _ghost;

        /// <summary>A spell let go of: the spell, the slot it came from (-1 = the spellbook) and where (panel coordinates).</summary>
        public event Action<uint, int, Vector2> Dropped;

        public SpellDrag(VisualElement layer, GameClient client)
        {
            _layer = layer;
            _client = client;
        }

        /// <summary>
        /// Lets the mouse drag what <paramref name="source"/> holds (<paramref name="spell"/>, read when the drag
        /// starts); <paramref name="slot"/> is its action bar slot or -1; <paramref name="click"/> runs on a press
        /// that does not drag. With <paramref name="shiftToDrag"/> a drag only starts while Shift is held (the
        /// action bar: no spell moves by accident in a fight). <paramref name="ghost"/> makes the icon that follows the
        /// pointer (default: the spell's icon).
        /// </summary>
        public void Attach(VisualElement source, Func<uint> spell, int slot, Action click, bool shiftToDrag = false, Func<uint, VisualElement> ghost = null)
        {
            bool pressed = false, dragging = false;
            uint held = 0;
            Vector2 start = Vector2.zero;
            source.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                pressed = true;
                dragging = false;
                start = e.position;
                source.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            source.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!pressed || !source.HasPointerCapture(e.pointerId)) return;
                if (!dragging && (!shiftToDrag || e.shiftKey) && Vector2.Distance(e.position, start) > StartPixels && (held = spell()) != 0)
                {
                    dragging = true;
                    ShowGhost(ghost != null ? ghost(held) : SpellIcons.Create(_client.Data.GetSpell(held)));
                }
                if (dragging) MoveGhost(e.position);
            });
            source.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!pressed || e.button != 0) return;
                pressed = false;
                if (source.HasPointerCapture(e.pointerId)) source.ReleasePointer(e.pointerId);
                if (dragging)
                {
                    dragging = false;
                    HideGhost();
                    Dropped?.Invoke(held, slot, e.position);
                }
                else click?.Invoke();
                e.StopPropagation();
            });
            source.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (dragging) HideGhost();
                pressed = dragging = false;
            });
        }

        private void ShowGhost(VisualElement icon)
        {
            HideGhost();
            _ghost = icon;
            _ghost.AddToClassList("spell-drag");
            _layer.Add(_ghost);
        }

        private void MoveGhost(Vector2 position)
        {
            if (_ghost == null) return;
            _ghost.style.left = position.x - GhostSize / 2f;
            _ghost.style.top = position.y - GhostSize / 2f;
        }

        private void HideGhost()
        {
            _ghost?.RemoveFromHierarchy();
            _ghost = null;
        }
    }
}
