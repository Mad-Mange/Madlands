using System.IO;
using Madlands.ClientCore;
using UnityEngine;
using UnityEngine.UIElements;

namespace Madlands.Client
{
    /// <summary>
    /// F3 toggles network/perf diagnostics in a small panel on the HUD that can be dragged with the
    /// mouse (its place is remembered). Also handles automated screenshots
    /// (-screenshot path -screenshotdelay s -quitafter s) used for verification runs.
    /// </summary>
    public sealed class DebugOverlay : MonoBehaviour
    {
        private const float RefreshSeconds = 0.25f;

        private NetworkController _network;
        private bool _visible = true;
        private float _fps;
        private bool _screenshotTaken;
        private VisualElement _panel;
        private Label _text;
        private float _refreshAt;
        private bool _placed;

        private void Awake()
        {
            _network = GetComponent<NetworkController>();
        }

        private void Start()
        {
            if (_network.Settings != null && _network.Settings.HideDebug) _visible = false;
            var hud = FindAnyObjectByType<HudController>();
            var layer = hud != null ? hud.GetComponent<UIDocument>().rootVisualElement.Q("hud") : null;
            if (layer == null) return;
            _panel = new VisualElement { pickingMode = PickingMode.Position };
            _panel.AddToClassList("debug-panel");
            _text = new Label { pickingMode = PickingMode.Ignore, enableRichText = true };
            _panel.Add(_text);
            layer.Add(_panel);
            Draggable.Make(_panel, _panel, p => Preferences.SetDebugPanelPosition(p));
            // Placed once the HUD has a size: where it was left, else below the unit frames.
            _panel.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (_placed || layer.layout.height <= 0f) return;
                _placed = true;
                var saved = Preferences.DebugPanelPosition;
                Draggable.MoveTo(_panel, saved.x >= 0f ? saved : new Vector2(10f, layer.layout.height * 0.12f));
            });
        }

        private void Update()
        {
            if (!InputGate.KeyboardBlocked && KeyBindings.WasPressed(GameAction.ToggleDebug)) _visible = !_visible;
            _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);
            UpdatePanel();

            var settings = _network.Settings;
            if (settings == null) return;
            // Runs that enter the world shoot after arriving; others (login/character screens) after startup.
            bool due = settings.AutoEntersWorld
                ? _network.EnteredWorldAt >= 0f && Time.time - _network.EnteredWorldAt >= settings.ScreenshotDelay
                : Time.realtimeSinceStartup >= settings.ScreenshotDelay;
            if (!_screenshotTaken && settings.ScreenshotPath != null && due)
            {
                var path = Path.GetFullPath(settings.ScreenshotPath);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                ScreenCapture.CaptureScreenshot(path);
                Debug.Log("[Debug] Screenshot saved to " + path);
                _screenshotTaken = true;
            }
            if (settings.QuitAfter > 0f && Time.realtimeSinceStartup >= settings.QuitAfter) Application.Quit();
        }

        private void UpdatePanel()
        {
            if (_panel == null) return;
            var client = _network.Client;
            bool show = _visible && client != null && client.State == ClientState.InWorld;
            _panel.EnableInClassList("hidden", !show);
            if (!show || Time.unscaledTime < _refreshAt) return;
            _refreshAt = Time.unscaledTime + RefreshSeconds;

            var player = _network.LocalPlayer;
            // Our own Ping/Pong round trip (includes server tick wait): what gameplay actually feels.
            _text.text = $"<b>Madlands</b>  {_fps:0} fps\n" +
                         $"Ping: {client.Clock.LastRttMs:0} ms  (best {client.Clock.BestRttMs:0} ms)\n" +
                         $"Map {client.MapId}:{client.InstanceId}  Entities: {client.World.Count}\n" +
                         (player != null ? $"Pos: {player.State.Position}\nFlags: {player.State.Flags}\n" : "") +
                         $"Server time: {client.ServerTimeMs / 1000.0:0.0} s";
        }
    }
}
