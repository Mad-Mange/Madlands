using System.IO;
using Madlands.ClientCore;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Madlands.Client
{
    /// <summary>
    /// F3 toggles network/perf diagnostics. Also handles automated screenshots
    /// (-screenshot path -screenshotdelay s -quitafter s) used for verification runs.
    /// </summary>
    public sealed class DebugOverlay : MonoBehaviour
    {
        private NetworkController _network;
        private bool _visible = true;
        private float _fps;
        private bool _screenshotTaken;
        private GUIStyle _style;

        private void Awake()
        {
            _network = GetComponent<NetworkController>();
        }

        private void Start()
        {
            if (_network.Settings != null && _network.Settings.HideDebug) _visible = false;
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame) _visible = !_visible;
            _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);

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

        private void OnGUI()
        {
            if (!_visible) return;
            var client = _network.Client;
            if (client == null || client.State != ClientState.InWorld) return; // front-end screens have their own UI
            if (_style == null) _style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, richText = true, fontSize = 13 };

            string text = $"<b>Madlands</b>  {_fps:0} fps\n" +
                          $"State: {client.State}\n";
            if (client.State == ClientState.InWorld)
            {
                var player = _network.LocalPlayer;
                // Our own Ping/Pong round trip (includes server tick wait): what gameplay actually feels.
                text += $"Ping: {client.Clock.LastRttMs:0} ms  (best {client.Clock.BestRttMs:0} ms)\n" +
                        $"Map {client.MapId}:{client.InstanceId}  Entities: {client.World.Count}\n" +
                        (player != null ? $"Pos: {player.State.Position}\nFlags: {player.State.Flags}\n" : "") +
                        $"Server time: {client.ServerTimeMs / 1000.0:0.0} s";
            }
            // Below the HUD's unit frames.
            float top = Screen.height * 0.12f;
            GUI.Box(new Rect(10, top, 300, client.State == ClientState.InWorld ? 140 : 50), text, _style);
        }
    }
}
