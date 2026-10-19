using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Madlands.Client
{
    /// <summary>
    /// Lets the mouse drag an absolutely positioned element by a handle (often the element itself).
    /// The element stays inside its parent; <c>moved</c> gets the new top-left corner when the drag ends.
    /// </summary>
    public static class Draggable
    {
        public static void Make(VisualElement handle, VisualElement target, Action<Vector2> moved = null)
        {
            bool dragging = false;
            Vector2 grab = Vector2.zero;
            handle.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                dragging = true;
                grab = (Vector2)e.position - new Vector2(target.layout.x, target.layout.y);
                handle.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!dragging || !handle.HasPointerCapture(e.pointerId)) return;
                MoveTo(target, (Vector2)e.position - grab);
            });
            handle.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!dragging) return;
                dragging = false;
                handle.ReleasePointer(e.pointerId);
                moved?.Invoke(new Vector2(target.layout.x, target.layout.y));
            });
            handle.RegisterCallback<PointerCaptureOutEvent>(_ => dragging = false);
        }

        /// <summary>Places the element with its top-left at <paramref name="position"/>, kept inside the parent.</summary>
        public static void MoveTo(VisualElement target, Vector2 position)
        {
            var parent = target.parent;
            if (parent != null && parent.layout.width > 0f)
            {
                position.x = Mathf.Clamp(position.x, 0f, Mathf.Max(0f, parent.layout.width - target.layout.width));
                position.y = Mathf.Clamp(position.y, 0f, Mathf.Max(0f, parent.layout.height - target.layout.height));
            }
            target.style.left = position.x;
            target.style.top = position.y;
        }
    }
}
