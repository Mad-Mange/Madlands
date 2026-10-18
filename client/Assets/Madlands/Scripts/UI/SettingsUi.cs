using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Madlands.Client
{
    /// <summary>
    /// The settings window (Esc menu or login screen): key bindings, mouse, interface scale and
    /// graphics. Rebinding: click a key button, then press the new key (Esc cancels, Backspace clears).
    /// </summary>
    public sealed class SettingsUi
    {
        private readonly GameWindow _window;
        private readonly VisualElement _controlsTab;
        private readonly VisualElement _videoTab;
        private readonly Button _controlsButton;
        private readonly Button _videoButton;
        private readonly Dictionary<(GameAction, int), Button> _keyButtons = new Dictionary<(GameAction, int), Button>();
        private readonly List<Resolution> _resolutions = new List<Resolution>();
        private (GameAction Action, int Slot)? _capturing;
        private int _capturedFrame = -1;

        public SettingsUi(VisualElement layer)
        {
            _window = new GameWindow(layer, "Settings", "settings");
            var tabs = new VisualElement { pickingMode = PickingMode.Ignore };
            tabs.AddToClassList("settings-tabs");
            _controlsButton = TabButton(tabs, "Key bindings", () => ShowTab(controls: true));
            _videoButton = TabButton(tabs, "Graphics & interface", () => ShowTab(controls: false));
            _window.Body.Add(tabs);

            _controlsTab = new ScrollView { pickingMode = PickingMode.Position };
            _controlsTab.AddToClassList("settings-page");
            BuildControls(_controlsTab);
            _window.Body.Add(_controlsTab);

            _videoTab = new VisualElement { pickingMode = PickingMode.Position };
            _videoTab.AddToClassList("settings-page");
            BuildVideo(_videoTab);
            _window.Body.Add(_videoTab);

            _window.AddFooterButton("Done", Close, "primary");
            _window.Closed += () => _capturing = null;
            ShowTab(controls: true);
            KeyBindings.Changed += RefreshKeys;
        }

        public bool IsOpen => _window.IsOpen;

        public void Open()
        {
            RefreshKeys();
            _window.Open();
        }

        public void Close() => _window.Close();

        /// <summary>Esc: stops rebinding, else closes the window.</summary>
        public void HandleEscape()
        {
            if (_capturing != null || _capturedFrame == Time.frameCount)
            {
                _capturing = null;
                RefreshKeys();
                return;
            }
            Close();
        }

        /// <summary>Per frame while open: catches the key for a rebind.</summary>
        public void Update()
        {
            if (_capturing is not { } target) return;
            var key = KeyBindings.PressedKey();
            if (key == Key.None) return;
            _capturing = null;
            _capturedFrame = Time.frameCount;
            if (key == Key.Escape)
            {
                RefreshKeys();
                return;
            }
            KeyBindings.Bind(target.Action, target.Slot, key == Key.Backspace || key == Key.Delete ? Key.None : key);
        }

        // ------------------------------------------------------------------ key bindings

        private void BuildControls(VisualElement page)
        {
            GameWindow.Text(page, "Mouse", "section-title");
            var sensitivity = new Slider("Mouse sensitivity", Preferences.MinSensitivity, Preferences.MaxSensitivity) { value = Preferences.MouseSensitivity };
            sensitivity.AddToClassList("settings-field");
            sensitivity.RegisterValueChangedCallback(e => Preferences.SetMouseSensitivity(e.newValue));
            page.Add(sensitivity);
            var invert = new Toggle("Invert vertical mouse") { value = Preferences.InvertMouseY };
            invert.AddToClassList("settings-field");
            invert.RegisterValueChangedCallback(e => Preferences.SetInvertMouseY(e.newValue));
            page.Add(invert);

            var header = new VisualElement { pickingMode = PickingMode.Ignore };
            header.AddToClassList("binding-header");
            GameWindow.Text(header, "Keys", "section-title");
            var reset = new Button(() => KeyBindings.ResetToDefaults()) { text = "Reset to defaults" };
            reset.AddToClassList("button");
            header.Add(reset);
            page.Add(header);
            GameWindow.Text(page, "Click a key, then press the new one. Esc cancels, Backspace clears.", "window-hint");

            foreach (var action in KeyBindings.Actions)
            {
                var row = new VisualElement { pickingMode = PickingMode.Position };
                row.AddToClassList("binding-row");
                GameWindow.Text(row, KeyBindings.Label(action), "binding-name");
                for (int slot = 0; slot < KeyBindings.Slots; slot++)
                {
                    var target = (action, slot);
                    var button = new Button(() => StartCapture(target)) { pickingMode = PickingMode.Position };
                    button.AddToClassList("binding-key");
                    row.Add(button);
                    _keyButtons[target] = button;
                }
                page.Add(row);
            }
        }

        private void StartCapture((GameAction, int) target)
        {
            _capturing = target;
            RefreshKeys();
        }

        private void RefreshKeys()
        {
            foreach (var (target, button) in _keyButtons)
            {
                bool capturing = _capturing is { } c && c.Action == target.Item1 && c.Slot == target.Item2;
                button.text = capturing ? "Press a key..." : KeyBindings.KeyName(KeyBindings.Get(target.Item1, target.Item2));
                button.EnableInClassList("capturing", capturing);
            }
        }

        // ------------------------------------------------------------------ graphics and interface

        private void BuildVideo(VisualElement page)
        {
            GameWindow.Text(page, "Display", "section-title");
            var fullscreen = new Toggle("Fullscreen") { value = Screen.fullScreen };
            fullscreen.AddToClassList("settings-field");
            fullscreen.RegisterValueChangedCallback(e =>
                Screen.fullScreenMode = e.newValue ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            page.Add(fullscreen);

            // Distinct sizes, largest first; the refresh rate is left to the system.
            foreach (var r in Screen.resolutions.OrderByDescending(r => r.width * r.height))
                if (!_resolutions.Any(x => x.width == r.width && x.height == r.height)) _resolutions.Add(r);
            var names = _resolutions.Select(r => r.width + " x " + r.height).ToList();
            string current = Screen.width + " x " + Screen.height;
            if (!names.Contains(current)) names.Insert(0, current);
            var resolution = new DropdownField("Resolution", names, names.IndexOf(current));
            resolution.AddToClassList("settings-field");
            resolution.RegisterValueChangedCallback(e =>
            {
                var parts = e.newValue.Split('x');
                if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out int w) && int.TryParse(parts[1].Trim(), out int h))
                    Screen.SetResolution(w, h, Screen.fullScreenMode);
            });
            page.Add(resolution);

            var vsync = new Toggle("Vertical sync") { value = Preferences.VSync };
            vsync.AddToClassList("settings-field");
            vsync.RegisterValueChangedCallback(e => Preferences.SetVSync(e.newValue));
            page.Add(vsync);

            var qualityNames = QualitySettings.names.ToList();
            var quality = new DropdownField("Quality", qualityNames, Preferences.QualityLevel);
            quality.AddToClassList("settings-field");
            quality.RegisterValueChangedCallback(e => Preferences.SetQualityLevel(qualityNames.IndexOf(e.newValue)));
            page.Add(quality);

            GameWindow.Text(page, "Interface", "section-title");
            var scale = new Slider("Interface scale", Preferences.MinUiScale, Preferences.MaxUiScale) { value = Preferences.UiScale };
            scale.AddToClassList("settings-field");
            var scaleLabel = GameWindow.Text(page, Percent(Preferences.UiScale), "window-hint");
            // Applied when the slider is let go: rescaling the panel under the mouse while dragging makes the handle jump.
            scale.RegisterValueChangedCallback(e => scaleLabel.text = Percent(e.newValue));
            scale.RegisterCallback<PointerCaptureOutEvent>(_ => Preferences.SetUiScale(scale.value));
            page.Insert(page.IndexOf(scaleLabel), scale);
        }

        private static string Percent(float scale) => (scale * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";

        // ------------------------------------------------------------------ tabs

        private Button TabButton(VisualElement tabs, string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("button");
            button.AddToClassList("settings-tab");
            tabs.Add(button);
            return button;
        }

        private void ShowTab(bool controls)
        {
            _capturing = null;
            _controlsTab.style.display = controls ? DisplayStyle.Flex : DisplayStyle.None;
            _videoTab.style.display = controls ? DisplayStyle.None : DisplayStyle.Flex;
            _controlsButton.EnableInClassList("active", controls);
            _videoButton.EnableInClassList("active", !controls);
            RefreshKeys();
        }
    }
}
