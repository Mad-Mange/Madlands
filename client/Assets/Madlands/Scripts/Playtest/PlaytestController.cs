#if MADLANDS_PLAYTEST
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Madlands.ClientCore;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Movement;
using Madlands.Shared.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Madlands.Client
{
    /// <summary>
    /// Playtest builds only (scripting define MADLANDS_PLAYTEST, set by Editor/PlaytestBuild): started with
    /// -playtest &lt;folder&gt;, the client takes validated actions from a file mailbox and answers each with an
    /// observation and a screenshot. tools/AICritic drives it (/critic-explore). Normal builds do not
    /// contain this class.
    ///
    ///   &lt;folder&gt;/commands/0001.json      one action each, numbered from 1 (written by the orchestrator)
    ///   &lt;folder&gt;/observations/0001.json  written here once the action has finished
    ///   &lt;folder&gt;/screenshots/0001.png    the game at that moment
    ///   &lt;folder&gt;/status.json             heartbeat: in the world yet, busy or idle
    ///   &lt;folder&gt;/gm_commands.json        the GM commands and their argument schemas (discovery)
    ///   &lt;folder&gt;/gm_catalog.json         spawnable creatures and teleport destinations (GM sessions)
    ///
    /// Movement goes through KeyBindings, the same path as the keyboard; everything else uses the client's
    /// own methods (targeting, interaction, windows, spells), like AutoPlay. Only the actions below exist,
    /// and GM chat lines are limited to a few scenario commands (the test realm runs with --gm-all).
    /// gm_command runs a developer-menu command by its stable id through the same GmCommandClient the menu
    /// uses (docs/GMMenu), and puts the structured result into the observation.
    /// </summary>
    public sealed class PlaytestController : MonoBehaviour
    {
        private const float NearbyRange = 45f;
        private const int MaxNearby = 10;
        private const int MaxEvents = 40;
        private static readonly string[] GmPrefixes =
            { ".tele ", ".level ", ".heal", ".revive", ".respawn", ".money ", ".additem ", ".quest " };

        [Serializable]
        private sealed class Command
        {
            public int seq;
            public string action;
            public string direction;
            public float seconds;
            public float degrees;
            public float yaw;
            public float x;
            public float z;
            public string name;
            public string window;
            public int slot;
            public bool has_pitch, has_distance, has_yaw_offset;
            public float pitch, distance, yaw_offset;
            public bool sprint;
            public string text;
            public string command_id;
            public string key;
            public bool shift;
            public bool @double;
            public List<string> arg_keys = new List<string>();
            public List<string> arg_values = new List<string>();
        }

        [Serializable]
        private sealed class GmResultObs
        {
            public uint request_id;
            public string command_id, code, message;
            public bool ok;
            public List<string> data = new List<string>();
        }

        [Serializable]
        private sealed class GmStateObs
        {
            public int gm_level, speed_percent, damage_percent, debug_spawns;
            public bool god_mode, no_cooldowns;
        }

        [Serializable]
        private sealed class GmArgSpecObs
        {
            public string name, type;
            public double min, max;
            public bool optional;
        }

        [Serializable]
        private sealed class GmCommandSpecObs
        {
            public string id, category, side;
            public bool persistent;
            public int min_gm_level;
            public List<GmArgSpecObs> args = new List<GmArgSpecObs>();
        }

        [Serializable]
        private sealed class GmCommandsFile
        {
            public List<GmCommandSpecObs> commands = new List<GmCommandSpecObs>();
        }

        [Serializable]
        private sealed class GmCatalogFile
        {
            public List<string> creatures = new List<string>();
            public List<string> destinations = new List<string>();
        }

        [Serializable]
        private sealed class PlayerObs
        {
            public string name;
            public int level, health, max_health, power, max_power;
            public bool dead, in_combat, airborne, casting;
            public float x, y, z, yaw;
        }

        [Serializable]
        private sealed class UnitObs
        {
            public string name, kind, reaction;
            public int level;
            public float health_percent, distance, bearing;
            public bool dead, lootable;
        }

        [Serializable]
        private sealed class Observation
        {
            public int seq;
            public string action;
            public bool ok;
            public string result = "";
            public string screenshot = "";
            /// <summary>Extra pictures taken during movement and fights (a short recording).</summary>
            public List<string> frames = new List<string>();
            public float started, finished;
            public int frame;
            public bool in_world;
            public PlayerObs player;
            public bool has_target;
            public UnitObs target;
            public List<UnitObs> nearby = new List<UnitObs>();
            public bool loot_open, dialog_open, stuck;
            /// <summary>fps_p5: the frame rate 95% of the frames reach (robust against a single stall).</summary>
            public float moved, fps_avg, fps_p5, fps_min;
            public List<string> events = new List<string>();
            public int log_error_count, log_warning_count;
            public List<string> log_errors = new List<string>();
            public GmStateObs gm;
            public bool has_gm_result;
            public GmResultObs gm_result;
            /// <summary>Menus and input focus: the developer menu, the game (Esc) menu, a text field with the keyboard.</summary>
            public bool developer_menu_open, game_menu_open, typing;
            /// <summary>The developer menu's teleport X and Z fields, and whether the world map is picking.</summary>
            public string developer_coordinates = "";
            public bool map_picking;
        }

        [Serializable]
        private sealed class Status
        {
            public string state;
            public bool in_world, busy;
            public int next_seq, frame;
            public float realtime;
        }

        private string _dir;
        private NetworkController _network;
        private HudController _hud;
        private TargetingController _targeting;
        private CameraController _camera;
        private readonly HashSet<GameAction> _held = new HashSet<GameAction>();
        private readonly HashSet<GameAction> _pressed = new HashSet<GameAction>();
        private readonly List<string> _events = new List<string>();
        private readonly List<string> _logErrors = new List<string>();
        private int _errorCount, _warningCount;
        private int _nextSeq = 1;
        private bool _busy, _subscribed;
        private float _nextPoll, _nextStatus;
        private int _frames;
        private float _frameTime, _worstFrame;
        private readonly List<float> _frameTimes = new List<float>();
        private int _skipFrameStats;
        private bool _actionDone, _burstDone;
        private int _spellCursor;
        // Server answers that tell whether an interaction worked (counted, so an action can look at its own).
        private int _rejections, _dialogsOpened, _vendorsOpened;
        private string _lastRejection = "";

        private GameClient Client => _network != null ? _network.Client : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string dir = null;
            var args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length - 1; i++)
                if (string.Equals(args[i], "-playtest", StringComparison.OrdinalIgnoreCase)) dir = args[i + 1];
            if (string.IsNullOrEmpty(dir)) return;
            var go = new GameObject("PlaytestController");
            DontDestroyOnLoad(go);
            go.AddComponent<PlaytestController>().Init(Path.GetFullPath(dir));
        }

        private void Init(string dir)
        {
            _dir = dir;
            foreach (var sub in new[] { "commands", "observations", "screenshots" })
                Directory.CreateDirectory(Path.Combine(dir, sub));
            KeyBindings.SimulatedHeld = action => _held.Contains(action);
            KeyBindings.SimulatedPressed = action => _pressed.Contains(action);
            Application.logMessageReceived += OnLog;
            Application.runInBackground = true;
            WriteGmCommands();
            Debug.Log("[Playtest] Controller active, mailbox " + dir);
        }

        private void OnDestroy()
        {
            KeyBindings.SimulatedHeld = null;
            KeyBindings.SimulatedPressed = null;
            Application.logMessageReceived -= OnLog;
        }

        private void Update()
        {
            if (_network == null)
            {
                _network = FindAnyObjectByType<NetworkController>();
                if (_network != null)
                {
                    _hud = _network.GetComponent<HudController>();
                    _targeting = _network.GetComponent<TargetingController>();
                }
            }
            if (_camera == null) _camera = FindAnyObjectByType<CameraController>();
            if (!_subscribed && Client != null) Subscribe();

            float dt = Time.unscaledDeltaTime;
            if (_skipFrameStats > 0)
            {
                // A burst picture stalls for the GPU readback and the PNG encoding, and the frames right after
                // it catch up: none of that is the game's frame rate.
                _skipFrameStats--;
            }
            else
            {
                _frames++;
                _frameTime += dt;
                if (dt > _worstFrame) _worstFrame = dt;
                if (_frameTimes.Count < 20000) _frameTimes.Add(dt);
            }

            if (Time.unscaledTime >= _nextStatus) WriteStatus();
            if (!_busy && Time.unscaledTime >= _nextPoll)
            {
                _nextPoll = Time.unscaledTime + 0.1f;
                PollCommand();
            }
        }

        // ------------------------------------------------------------------ mailbox

        private void PollCommand()
        {
            string path = Path.Combine(_dir, "commands", _nextSeq.ToString("0000") + ".json");
            if (!File.Exists(path)) return;
            Command command;
            try
            {
                command = JsonUtility.FromJson<Command>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                command = new Command { seq = _nextSeq, action = "invalid", text = e.Message };
            }
            if (command == null) command = new Command { seq = _nextSeq, action = "invalid" };
            command.seq = _nextSeq;
            StartCoroutine(Run(command));
        }

        private IEnumerator Run(Command c)
        {
            _busy = true;
            WriteStatus();
            var obs = new Observation { seq = c.seq, action = c.action, started = Time.realtimeSinceStartup };
            _frames = 0;
            _frameTime = 0f;
            _worstFrame = 0f;
            _frameTimes.Clear();
            var start = PlayerPosition();
            bool needsWorld = c.action != "quit" && c.action != "wait" && c.action != "screenshot" && c.action != "invalid";
            if (needsWorld && (_network == null || !_network.InWorld))
                Fail(obs, "not in the world");
            else
            {
                _actionDone = false;
                _burstDone = true;
                var (count, first, interval) = BurstPlan(c);
                if (count > 0)
                {
                    _burstDone = false;
                    StartCoroutine(Burst(obs, count, first, interval));
                }
                yield return StartCoroutine(Execute(c, obs));
                _actionDone = true;
                float waitUntil = Time.realtimeSinceStartup + 5f;
                while (!_burstDone && Time.realtimeSinceStartup < waitUntil) yield return null;
            }
            _held.Clear();
            _pressed.Clear();
            if (start.HasValue && PlayerPosition() is Vector3 end) obs.moved = Flat(end - start.Value);
            yield return StartCoroutine(Capture(obs));
            Fill(obs);
            WriteJson(Path.Combine(_dir, "observations", c.seq.ToString("0000") + ".json"), JsonUtility.ToJson(obs, true));
            _nextSeq++;
            _busy = false;
            WriteStatus();
            if (c.action == "quit") Application.Quit();
        }

        private IEnumerator Execute(Command c, Observation o)
        {
            var client = Client;
            var player = _network != null ? _network.LocalPlayer : null;
            switch (c.action)
            {
                case "wait":
                {
                    float s = Mathf.Clamp(c.seconds, 0.1f, 10f);
                    yield return new WaitForSeconds(s);
                    Ok(o, $"waited {s:0.0} s");
                    break;
                }
                case "screenshot":
                    yield return null;
                    Ok(o, "screenshot taken");
                    break;
                case "move":
                {
                    GameAction action;
                    switch (c.direction)
                    {
                        case "forward": action = GameAction.MoveForward; break;
                        case "backward": action = GameAction.MoveBackward; break;
                        case "left": action = GameAction.StrafeLeft; break;
                        case "right": action = GameAction.StrafeRight; break;
                        default: Fail(o, "unknown direction " + c.direction); yield break;
                    }
                    float s = Mathf.Clamp(c.seconds, 0.2f, 6f);
                    var from = player.transform.position;
                    _held.Add(action);
                    if (c.sprint && action == GameAction.MoveForward) _held.Add(GameAction.Sprint);
                    yield return new WaitForSeconds(s);
                    _held.Clear();
                    yield return new WaitForSeconds(0.2f);
                    float moved = Flat(player.transform.position - from);
                    float expected = (action == GameAction.MoveBackward ? GameConstants.BackwardSpeed : GameConstants.RunSpeed) * s;
                    o.stuck = moved < expected * 0.3f;
                    Ok(o, $"moved {c.direction} {moved:0.0} m in {s:0.0} s (about {expected:0.0} m expected)"
                          + (o.stuck ? ": blocked or stuck" : ""));
                    break;
                }
                case "turn":
                case "face":
                {
                    float yaw = c.action == "turn" ? player.State.Yaw * Mathf.Rad2Deg + Mathf.Clamp(c.degrees, -180f, 180f) : c.yaw;
                    FaceYaw(player, yaw);
                    yield return null;
                    yield return null;
                    Ok(o, $"now facing {Wrap360(player.State.Yaw * Mathf.Rad2Deg):0} degrees");
                    break;
                }
                case "jump":
                {
                    float y0 = player.transform.position.y, peak = y0;
                    _pressed.Add(GameAction.Jump);
                    yield return null;
                    yield return null;
                    _pressed.Clear();
                    float until = Time.time + 1.5f;
                    while (Time.time < until)
                    {
                        peak = Mathf.Max(peak, player.transform.position.y);
                        yield return null;
                    }
                    Ok(o, $"jumped {peak - y0:0.00} m high");
                    break;
                }
                case "camera":
                    if (_camera == null)
                    {
                        Fail(o, "no camera controller");
                        yield break;
                    }
                    _camera.Frame(c.has_pitch ? Mathf.Clamp(c.pitch, 5f, 80f) : (float?)null,
                        c.has_distance ? Mathf.Clamp(c.distance, 2f, 40f) : (float?)null,
                        c.has_yaw_offset ? Mathf.Clamp(c.yaw_offset, -180f, 180f) : (float?)null);
                    yield return new WaitForSeconds(0.5f);
                    Ok(o, "camera framed");
                    break;
                case "walk_to":
                    yield return StartCoroutine(WalkTo(player, new Vector3(c.x, 0f, c.z), Mathf.Clamp(c.seconds, 1f, 30f), o));
                    break;
                case "teleport":
                {
                    client.SendChat(".tele " + c.x.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " "
                                    + c.z.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                    yield return new WaitForSeconds(2f);
                    var p = player.transform.position;
                    float off = Flat(new Vector3(c.x - p.x, 0f, c.z - p.z));
                    if (off < 5f) Ok(o, $"teleported to {c.x:0}, {c.z:0}");
                    else Fail(o, $"teleport did not arrive ({off:0} m away)");
                    break;
                }
                case "gm":
                {
                    string text = (c.text ?? "").Trim();
                    bool allowed = false;
                    foreach (var prefix in GmPrefixes)
                        if (text.StartsWith(prefix.TrimEnd(), StringComparison.OrdinalIgnoreCase)) allowed = true;
                    if (!allowed)
                    {
                        Fail(o, "GM command not allowed: " + text);
                        yield break;
                    }
                    client.SendChat(text);
                    yield return new WaitForSeconds(0.8f);
                    Ok(o, "sent " + text);
                    break;
                }
                case "target_nearest":
                {
                    var guid = Nearest(player.transform.position, NearbyRange, e => e.Unit != null
                        && e.Guid.Type == EntityType.Creature && client.CanAttack(e.Guid));
                    if (guid.IsEmpty)
                    {
                        Fail(o, $"no attackable creature within {NearbyRange:0} m");
                        yield break;
                    }
                    client.SetTarget(guid);
                    yield return new WaitForSeconds(0.3f);
                    Ok(o, "targeted " + NameOf(guid));
                    break;
                }
                case "target":
                {
                    var guid = Nearest(player.transform.position, NearbyRange, e => e.Name == c.name);
                    if (guid.IsEmpty)
                    {
                        Fail(o, $"nothing named '{c.name}' within {NearbyRange:0} m");
                        yield break;
                    }
                    client.SetTarget(guid);
                    yield return new WaitForSeconds(0.3f);
                    Ok(o, "targeted " + NameOf(guid));
                    break;
                }
                case "attack":
                    yield return StartCoroutine(Attack(player, Mathf.Clamp(c.seconds, 1f, 12f), o));
                    break;
                case "cast":
                {
                    int slot = Mathf.Clamp(c.slot, 1, 12);
                    _hud.PressButton(slot - 1);
                    yield return new WaitForSeconds(1.6f);
                    Ok(o, $"pressed action button {slot}");
                    break;
                }
                case "interact":
                {
                    var guid = Nearest(player.transform.position, NearbyRange, e => e.Name == c.name);
                    if (guid.IsEmpty)
                    {
                        Fail(o, $"nothing named '{c.name}' within {NearbyRange:0} m");
                        yield break;
                    }
                    int rejections = _rejections, dialogs = _dialogsOpened, vendors = _vendorsOpened;
                    float distance = Distance(player, guid);
                    client.SetTarget(guid);
                    client.Interact(guid);
                    yield return new WaitForSeconds(1.5f);
                    if (_rejections > rejections) Fail(o, $"{c.name} ({distance:0.0} m away) refused: {_lastRejection}");
                    else if (_vendorsOpened > vendors) Ok(o, $"talked to {c.name}: the vendor window is open");
                    else if (_dialogsOpened > dialogs || _hud.Quests.DialogOpen) Ok(o, $"talked to {c.name}: a dialog is open");
                    else Fail(o, $"nothing opened when interacting with {c.name} ({distance:0.0} m away)");
                    break;
                }
                case "interact_object":
                {
                    var guid = Nearest(player.transform.position, GameConstants.InteractRange, e => e.IsObject);
                    if (guid.IsEmpty)
                    {
                        Fail(o, $"no object within {GameConstants.InteractRange:0} m");
                        yield break;
                    }
                    int rejections = _rejections;
                    client.Interact(guid);
                    yield return new WaitForSeconds(2f);
                    if (_rejections > rejections) Fail(o, $"{NameOf(guid)} refused: {_lastRejection}");
                    else Ok(o, "used " + NameOf(guid) + (client.Loot.IsOpen ? ": its loot is open" : ""));
                    break;
                }
                case "loot":
                {
                    var guid = Nearest(player.transform.position, GameConstants.InteractRange, e => client.IsLootable(e.Guid));
                    if (guid.IsEmpty)
                    {
                        Fail(o, $"no lootable corpse within {GameConstants.InteractRange:0} m");
                        yield break;
                    }
                    int rejections = _rejections;
                    client.OpenLoot(guid);
                    yield return new WaitForSeconds(1f);
                    if (_rejections > rejections) Fail(o, $"looting {NameOf(guid)} refused: {_lastRejection}");
                    else if (client.Loot.IsOpen) Ok(o, "opened the loot of " + NameOf(guid));
                    else Fail(o, "the loot of " + NameOf(guid) + " did not open");
                    break;
                }
                case "gm_command":
                {
                    // The same service the developer menu uses: client-side validation, then the server decides.
                    var args = new List<KeyValuePair<string, string>>();
                    for (int i = 0; i < Math.Min(c.arg_keys.Count, c.arg_values.Count); i++)
                        args.Add(new KeyValuePair<string, string>(c.arg_keys[i], c.arg_values[i]));
                    GmCommandResult result = null;
                    client.Gm.Execute(c.command_id ?? "", args, r => result = r);
                    float until = Time.realtimeSinceStartup + GmCommandClient.TimeoutMs / 1000f + 1f;
                    while (result == null && Time.realtimeSinceStartup < until) yield return null;
                    if (result == null)
                    {
                        Fail(o, "no answer to " + c.command_id);
                        yield break;
                    }
                    o.has_gm_result = true;
                    o.gm_result = new GmResultObs
                    {
                        request_id = result.RequestId, command_id = result.CommandId, code = result.Code.ToString(),
                        message = result.Message, ok = result.Ok,
                    };
                    foreach (var d in result.Data) o.gm_result.data.Add(d.Key + "=" + d.Value);
                    // Let teleports, spawns and overlays show before the picture.
                    yield return new WaitForSeconds(0.6f);
                    if (result.Ok) Ok(o, $"{result.Code}: {result.Message}");
                    else Fail(o, $"{result.Code}: {result.Message}");
                    break;
                }

                case "press_key":
                {
                    // A real key press through the Input System (the same path as the keyboard), so the whole
                    // chain runs: front end, HUD, the developer menu's own key, game menu. Only Esc and § / `.
                    var keyboard = Keyboard.current;
                    Key key = c.key == "escape" ? Key.Escape : c.key == "backquote" ? Key.Backquote : Key.None;
                    if (keyboard == null || key == Key.None)
                    {
                        Fail(o, keyboard == null ? "no keyboard device" : "key not allowed: " + c.key);
                        yield break;
                    }
                    InputSystem.QueueStateEvent(keyboard, c.shift ? new KeyboardState(Key.LeftShift, key) : new KeyboardState(key));
                    yield return null;
                    yield return null;
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    yield return new WaitForSeconds(0.4f);
                    Ok(o, $"pressed {(c.shift ? "Shift+" : "")}{c.key}: developer menu {(_hud.DeveloperMenu.IsOpen ? "open" : "closed")}, " +
                          $"game menu {(InputGate.MenuOpen ? "open" : "closed")}");
                    break;
                }

                case "developer_pick_on_map":
                    if (_hud.DeveloperMenu == null || !_hud.DeveloperMenu.IsAvailable)
                    {
                        Fail(o, "the developer menu needs GM rights");
                        yield break;
                    }
                    _hud.DeveloperMenu.Open();
                    _hud.DeveloperMenu.PickOnMap();
                    yield return new WaitForSeconds(0.6f);
                    if (_hud.Map.IsPicking) Ok(o, "world map open for picking");
                    else Fail(o, "the world map did not open for picking");
                    break;

                case "click_map":
                {
                    // Real mouse clicks (Input System) on the world map where world x, z is drawn.
                    if (!_hud.Map.TryWorldMapPoint(c.x, c.z, out var panelPoint))
                    {
                        Fail(o, "the world map is not open");
                        yield break;
                    }
                    var screen = PanelToScreen(panelPoint);
                    yield return MouseClick(screen);
                    if (c.@double) yield return MouseClick(screen);
                    yield return new WaitForSeconds(c.@double ? 1.2f : 0.4f);
                    Ok(o, $"clicked the map at world {c.x:0.0}, {c.z:0.0} (screen {screen.x:0}, {screen.y:0}){(c.@double ? " twice" : "")}: " +
                          $"fields {_hud.DeveloperMenu.Coordinates}");
                    break;
                }

                case "open_developer_menu":
                    if (_hud.DeveloperMenu == null || !_hud.DeveloperMenu.IsAvailable)
                    {
                        Fail(o, "the developer menu needs GM rights");
                        yield break;
                    }
                    _hud.DeveloperMenu.Open();
                    yield return new WaitForSeconds(0.5f);
                    Ok(o, "developer menu open");
                    break;

                case "open_window":
                    _hud.OpenWindow(c.window ?? "");
                    yield return new WaitForSeconds(0.8f);
                    Ok(o, "opened " + c.window);
                    break;
                case "close_windows":
                {
                    int closed = 0;
                    for (int i = 0; i < 10 && _hud.HandleEscape(); i++)
                    {
                        closed++;
                        yield return null;
                    }
                    yield return new WaitForSeconds(0.3f);
                    Ok(o, $"closed {closed} window(s)");
                    break;
                }
                case "quit":
                    Ok(o, "quitting");
                    break;
                default:
                    Fail(o, "unknown action " + c.action + (c.text != null ? ": " + c.text : ""));
                    break;
            }
        }

        private IEnumerator WalkTo(PlayerController player, Vector3 dest, float limit, Observation o)
        {
            float t0 = Time.time, lastCheck = t0;
            var last = player.transform.position;
            bool stuck = false;
            _held.Add(GameAction.MoveForward);
            while (Time.time - t0 < limit)
            {
                var p = player.transform.position;
                if (Flat(new Vector3(dest.x - p.x, 0f, dest.z - p.z)) < 2f) break;
                player.FaceTowards(new Vector3(dest.x, p.y, dest.z));
                if (Time.time - lastCheck >= 1.5f)
                {
                    if (Flat(p - last) < 0.75f)
                    {
                        stuck = true;
                        break;
                    }
                    last = p;
                    lastCheck = Time.time;
                }
                yield return null;
            }
            _held.Clear();
            var end = player.transform.position;
            float left = Flat(new Vector3(dest.x - end.x, 0f, dest.z - end.z));
            o.stuck = stuck;
            if (left < 2f) Ok(o, $"reached {dest.x:0}, {dest.z:0} in {Time.time - t0:0.0} s");
            else Fail(o, (stuck ? "stuck" : "time ran out") + $" {left:0.0} m short of {dest.x:0}, {dest.z:0}");
        }

        /// <summary>Fights the target (or the nearest enemy) for a while: faces it, swings in melee range and
        /// casts the usable spells in turn, like AutoPlay's -autofight.</summary>
        private IEnumerator Attack(PlayerController player, float seconds, Observation o)
        {
            var client = Client;
            if (client.Target.IsEmpty || !client.CanAttack(client.Target))
                client.SetTarget(Nearest(player.transform.position, NearbyRange, e => e.Unit != null
                    && e.Guid.Type == EntityType.Creature && client.CanAttack(e.Guid)));
            var target = client.Target;
            if (target.IsEmpty)
            {
                Fail(o, $"no attackable creature within {NearbyRange:0} m");
                yield break;
            }
            int startHealth = client.TryGetUnit(target, out var unit) ? unit.Values.Health : 0;
            string name = NameOf(target);
            float until = Time.time + seconds;
            int casts = 0;
            while (Time.time < until)
            {
                if (client.Self.Values.IsDead || !client.TryGetUnit(target, out unit) || unit.Values.IsDead) break;
                if (_network.WorldView.TryGetView(target, out var view))
                {
                    player.FaceTowards(view.transform.position);
                    if (Vector3.Distance(player.transform.position, view.transform.position) <= GameConstants.MeleeRange)
                        _targeting.Attack(target);
                    if (client.Self.Cast == null && client.GcdRemainingMs <= 0 && CastNext(client, target)) casts++;
                }
                yield return new WaitForSeconds(0.35f);
            }
            bool killed = client.TryGetUnit(target, out unit) && unit.Values.IsDead;
            int health = unit != null ? unit.Values.Health : 0;
            Ok(o, $"fought {name} for {seconds:0} s: {casts} spell(s), health {startHealth} -> {health}"
                  + (killed ? ", killed it" : "") + (client.Self.Values.IsDead ? ", and died" : ""));
        }

        private bool CastNext(GameClient client, EntityGuid target)
        {
            var known = client.KnownSpells;
            for (int i = 0; i < known.Count; i++)
            {
                uint id = known[(_spellCursor + i) % known.Count];
                if (!client.Data.TryGetSpell(id, out var spell) || spell.IsAutoAttack || spell.Has(SpellFlags.Hidden)) continue;
                var spellTarget = spell.Target == SpellTarget.Enemy ? target : client.LocalGuid;
                if (client.CheckCast(id, spellTarget) != CastResult.Success) continue;
                _hud.UseSpell(id);
                _spellCursor = (_spellCursor + i + 1) % known.Count;
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ observation

        /// <summary>How many extra pictures an action gets, when the first is taken and how far apart.</summary>
        private static (int count, float first, float interval) BurstPlan(Command c)
        {
            switch (c.action)
            {
                case "move":
                {
                    float s = Mathf.Clamp(c.seconds, 0.2f, 6f);
                    return (4, Mathf.Min(0.3f, s / 4f), Mathf.Max(0.2f, s / 4f));
                }
                case "walk_to":
                    return (6, 0.5f, Mathf.Max(0.5f, Mathf.Clamp(c.seconds, 1f, 30f) / 6f));
                case "jump":
                    return (4, 0.08f, 0.15f);
                case "attack":
                {
                    float s = Mathf.Clamp(c.seconds, 1f, 12f);
                    return (6, 0.4f, Mathf.Max(0.3f, s / 6f));
                }
                case "cast":
                    return (3, 0.3f, 0.4f);
                default:
                    return (0, 0f, 0f);
            }
        }

        /// <summary>Takes pictures while the action runs (NNNN_f1.png ...): a short recording of motion and effects.</summary>
        private IEnumerator Burst(Observation o, int count, float first, float interval)
        {
            try
            {
                yield return new WaitForSeconds(first);
                for (int i = 1; i <= count; i++)
                {
                    if (i > 1)
                    {
                        if (_actionDone) break;
                        yield return new WaitForSeconds(interval);
                    }
                    string name = $"{o.seq:0000}_f{i}.png";
                    yield return StartCoroutine(SaveScreenshot("screenshots/" + name));
                    if (File.Exists(Path.Combine(_dir, "screenshots", name))) o.frames.Add("screenshots/" + name);
                }
            }
            finally
            {
                _burstDone = true;
            }
        }

        private IEnumerator Capture(Observation o)
        {
            string rel = "screenshots/" + o.seq.ToString("0000") + ".png";
            yield return StartCoroutine(SaveScreenshot(rel));
            if (File.Exists(Path.Combine(_dir, rel))) o.screenshot = rel;
            else o.result += " (screenshot failed)";
        }

        /// <summary>Unity's own screenshot (encoded and written natively, no managed garbage that would show up
        /// as garbage-collection stalls in the frame rate); waits until the file is complete.</summary>
        private IEnumerator SaveScreenshot(string relativePath)
        {
            string path = Path.Combine(_dir, relativePath);
            ScreenCapture.CaptureScreenshot(path);
            // Unity encodes the picture at the end of this frame, so the stall shows in the next frames.
            _skipFrameStats = 3;
            long size = -1;
            float until = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                long now = File.Exists(path) ? new FileInfo(path).Length : -1;
                if (now > 0 && now == size) break;
                size = now;
            }
            _skipFrameStats = Mathf.Max(_skipFrameStats, 1);
        }

        private void Fill(Observation o)
        {
            o.finished = Time.realtimeSinceStartup;
            o.frame = Time.frameCount;
            o.fps_avg = _frameTime > 0f ? _frames / _frameTime : 0f;
            o.fps_min = _worstFrame > 0f ? 1f / _worstFrame : 0f;
            if (_frameTimes.Count > 0)
            {
                _frameTimes.Sort();
                float p95 = _frameTimes[Mathf.Min(_frameTimes.Count - 1, (int)(_frameTimes.Count * 0.95f))];
                o.fps_p5 = p95 > 0f ? 1f / p95 : 0f;
            }
            o.in_world = _network != null && _network.InWorld;
            o.developer_menu_open = _hud != null && _hud.DeveloperMenu != null && _hud.DeveloperMenu.IsOpen;
            o.game_menu_open = o.in_world && InputGate.MenuOpen;
            o.typing = InputGate.Typing;
            if (_hud != null && _hud.DeveloperMenu != null)
            {
                o.developer_coordinates = _hud.DeveloperMenu.Coordinates;
                o.map_picking = _hud.Map != null && _hud.Map.IsPicking;
            }
            if (Client != null)
            {
                var gm = Client.GmStatus;
                o.gm = new GmStateObs
                {
                    gm_level = gm.GmLevel, god_mode = (gm.Overrides & GmOverrides.GodMode) != 0,
                    no_cooldowns = (gm.Overrides & GmOverrides.NoCooldowns) != 0, speed_percent = gm.SpeedPercent,
                    damage_percent = gm.DamagePercent, debug_spawns = gm.DebugSpawns,
                };
            }
            var client = Client;
            if (o.in_world)
            {
                var p = _network.LocalPlayer;
                var v = client.Self.Values;
                var pos = p.transform.position;
                float yaw = Wrap360(p.State.Yaw * Mathf.Rad2Deg);
                o.player = new PlayerObs
                {
                    name = client.LocalName, level = v.Level, health = v.Health, max_health = v.MaxHealth,
                    power = v.Power, max_power = v.MaxPower, dead = v.IsDead, in_combat = v.Has(UnitFlags.InCombat),
                    airborne = (p.State.Flags & MovementFlags.Airborne) != 0, casting = client.Self.Cast != null,
                    x = Round(pos.x), y = Round(pos.y), z = Round(pos.z), yaw = Round(yaw),
                };
                if (!client.Target.IsEmpty && client.World.TryGet(client.Target, out var target) && target.View is GameObject tgo)
                {
                    o.has_target = true;
                    o.target = Describe(client, target, tgo, pos, yaw);
                }
                var nearby = new List<(float, UnitObs)>();
                foreach (var e in client.World.Entities)
                {
                    if (!(e.View is GameObject go) || e.Guid == client.LocalGuid) continue;
                    float d = Vector3.Distance(pos, go.transform.position);
                    if (d <= NearbyRange) nearby.Add((d, Describe(client, e, go, pos, yaw)));
                }
                nearby.Sort((a, b) => a.Item1.CompareTo(b.Item1));
                for (int i = 0; i < nearby.Count && i < MaxNearby; i++) o.nearby.Add(nearby[i].Item2);
                o.loot_open = client.Loot.IsOpen;
                o.dialog_open = _hud != null && _hud.Quests.DialogOpen;
            }
            o.events.AddRange(_events);
            _events.Clear();
            o.log_errors.AddRange(_logErrors);
            _logErrors.Clear();
            o.log_error_count = _errorCount;
            o.log_warning_count = _warningCount;
            _errorCount = _warningCount = 0;
        }

        private static UnitObs Describe(GameClient client, RemoteEntity e, GameObject go, Vector3 from, float yaw)
        {
            var to = go.transform.position - from;
            var u = new UnitObs
            {
                name = e.Name,
                kind = e.Guid.Type == EntityType.Player ? "player" : e.Guid.Type == EntityType.Creature ? "creature" : "object",
                distance = Round(Flat(to)),
                bearing = Round(Mathf.DeltaAngle(yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg)),
                lootable = client.IsLootable(e.Guid),
                reaction = "",
            };
            if (e.Unit != null)
            {
                var v = e.Unit.Values;
                u.level = v.Level;
                u.dead = v.IsDead;
                u.health_percent = v.MaxHealth > 0 ? Round(100f * v.Health / v.MaxHealth) : 0f;
                u.reaction = client.GetReaction(e.Guid).ToString().ToLowerInvariant();
            }
            return u;
        }

        // ------------------------------------------------------------------ events and logs

        private void Subscribe()
        {
            _subscribed = true;
            var c = Client;
            c.GmCatalogReceived += WriteGmCatalog;
            c.ChatReceived += m =>
            {
                AddEvent($"chat {m.Channel}: " + (string.IsNullOrEmpty(m.SenderName) ? "" : m.SenderName + ": ") + m.Text);
                if (m.Channel == ChatChannel.Error) Reject(m.Text);
            };
            c.ActionFailed += (error, value) =>
            {
                AddEvent($"action failed: {error} {value}");
                Reject($"{error} {value}");
            };
            c.GossipOpened += menu => _dialogsOpened++;
            c.VendorOpened += vendor => _vendorsOpened++;
            c.SpellFailed += f =>
            {
                if (f.Caster == c.LocalGuid) AddEvent($"spell failed: {SpellName(f.SpellId)} ({f.Result})");
            };
            c.CombatEventReceived += e => AddEvent($"combat: {NameOf(e.Source)} -> {NameOf(e.Target)} "
                                                   + $"{(e.SpellId == 0 ? "melee" : SpellName(e.SpellId))} {e.Amount} {e.Flags}");
            c.ExperienceReceived += (amount, rested, source) => AddEvent($"experience +{amount}");
            c.QuestProgressed += (quest, have, need) => AddEvent($"quest {quest}: {have}/{need}");
            c.Disconnected += reason => AddEvent("disconnected: " + reason);
        }

        private void Reject(string why)
        {
            _rejections++;
            _lastRejection = why ?? "";
        }

        private void AddEvent(string text)
        {
            if (_events.Count >= MaxEvents) _events.RemoveAt(0);
            _events.Add(text.Length > 200 ? text.Substring(0, 200) : text);
        }

        private void OnLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Warning)
            {
                _warningCount++;
                return;
            }
            if (type == LogType.Log) return;
            _errorCount++;
            if (_logErrors.Count < 10) _logErrors.Add($"{type}: " + (message.Length > 300 ? message.Substring(0, 300) : message));
        }

        // ------------------------------------------------------------------ helpers

        private EntityGuid Nearest(Vector3 from, float range, Func<RemoteEntity, bool> match)
        {
            var best = EntityGuid.Empty;
            float bestDistance = range;
            foreach (var e in Client.World.Entities)
            {
                if (e.Guid == Client.LocalGuid || !(e.View is GameObject go) || !match(e)) continue;
                float d = Vector3.Distance(from, go.transform.position);
                if (d <= bestDistance)
                {
                    bestDistance = d;
                    best = e.Guid;
                }
            }
            return best;
        }

        private float Distance(PlayerController player, EntityGuid guid) =>
            Client.World.TryGet(guid, out var e) && e.View is GameObject go
                ? Vector3.Distance(player.transform.position, go.transform.position) : -1f;

        private string NameOf(EntityGuid guid)
        {
            var c = Client;
            if (c == null || guid.IsEmpty) return "?";
            if (guid == c.LocalGuid) return "you";
            return c.World.TryGet(guid, out var e) ? e.Name : "?";
        }

        private string SpellName(uint id) => Client != null && Client.Data.TryGetSpell(id, out var s) ? s.Name : "spell " + id;

        private Vector3? PlayerPosition() =>
            _network != null && _network.LocalPlayer != null ? _network.LocalPlayer.transform.position : (Vector3?)null;

        private static void FaceYaw(PlayerController player, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            player.FaceTowards(player.transform.position + new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r)) * 10f);
        }

        private static void Ok(Observation o, string result)
        {
            o.ok = true;
            o.result = result;
        }

        private static void Fail(Observation o, string result)
        {
            o.ok = false;
            o.result = result;
        }

        private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
        private static float Wrap360(float degrees) => Mathf.Repeat(degrees, 360f);
        private static float Round(float v) => Mathf.Round(v * 10f) / 10f;

        private void WriteStatus()
        {
            _nextStatus = Time.unscaledTime + 0.5f;
            bool inWorld = _network != null && _network.InWorld;
            var status = new Status
            {
                state = _busy ? "busy" : inWorld ? "ready" : "waiting_for_world", in_world = inWorld, busy = _busy,
                next_seq = _nextSeq, frame = Time.frameCount, realtime = Time.realtimeSinceStartup,
            };
            string path = Path.Combine(_dir, "status.json");
            try
            {
                File.WriteAllText(path + ".tmp", JsonUtility.ToJson(status));
                File.Copy(path + ".tmp", path, true);
            }
            catch (IOException)
            {
                // The orchestrator may be reading it; the next heartbeat follows in half a second.
            }
        }

        /// <summary>Panel coordinates of the HUD document to Input System screen coordinates (origin bottom left).</summary>
        private Vector2 PanelToScreen(Vector2 panelPoint)
        {
            var panel = _hud.GetComponent<UnityEngine.UIElements.UIDocument>().rootVisualElement.panel;
            var p0 = UnityEngine.UIElements.RuntimePanelUtils.ScreenToPanel(panel, Vector2.zero);
            var p1 = UnityEngine.UIElements.RuntimePanelUtils.ScreenToPanel(panel, new Vector2(Screen.width, Screen.height));
            float x = (panelPoint.x - p0.x) * Screen.width / (p1.x - p0.x);
            float yFromTop = (panelPoint.y - p0.y) * Screen.height / (p1.y - p0.y);
            return new Vector2(x, Screen.height - yFromTop);
        }

        private static IEnumerator MouseClick(Vector2 screen)
        {
            var mouse = Mouse.current;
            if (mouse == null) yield break;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = screen });
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = screen }.WithButton(MouseButton.Left, true));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = screen }.WithButton(MouseButton.Left, false));
            yield return null;
            yield return null;
        }

        /// <summary>Discovery for automation: every GM command with its argument schema (from the shared table).</summary>
        private void WriteGmCommands()
        {
            var file = new GmCommandsFile();
            foreach (var info in GmCommands.All)
            {
                var spec = new GmCommandSpecObs
                {
                    id = info.Id, category = info.Category.ToString(), side = info.Side.ToString(),
                    persistent = info.Persistent, min_gm_level = info.MinGmLevel,
                };
                foreach (var a in info.Args)
                    spec.args.Add(new GmArgSpecObs { name = a.Name, type = a.Type.ToString(), min = a.Min, max = a.Max, optional = a.Optional });
                file.commands.Add(spec);
            }
            WriteJson(Path.Combine(_dir, "gm_commands.json"), JsonUtility.ToJson(file, true));
        }

        private void WriteGmCatalog()
        {
            var file = new GmCatalogFile();
            var catalog = Client.GmCatalog;
            foreach (var c in catalog.Creatures) file.creatures.Add($"{c.Id}|{c.Name}|{c.MinLevel}-{c.MaxLevel}|{c.Reaction}");
            foreach (var d in catalog.Destinations)
                file.destinations.Add($"{d.Id}|{d.Name}|{d.Kind}|{d.X.ToString("0.0", CultureInfo.InvariantCulture)}|{d.Z.ToString("0.0", CultureInfo.InvariantCulture)}");
            WriteJson(Path.Combine(_dir, "gm_catalog.json"), JsonUtility.ToJson(file, true));
        }

        private static void WriteJson(string path, string json)
        {
            File.WriteAllText(path + ".tmp", json);
            if (File.Exists(path)) File.Delete(path);
            File.Move(path + ".tmp", path);
        }
    }
}
#endif
