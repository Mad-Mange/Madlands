using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Madlands.ClientCore;
using Madlands.Shared;
using Madlands.Shared.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Madlands.Client
{
    /// <summary>
    /// Login, character select/create, the in-game Esc menu and the settings window (UI Toolkit, FrontEnd.uxml).
    /// Talks to the account API through <see cref="AccountClient"/> and hands the chosen character
    /// to <see cref="NetworkController"/>. Async calls resume on the Unity main thread.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class FrontEndController : MonoBehaviour
    {
        private const string LastUserKey = "madlands.lastUser";
        private const string LastServerKey = "madlands.lastServer";
        private const string LastCharacterKey = "madlands.lastCharacter";
        private const float DeleteConfirmSeconds = 5f;

        [SerializeField] private NetworkController network;
        [SerializeField] private HudController hud;

        private AccountClient _account;
        private CharacterListResponse _list;
        private ulong _selectedId;
        private ulong _pendingDeleteId;
        private float _pendingDeleteUntil;

        private VisualElement _root, _loginScreen, _charactersScreen, _createScreen, _busyOverlay, _gameMenu;
        private TextField _username, _password, _server, _createName;
        private Label _loginStatus, _charactersStatus, _createStatus, _realmLabel, _busyLabel, _charactersEmpty;
        private ScrollView _characterList;
        private DropdownField _raceDropdown, _classDropdown;
        private Button _quickLogin;
        private SettingsUi _settings;
        private PanelSettings _panelSettings;
        private float _originalScale = 1f;

        private ClientSettings Settings => network.Settings;

        private void Awake()
        {
            if (network == null) network = GetComponent<NetworkController>();
            if (hud == null) hud = GetComponent<HudController>();
            Preferences.ApplyGraphics();
        }

        private void OnDestroy()
        {
            Preferences.Changed -= ApplyUiScale;
#if UNITY_EDITOR
            // The panel settings are an asset: in the editor a runtime change would stick to it.
            if (_panelSettings != null) _panelSettings.scale = _originalScale;
#endif
        }

        private void ApplyUiScale()
        {
            if (_panelSettings != null) _panelSettings.scale = _originalScale * Preferences.UiScale;
        }

        private void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            var r = doc.rootVisualElement;
            r.pickingMode = PickingMode.Ignore;
            if (_panelSettings == null && doc.panelSettings != null)
            {
                _panelSettings = doc.panelSettings;
                _originalScale = _panelSettings.scale;
                Preferences.Changed += ApplyUiScale;
                ApplyUiScale();
            }

            _root = r.Q("root");
            _loginScreen = r.Q("login-screen");
            _charactersScreen = r.Q("characters-screen");
            _createScreen = r.Q("create-screen");
            _busyOverlay = r.Q("busy-overlay");
            _gameMenu = r.Q("game-menu");
            _username = r.Q<TextField>("login-username");
            _password = r.Q<TextField>("login-password");
            _password.isPasswordField = true;
            _server = r.Q<TextField>("login-server");
            _quickLogin = r.Q<Button>("quick-login-button");
            _createName = r.Q<TextField>("create-name");
            _loginStatus = r.Q<Label>("login-status");
            _charactersStatus = r.Q<Label>("characters-status");
            _createStatus = r.Q<Label>("create-status");
            _realmLabel = r.Q<Label>("realm-label");
            _busyLabel = r.Q<Label>("busy-label");
            _charactersEmpty = r.Q<Label>("characters-empty");
            _characterList = r.Q<ScrollView>("character-list");
            _raceDropdown = r.Q<DropdownField>("create-race");
            _classDropdown = r.Q<DropdownField>("create-class");
            r.Q<Label>("version-label").text = $"v{Application.version} · protocol {ProtocolVersion.Current}";

            r.Q<Button>("login-button").clicked += () => _ = LoginAsync(register: false);
            r.Q<Button>("register-button").clicked += () => _ = LoginAsync(register: true);
            _quickLogin.text = "Quick login: " + ClientSettings.QuickLoginAccount;
            _quickLogin.clicked += () => _ = QuickLoginAsync();
            _server.RegisterValueChangedCallback(_ => UpdateQuickLogin());
            r.Q<Button>("enter-world-button").clicked += EnterWorld;
            r.Q<Button>("create-new-button").clicked += ShowCreate;
            r.Q<Button>("delete-button").clicked += () => _ = DeleteSelectedAsync();
            r.Q<Button>("logout-button").clicked += () => _ = LogoutAsync();
            r.Q<Button>("create-button").clicked += () => _ = CreateAsync();
            r.Q<Button>("create-cancel-button").clicked += () => Show(_charactersScreen);
            r.Q<Button>("menu-resume-button").clicked += () => SetMenuOpen(false);
            // Settings sit above everything, the menu included.
            var settingsLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            settingsLayer.AddToClassList("layer");
            _root.Add(settingsLayer);
            _settings = new SettingsUi(settingsLayer);
            r.Q<Button>("menu-settings-button").clicked += _settings.Open;
            r.Q<Button>("login-settings-button").clicked += _settings.Open;
            r.Q<Button>("menu-logout-button").clicked += () => { SetMenuOpen(false); network.LeaveWorld(); };
            r.Q<Button>("menu-quit-button").clicked += Application.Quit;

            // Enter submits, like any login form.
            _password.RegisterCallback<KeyDownEvent>(e => { if (IsSubmit(e)) _ = LoginAsync(register: false); }, TrickleDown.TrickleDown);
            _createName.RegisterCallback<KeyDownEvent>(e => { if (IsSubmit(e)) _ = CreateAsync(); }, TrickleDown.TrickleDown);

            network.EnteredWorld += OnEnteredWorld;
            network.LeftWorld += OnLeftWorld;
        }

        private void OnDisable()
        {
            network.EnteredWorld -= OnEnteredWorld;
            network.LeftWorld -= OnLeftWorld;
        }

        private void Start()
        {
            _server.value = Settings.Server != "127.0.0.1" ? Settings.Server : PlayerPrefsGet(LastServerKey, Settings.Server);
            _username.value = Settings.User ?? PlayerPrefsGet(LastUserKey, "");
            UpdateQuickLogin();
            Show(_loginScreen);
            if (System.Array.IndexOf(Settings.OpenWindows, "settings") >= 0) _settings.Open();
            if (Settings.AutoConnect) _ = AutoConnectAsync();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (_settings.IsOpen)
            {
                // The settings window takes the keyboard (rebinding); Esc only concerns it.
                InputGate.MenuOpen = true;
                _settings.Update();
                if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                {
                    _settings.HandleEscape();
                    if (!_settings.IsOpen) InputGate.MenuOpen = IsVisible(_gameMenu) || !network.InWorld;
                }
                return;
            }
            // Esc closes the menu, else the chat line or the target (HUD), else opens the menu.
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && network.InWorld)
            {
                if (IsVisible(_gameMenu)) SetMenuOpen(false);
                else if (hud == null || !hud.HandleEscape()) SetMenuOpen(true);
            }
            // Enter on the character screen enters the world with the selected character.
            if (keyboard != null && IsVisible(_charactersScreen) && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
                EnterWorld();
            if (_pendingDeleteId != 0 && Time.time > _pendingDeleteUntil) _pendingDeleteId = 0;
        }

        // ------------------------------------------------------------------ login

        private async Task LoginAsync(bool register)
        {
            if (IsVisible(_busyOverlay)) return;
            EnsureAccountClient();
            SetStatus(_loginStatus, "");
            SetBusy(register ? "Creating account..." : "Logging in...");
            var response = register
                ? await _account.RegisterAsync(_username.value, _password.value)
                : await _account.LoginAsync(_username.value, _password.value);
            SetBusy(null);
            if (!response.Ok)
            {
                SetStatus(_loginStatus, response.Message);
                return;
            }
            await OnLoggedInAsync();
        }

        /// <summary>
        /// Temporary playtest shortcut: logs in to <see cref="ClientSettings.QuickLoginAccount"/> with the
        /// dev password, creating the account if it does not exist.
        /// </summary>
        private async Task QuickLoginAsync()
        {
            if (IsVisible(_busyOverlay)) return;
            EnsureAccountClient();
            SetStatus(_loginStatus, "");
            string account = ClientSettings.QuickLoginAccount;
            SetBusy("Logging in as " + account + "...");
            var response = await _account.LoginOrRegisterAsync(account, ClientSettings.DevPassword);
            SetBusy(null);
            if (!response.Ok)
            {
                SetStatus(_loginStatus, response.Result == ApiResultCode.InvalidCredentials
                    ? $"'{account}' has another password. Give it the dev password with: Madlands.Server set-password {account} {ClientSettings.DevPassword}"
                    : response.Message);
                return;
            }
            _username.value = account;
            await OnLoggedInAsync();
        }

        private async Task OnLoggedInAsync()
        {
            PlayerPrefs.SetString(LastUserKey, _username.value.Trim());
            PlayerPrefs.SetString(LastServerKey, _server.value.Trim());
            _password.value = "";
            await ShowCharactersAsync();
        }

        /// <summary>The quick-login button is a playtest shortcut: only offered for servers on this machine or the local network.</summary>
        private void UpdateQuickLogin() => _quickLogin.EnableInClassList("hidden", !IsLocalNetwork(_server.value));

        private async Task AutoConnectAsync()
        {
            EnsureAccountClient();
            SetBusy("Logging in as " + Settings.User + "...");
            var auth = await _account.LoginOrRegisterAsync(Settings.User, Settings.Password);
            if (!auth.Ok)
            {
                SetBusy(null);
                SetStatus(_loginStatus, auth.Message);
                return;
            }
            if (!await RefreshCharactersAsync()) return;

            var wanted = NameRules.NormalizeCharacterName(Settings.CharacterName);
            var existing = _list.Characters.FirstOrDefault(c => c.Name == wanted);
            if (existing.Id == 0 && wanted != null && _list.Races.Count > 0 && _list.Classes.Count > 0)
            {
                var created = await _account.CreateCharacterAsync(wanted, _list.Races[0].Id, ClassForAutoConnect());
                if (created.Ok)
                {
                    existing = created.Character;
                    await RefreshCharactersAsync();
                }
                else SetStatus(_charactersStatus, created.Message);
            }
            SetBusy(null);
            Show(_charactersScreen);
            if (existing.Id != 0) Select(existing.Id);
            if (Settings.AutoEntersWorld && existing.Id != 0) EnterWorld();
        }

        /// <summary>-class name (prefix) or id; the first class otherwise.</summary>
        private byte ClassForAutoConnect()
        {
            var wanted = Settings.ClassName;
            if (!string.IsNullOrEmpty(wanted))
            {
                foreach (var c in _list.Classes)
                    if (c.Name.StartsWith(wanted, System.StringComparison.OrdinalIgnoreCase) || c.Id.ToString() == wanted) return c.Id;
            }
            return _list.Classes[0].Id;
        }

        private async Task LogoutAsync()
        {
            if (_account != null) await _account.LogoutAsync();
            _list = null;
            _selectedId = 0;
            SetStatus(_loginStatus, "");
            Show(_loginScreen);
        }

        private void EnsureAccountClient()
        {
            string url = Settings.AutoConnect ? Settings.AccountApiUrl : AccountClient.BaseUrlFor(_server.value);
            if (_account != null && _account.BaseUrl == url) return;
            _account?.Dispose();
            _account = new AccountClient(url);
        }

        // ------------------------------------------------------------------ characters

        private async Task ShowCharactersAsync(string status = "")
        {
            Show(_charactersScreen);
            SetStatus(_charactersStatus, status, ok: false);
            await RefreshCharactersAsync();
        }

        /// <returns>False if the session expired (the login screen is shown instead).</returns>
        private async Task<bool> RefreshCharactersAsync()
        {
            var list = await _account.GetCharactersAsync();
            if (list.Result == ApiResultCode.Unauthorized)
            {
                SetBusy(null);
                Show(_loginScreen);
                SetStatus(_loginStatus, list.Message);
                return false;
            }
            if (!list.Ok)
            {
                SetStatus(_charactersStatus, list.Message);
                return true;
            }

            _list = list;
            var auth = _account.LastAuth;
            _realmLabel.text = auth != null ? $"{auth.RealmName} · {list.Characters.Count}/{list.MaxCharacters} characters" : "";
            _characterList.Clear();
            foreach (var c in list.Characters) _characterList.Add(CreateEntry(c));
            _charactersEmpty.style.display = list.Characters.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;

            if (list.Characters.All(c => c.Id != _selectedId)) _selectedId = LastPlayedOrFirst(list.Characters);
            Select(_selectedId);
            return true;
        }

        private VisualElement CreateEntry(CharacterSummary c)
        {
            var entry = new VisualElement { userData = c.Id };
            entry.AddToClassList("character-entry");
            var name = new Label(c.Name);
            name.AddToClassList("character-name");
            var details = new Label($"Level {c.Level} {OptionName(_list.Races, c.Race)} {OptionName(_list.Classes, c.Class)}");
            details.AddToClassList("character-details");
            entry.Add(name);
            entry.Add(details);
            entry.RegisterCallback<ClickEvent>(e =>
            {
                Select(c.Id);
                if (e.clickCount == 2) EnterWorld();
            });
            return entry;
        }

        private void Select(ulong id)
        {
            _selectedId = id;
            foreach (var child in _characterList.Children())
                child.EnableInClassList("selected", child.userData is ulong childId && childId == id);
        }

        private void EnterWorld()
        {
            var auth = _account?.LastAuth;
            if (_selectedId == 0 || auth == null || IsVisible(_busyOverlay)) return;
            PlayerPrefs.SetString(LastCharacterKey, _selectedId.ToString());
            SetStatus(_charactersStatus, "");
            SetBusy("Entering the world...");
            // A dev server bound to loopback announces 127.0.0.1; if we reached its API on another
            // address, the game port is on that address too.
            string apiHost = new System.Uri(_account.BaseUrl).Host;
            string host = IsLoopback(auth.RealmHost) && !IsLoopback(apiHost) ? apiHost : auth.RealmHost;
            network.EnterWorld(host, auth.RealmPort, _account.SessionToken, _selectedId);
        }

        private async Task DeleteSelectedAsync()
        {
            if (_selectedId == 0 || _list == null) return;
            var character = _list.Characters.First(c => c.Id == _selectedId);
            if (_pendingDeleteId != _selectedId)
            {
                _pendingDeleteId = _selectedId;
                _pendingDeleteUntil = Time.time + DeleteConfirmSeconds;
                SetStatus(_charactersStatus, $"Click Delete again to permanently delete {character.Name}.");
                return;
            }
            _pendingDeleteId = 0;
            var result = await _account.DeleteCharacterAsync(character.Id);
            SetStatus(_charactersStatus, result.Ok ? $"{character.Name} was deleted." : result.Message, result.Ok);
            await RefreshCharactersAsync();
        }

        private void ShowCreate()
        {
            if (_list == null) return;
            _raceDropdown.choices = _list.Races.Select(r => r.Name).ToList();
            _classDropdown.choices = _list.Classes.Select(c => c.Name).ToList();
            if (_raceDropdown.index < 0) _raceDropdown.index = 0;
            if (_classDropdown.index < 0) _classDropdown.index = 0;
            _createName.value = "";
            SetStatus(_createStatus, "");
            Show(_createScreen);
            _createName.Focus();
        }

        private async Task CreateAsync()
        {
            if (_list == null || IsVisible(_busyOverlay)) return;
            var name = NameRules.NormalizeCharacterName(_createName.value);
            if (name == null)
            {
                SetStatus(_createStatus, $"Names are {NameRules.CharacterNameMin}-{NameRules.CharacterNameMax} letters, no spaces or digits.");
                return;
            }
            SetBusy("Creating " + name + "...");
            var race = _list.Races[Mathf.Max(0, _raceDropdown.index)].Id;
            var cls = _list.Classes[Mathf.Max(0, _classDropdown.index)].Id;
            var created = await _account.CreateCharacterAsync(name, race, cls);
            SetBusy(null);
            if (!created.Ok)
            {
                SetStatus(_createStatus, created.Message);
                return;
            }
            _selectedId = created.Character.Id;
            await ShowCharactersAsync();
        }

        // ------------------------------------------------------------------ world transitions

        private void OnEnteredWorld()
        {
            SetBusy(null);
            Show(null);
        }

        private void OnLeftWorld(string reason, RejectReason? rejectReason)
        {
            SetMenuOpen(false);
            SetBusy(null);
            if (_account == null || rejectReason == RejectReason.InvalidSession)
            {
                Show(_loginScreen);
                SetStatus(_loginStatus, rejectReason == RejectReason.InvalidSession ? "Your session has expired. Please log in again." : reason);
                return;
            }
            bool userLeft = reason == "DisconnectPeerCalled";
            _ = ShowCharactersAsync(userLeft ? "" : "Disconnected: " + reason);
        }

        // ------------------------------------------------------------------ helpers

        private void Show(VisualElement screen)
        {
            // A hidden text field must not keep the keyboard focus (its Enter would submit the old form again).
            _root.panel?.focusController?.focusedElement?.Blur();
            foreach (var s in new[] { _loginScreen, _charactersScreen, _createScreen })
                s.EnableInClassList("hidden", s != screen);
            // In the world the root is transparent; on the front-end screens it hides the empty scene.
            _root.EnableInClassList("frontend", screen != null);
            InputGate.MenuOpen = screen != null || IsVisible(_gameMenu);
            if (screen == _loginScreen) (_username.value.Length > 0 ? _password : _username).Focus();
        }

        private void SetMenuOpen(bool open)
        {
            _gameMenu.EnableInClassList("hidden", !open);
            if (!open) _settings.Close();
            InputGate.MenuOpen = open || !network.InWorld;
        }

        private void SetBusy(string message)
        {
            _busyOverlay.EnableInClassList("hidden", message == null);
            if (message != null) _busyLabel.text = message;
        }

        private static void SetStatus(Label label, string text, bool ok = false)
        {
            label.text = text ?? "";
            label.EnableInClassList("ok", ok);
        }

        private static bool IsVisible(VisualElement e) => !e.ClassListContains("hidden");

        private static bool IsSubmit(KeyDownEvent e) => e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter;

        private static string OptionName(List<NamedOption> options, byte id)
        {
            foreach (var o in options) if (o.Id == id) return o.Name;
            return "?";
        }

        private static bool IsLoopback(string host) => host == "127.0.0.1" || host == "localhost" || host == "::1";

        /// <summary>This machine or a private network address (10/8, 172.16/12, 192.168/16).</summary>
        private static bool IsLocalNetwork(string server)
        {
            string host;
            try
            {
                host = new System.Uri(AccountClient.BaseUrlFor(server)).Host;
            }
            catch (System.UriFormatException)
            {
                return false;
            }
            if (host == "localhost") return true;
            if (!System.Net.IPAddress.TryParse(host.Trim('[', ']'), out var ip)) return false;
            if (System.Net.IPAddress.IsLoopback(ip)) return true;
            var b = ip.GetAddressBytes();
            return b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168));
        }

        private static ulong LastPlayedOrFirst(IReadOnlyList<CharacterSummary> characters)
        {
            if (characters.Count == 0) return 0;
            string last = PlayerPrefsGet(LastCharacterKey, "");
            foreach (var c in characters)
                if (c.Id.ToString() == last) return c.Id;
            return characters[0].Id;
        }

        private static string PlayerPrefsGet(string key, string fallback)
        {
            try
            {
                return PlayerPrefs.GetString(key, fallback);
            }
            catch
            {
                return fallback;
            }
        }
    }
}
