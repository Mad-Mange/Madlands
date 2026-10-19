using System;
using System.Collections;
using Madlands.ClientCore;
using Madlands.Shared.Movement;
using Madlands.Shared.Net;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Madlands.Client
{
    /// <summary>
    /// Owns the game connection (<see cref="GameClient"/>) and the world's lifetime: on Welcome it
    /// loads the map's scene (World_NNNN, additively, by the mapId the server sends), then spawns
    /// the local player; on disconnect it tears everything down again. The login and character
    /// screens (<see cref="FrontEndController"/>) live in the persistent FrontEnd scene.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class NetworkController : MonoBehaviour
    {
        [SerializeField] private WorldView worldView;
        [SerializeField] private CameraController cameraController;

        private PlayerController _localPlayer;
        private MovementState _pendingSpawn;
        private string _loadedScene;
        private Coroutine _loading;

        public GameClient Client { get; private set; }
        public ClientSettings Settings { get; private set; }
        public PlayerController LocalPlayer => _localPlayer;
        public WorldView WorldView => worldView;
        public bool InWorld => _localPlayer != null;
        public float EnteredWorldAt { get; private set; } = -1f;

        /// <summary>The map is loaded and the local player spawned.</summary>
        public event Action EnteredWorld;
        /// <summary>The connection ended (reason text, and the server's reject reason if it sent one).</summary>
        public event Action<string, RejectReason?> LeftWorld;

        public static string SceneNameFor(ushort mapId) => "World_" + mapId.ToString("0000");

        private void Awake()
        {
            Settings = ClientSettings.FromCommandLine();
            if (worldView == null) worldView = GetComponent<WorldView>();
            if (cameraController == null) cameraController = FindAnyObjectByType<CameraController>();

            Client = new GameClient(manualMode: false, log: msg => Debug.Log("[Net] " + msg));
            Client.EnteredWorld += OnWelcome;
            Client.Corrected += OnCorrected;
            Client.EntitySpawned += e => worldView.OnEntitySpawned(e);
            Client.EntityDespawned += e => worldView.OnEntityDespawned(e);
            Client.Disconnected += OnDisconnected;
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

        /// <summary>The realm and character being played ("host:port/id"), for settings kept per character.</summary>
        public string CharacterKey { get; private set; } = "";

        public void EnterWorld(string host, int port, string sessionToken, ulong characterId)
        {
            if (Client.State != ClientState.Disconnected) return;
            CharacterKey = host + ":" + port + "/" + characterId;
            Client.Connect(host, port, sessionToken, characterId);
        }

        public void LeaveWorld() => Client.Disconnect();

        private void OnWelcome(MovementState spawn)
        {
            _pendingSpawn = spawn;
            Debug.Log($"[Net] Welcome as {Client.LocalGuid} on map {Client.MapId} at {spawn.Position}");
            _loading = StartCoroutine(LoadMapAndSpawn(SceneNameFor(Client.MapId)));
        }

        private IEnumerator LoadMapAndSpawn(string sceneName)
        {
            if (_loadedScene != sceneName)
            {
                if (_loadedScene != null) yield return SceneManager.UnloadSceneAsync(_loadedScene);
                _loadedScene = null;
                var load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                if (load == null)
                {
                    Debug.LogError("[Net] Map scene '" + sceneName + "' is not in the build settings.");
                    Client.Disconnect();
                    yield break;
                }
                yield return load;
                _loadedScene = sceneName;
            }
            _loading = null;

            // The connection may have dropped while loading.
            if (Client.State != ClientState.InWorld) yield break;

            // The map scene's lighting, fog and sky apply to everything created from now on.
            SceneManager.SetActiveScene(SceneManager.GetSceneByName(sceneName));
            _localPlayer = PlayerController.Create(_pendingSpawn, this, cameraController);
            worldView.RegisterLocal(_localPlayer.View);
            cameraController.SetTarget(_localPlayer);
            cameraController.Frame(Settings.CameraPitch, Settings.CameraDistance, Settings.CameraYawOffset);
            worldView.SetReady(true);
            EnteredWorldAt = Time.time;
            EnteredWorld?.Invoke();
        }

        private void OnCorrected(MovementState state)
        {
            if (_localPlayer != null) _localPlayer.ApplyCorrection(state);
            else _pendingSpawn = state; // still loading the map
        }

        private void OnDisconnected(string reason)
        {
            if (_loading != null)
            {
                StopCoroutine(_loading);
                _loading = null;
            }
            worldView.SetReady(false);
            worldView.Clear();
            if (_localPlayer != null) Destroy(_localPlayer.gameObject);
            _localPlayer = null;
            cameraController.SetTarget(null);
            EnteredWorldAt = -1f;
            if (_loadedScene != null)
            {
                SceneManager.UnloadSceneAsync(_loadedScene);
                _loadedScene = null;
            }
            LeftWorld?.Invoke(reason, Client.LastRejectReason);
        }
    }
}
