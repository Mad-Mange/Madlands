using Madlands.ClientCore;
using Madlands.Shared.Movement;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// Owns the <see cref="GameClient"/> and wires its events to the scene: spawns the local player
    /// on Welcome, routes corrections to it and remote entities to the <see cref="WorldView"/>.
    /// Phase 1 dev login: a name and a server address in an IMGUI panel (UI Toolkit in Phase 2).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class NetworkController : MonoBehaviour
    {
        [SerializeField] private WorldView worldView;
        [SerializeField] private CameraController cameraController;

        private ClientSettings _settings;
        private string _portText;
        private PlayerController _localPlayer;
        private string _status = "";

        public GameClient Client { get; private set; }
        public ClientSettings Settings => _settings;
        public PlayerController LocalPlayer => _localPlayer;
        public float EnteredWorldAt { get; private set; } = -1f;

        private void Awake()
        {
            _settings = ClientSettings.FromCommandLine();
            _portText = _settings.Port.ToString();
            if (worldView == null) worldView = GetComponent<WorldView>();
            if (cameraController == null) cameraController = FindAnyObjectByType<CameraController>();

            Client = new GameClient(manualMode: false, log: msg => Debug.Log("[Net] " + msg));
            Client.EnteredWorld += OnEnteredWorld;
            Client.Corrected += OnCorrected;
            Client.EntitySpawned += e => worldView.OnEntitySpawned(e);
            Client.EntityDespawned += e => worldView.OnEntityDespawned(e);
            Client.Disconnected += OnDisconnected;
        }

        private void Start()
        {
            if (_settings.AutoConnect) Connect();
        }

        private void Update()
        {
            Client.Update();
        }

        private void OnApplicationQuit()
        {
            Client.Disconnect();
            Client.Dispose();
        }

        public void Connect()
        {
            if (Client.State != ClientState.Disconnected) return;
            _status = "Connecting to " + _settings.Host + ":" + _settings.Port + "...";
            Client.Connect(_settings.Host, _settings.Port, _settings.Name);
        }

        private void OnEnteredWorld(MovementState spawn)
        {
            _status = "";
            EnteredWorldAt = Time.time;
            _localPlayer = PlayerController.Create(spawn, this, cameraController);
            cameraController.SetTarget(_localPlayer);
            cameraController.Frame(_settings.CameraPitch, _settings.CameraDistance, _settings.CameraYawOffset);
            Debug.Log($"[Net] Entered world as {Client.LocalGuid} at {spawn.Position}");
        }

        private void OnCorrected(MovementState state)
        {
            if (_localPlayer != null) _localPlayer.ApplyCorrection(state);
        }

        private void OnDisconnected(string reason)
        {
            _status = "Disconnected: " + reason;
            worldView.Clear();
            if (_localPlayer != null) Destroy(_localPlayer.gameObject);
            _localPlayer = null;
            cameraController.SetTarget(null);
            EnteredWorldAt = -1f;
        }

        private void OnGUI()
        {
            if (Client == null || Client.State == ClientState.InWorld) return;

            const float w = 320f, h = 190f;
            GUILayout.BeginArea(new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h), GUI.skin.box);
            GUILayout.Label("<b>Madlands</b> – dev login (Phase 1)", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 16 });
            bool idle = Client.State == ClientState.Disconnected;
            GUI.enabled = idle;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", GUILayout.Width(60));
            _settings.Name = GUILayout.TextField(_settings.Name ?? "", 24);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Server", GUILayout.Width(60));
            _settings.Host = GUILayout.TextField(_settings.Host ?? "", 64);
            _portText = GUILayout.TextField(_portText, 5, GUILayout.Width(60));
            GUILayout.EndHorizontal();
            if (GUILayout.Button(idle ? "Connect" : "Connecting...", GUILayout.Height(32)) && idle)
            {
                if (int.TryParse(_portText, out var port)) _settings.Port = port;
                Connect();
            }
            GUI.enabled = true;
            GUILayout.Label(_status, new GUIStyle(GUI.skin.label) { wordWrap = true });
            GUILayout.EndArea();
        }
    }
}
