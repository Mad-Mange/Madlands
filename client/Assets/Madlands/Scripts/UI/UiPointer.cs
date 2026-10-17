using UnityEngine;
using UnityEngine.UIElements;

namespace Madlands.Client
{
    /// <summary>
    /// Is the mouse over something clickable in the UI (a button, an aura, the chat line)? Mouse
    /// presses that start there belong to the UI: they must not turn the camera or pick units.
    /// </summary>
    public static class UiPointer
    {
        private static UIDocument _document;

        public static void Register(UIDocument document) => _document = document;

        /// <param name="screenPosition">Input System coordinates (origin bottom-left).</param>
        public static bool IsOverInteractive(Vector2 screenPosition)
        {
            var panel = _document != null ? _document.rootVisualElement?.panel : null;
            if (panel == null) return false;
            var panelPosition = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            var picked = panel.Pick(panelPosition);
            return picked != null && picked.pickingMode == PickingMode.Position;
        }
    }
}
