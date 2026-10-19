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
    /// The settings window (Esc menu or login screen): key bindings, mouse, interface scale,
    /// graphics and sound volumes. Rebinding: click a key button, then press the new key (Esc cancels, Backspace clears).
    /// </summary>
    public sealed class SettingsUi
    {
        private readonly GameWindow _window;
        private readonly VisualElement _controlsTab;
        private readonly VisualElement _videoTab;
        private readonly VisualElement _soundTab;
        private readonly Button _controlsButton;
        private readonly Button _videoButton;
        private readonly Button _soundButton;
        private readonly Dictionary<(GameAction, int), Button> _keyButtons = new Dictionary<(GameAction, int), Button>();
        private readonly List<Resolution> _resolutions = new List<Resolution>();
        private (GameAction Action, int Slot)? _capturing;
        private int _capturedFrame = -1;

        public SettingsUi(VisualElement layer)
        {
            _window = new GameWindow(layer, Loc.T("settings.window"), "settings");
            var tabs = new VisualElement { pickingMode = PickingMode.Ignore };
            tabs.AddToClassList("settings-tabs");
            _controlsButton = TabButton(tabs, Loc.T("settings.tabKeys"), () => ShowTab(0));
            _videoButton = TabButton(tabs, Loc.T("settings.tabVideo"), () => ShowTab(1));
            _soundButton = TabButton(tabs, Loc.T("settings.tabSound"), () => ShowTab(2));
            _window.Body.Add(tabs);

            _controlsTab = new ScrollView { pickingMode = PickingMode.Position };
            _controlsTab.AddToClassList("settings-page");
            BuildControls(_controlsTab);
            _window.Body.Add(_controlsTab);

            _videoTab = new VisualElement { pickingMode = PickingMode.Position };
            _videoTab.AddToClassList("settings-page");
            BuildVideo(_videoTab);
            _window.Body.Add(_videoTab);

            _soundTab = new VisualElement { pickingMode = PickingMode.Position };
            _soundTab.AddToClassList("settings-page");
            BuildSound(_soundTab);
            _window.Body.Add(_soundTab);

            _window.AddFooterButton(Loc.T("settings.done"), Close, "primary");
            _window.Closed += () => _capturing = null;
            ShowTab(0);
            KeyBindings.Changed += RefreshKeys;
        }

        public bool IsOpen => _window.IsOpen;

        public void Open()
        {
            RefreshKeys();
            _window.Open();
        }

        /// <summary>Opens on the sound page (the -open sound flag).</summary>
        public void OpenSound()
        {
            Open();
            ShowTab(2);
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
            GameWindow.Text(page, Loc.T("settings.mouse"), "section-title");
            var sensitivity = new Slider(Loc.T("settings.sensitivity"), Preferences.MinSensitivity, Preferences.MaxSensitivity) { value = Preferences.MouseSensitivity };
            sensitivity.AddToClassList("settings-field");
            sensitivity.RegisterValueChangedCallback(e => Preferences.SetMouseSensitivity(e.newValue));
            page.Add(sensitivity);
            var invert = new Toggle(Loc.T("settings.invert")) { value = Preferences.InvertMouseY };
            invert.AddToClassList("settings-field");
            invert.RegisterValueChangedCallback(e => Preferences.SetInvertMouseY(e.newValue));
            page.Add(invert);

            var header = new VisualElement { pickingMode = PickingMode.Ignore };
            header.AddToClassList("binding-header");
            GameWindow.Text(header, Loc.T("settings.keys"), "section-title");
            var reset = new Button(() => KeyBindings.ResetToDefaults()) { text = Loc.T("settings.resetKeys") };
            reset.AddToClassList("button");
            header.Add(reset);
            page.Add(header);
            GameWindow.Text(page, Loc.T("settings.keysHint"), "window-hint");

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
                button.text = capturing ? Loc.T("settings.pressKey") : KeyBindings.KeyName(KeyBindings.Get(target.Item1, target.Item2));
                button.EnableInClassList("capturing", capturing);
            }
        }

        // ------------------------------------------------------------------ graphics and interface

        private void BuildVideo(VisualElement page)
        {
            GameWindow.Text(page, Loc.T("settings.display"), "section-title");
            var fullscreen = new Toggle(Loc.T("settings.fullscreen")) { value = Screen.fullScreen };
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
            var resolution = new DropdownField(Loc.T("settings.resolution"), names, names.IndexOf(current));
            resolution.AddToClassList("settings-field");
            resolution.RegisterValueChangedCallback(e =>
            {
                var parts = e.newValue.Split('x');
                if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out int w) && int.TryParse(parts[1].Trim(), out int h))
                    Screen.SetResolution(w, h, Screen.fullScreenMode);
            });
            page.Add(resolution);

            var vsync = new Toggle(Loc.T("settings.vsync")) { value = Preferences.VSync };
            vsync.AddToClassList("settings-field");
            vsync.RegisterValueChangedCallback(e => Preferences.SetVSync(e.newValue));
            page.Add(vsync);

            var qualityNames = QualitySettings.names.ToList();
            var quality = new DropdownField(Loc.T("settings.quality"), qualityNames, Preferences.QualityLevel);
            quality.AddToClassList("settings-field");
            quality.RegisterValueChangedCallback(e => Preferences.SetQualityLevel(qualityNames.IndexOf(e.newValue)));
            page.Add(quality);

            GameWindow.Text(page, Loc.T("settings.interface"), "section-title");
            var scale = new Slider(Loc.T("settings.uiScale"), Preferences.MinUiScale, Preferences.MaxUiScale) { value = Preferences.UiScale };
            scale.AddToClassList("settings-field");
            var scaleLabel = GameWindow.Text(page, Percent(Preferences.UiScale), "window-hint");
            // Applied when the slider is let go: rescaling the panel under the mouse while dragging makes the handle jump.
            scale.RegisterValueChangedCallback(e => scaleLabel.text = Percent(e.newValue));
            scale.RegisterCallback<PointerCaptureOutEvent>(_ => Preferences.SetUiScale(scale.value));
            page.Insert(page.IndexOf(scaleLabel), scale);

            // Every language with a table; the interface is rebuilt in it on the next start.
            var languages = Loc.Languages;
            var languageNames = languages.Select(l => l.Value).ToList();
            int currentLanguage = Math.Max(0, languages.ToList().FindIndex(l => l.Key == Preferences.Language));
            var language = new DropdownField(Loc.T("settings.language"), languageNames, currentLanguage);
            language.AddToClassList("settings-field");
            language.RegisterValueChangedCallback(e => Preferences.SetLanguage(languages[languageNames.IndexOf(e.newValue)].Key));
            page.Add(language);
            if (languages.Count > 1) GameWindow.Text(page, Loc.T("settings.languageHint"), "window-hint");
        }

        // ------------------------------------------------------------------ sound

        private static void BuildSound(VisualElement page)
        {
            GameWindow.Text(page, Loc.T("settings.volume"), "section-title");
            Volume(page, Loc.T("settings.master"), Preferences.MasterVolume, Preferences.SetMasterVolume);
            Volume(page, Loc.T("settings.music"), Preferences.MusicVolume, Preferences.SetMusicVolume);
            Volume(page, Loc.T("settings.effects"), Preferences.EffectsVolume, Preferences.SetEffectsVolume);
            Volume(page, Loc.T("settings.ambience"), Preferences.AmbienceVolume, Preferences.SetAmbienceVolume);
        }

        private static void Volume(VisualElement page, string label, float value, Action<float> set)
        {
            var slider = new Slider(label, 0f, 1f) { value = value };
            slider.AddToClassList("settings-field");
            var percent = GameWindow.Text(page, Percent(value), "window-hint");
            slider.RegisterValueChangedCallback(e =>
            {
                percent.text = Percent(e.newValue);
                set(e.newValue);
            });
            page.Insert(page.IndexOf(percent), slider);
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

        private void ShowTab(int tab)
        {
            _capturing = null;
            _controlsTab.style.display = tab == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _videoTab.style.display = tab == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            _soundTab.style.display = tab == 2 ? DisplayStyle.Flex : DisplayStyle.None;
            _controlsButton.EnableInClassList("active", tab == 0);
            _videoButton.EnableInClassList("active", tab == 1);
            _soundButton.EnableInClassList("active", tab == 2);
            RefreshKeys();
        }
    }
}
