using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Madlands.Client
{
    /// <summary>Everything a key can be bound to. Stable names: they are saved in the player's preferences.</summary>
    public enum GameAction
    {
        MoveForward,
        MoveBackward,
        TurnLeft,
        TurnRight,
        StrafeLeft,
        StrafeRight,
        Jump,
        AutoRun,
        TargetNextEnemy,
        OpenChat,
        ChatCommand,
        ToggleBags,
        ToggleCharacter,
        ToggleQuestLog,
        ToggleMap,
        ToggleDebug,
        Action1,
        Action2,
        Action3,
        Action4,
        Action5,
        Action6,
        Action7,
        Action8,
        Action9,
        Action10,
        Action11,
        Action12,
    }

    /// <summary>
    /// Rebindable keys: two per action (like WoW's primary and secondary binding), saved in PlayerPrefs.
    /// Game code asks <see cref="IsHeld"/> and <see cref="WasPressed"/> instead of reading keys directly.
    /// Esc is not bindable: it always closes things or opens the menu.
    /// </summary>
    public static class KeyBindings
    {
        public const int Slots = 2;
        private const string PrefsKey = "madlands.keys.v1";

        private static readonly Dictionary<GameAction, Key[]> Defaults = new Dictionary<GameAction, Key[]>
        {
            { GameAction.MoveForward, new[] { Key.W, Key.UpArrow } },
            { GameAction.MoveBackward, new[] { Key.S, Key.DownArrow } },
            { GameAction.TurnLeft, new[] { Key.A, Key.LeftArrow } },
            { GameAction.TurnRight, new[] { Key.D, Key.RightArrow } },
            { GameAction.StrafeLeft, new[] { Key.Q, Key.None } },
            { GameAction.StrafeRight, new[] { Key.E, Key.None } },
            { GameAction.Jump, new[] { Key.Space, Key.None } },
            { GameAction.AutoRun, new[] { Key.R, Key.NumLock } },
            { GameAction.TargetNextEnemy, new[] { Key.Tab, Key.None } },
            { GameAction.OpenChat, new[] { Key.Enter, Key.NumpadEnter } },
            { GameAction.ChatCommand, new[] { Key.Slash, Key.None } },
            { GameAction.ToggleBags, new[] { Key.B, Key.None } },
            { GameAction.ToggleCharacter, new[] { Key.C, Key.None } },
            { GameAction.ToggleQuestLog, new[] { Key.L, Key.None } },
            { GameAction.ToggleMap, new[] { Key.M, Key.None } },
            { GameAction.ToggleDebug, new[] { Key.F3, Key.None } },
            { GameAction.Action1, new[] { Key.Digit1, Key.None } },
            { GameAction.Action2, new[] { Key.Digit2, Key.None } },
            { GameAction.Action3, new[] { Key.Digit3, Key.None } },
            { GameAction.Action4, new[] { Key.Digit4, Key.None } },
            { GameAction.Action5, new[] { Key.Digit5, Key.None } },
            { GameAction.Action6, new[] { Key.Digit6, Key.None } },
            { GameAction.Action7, new[] { Key.Digit7, Key.None } },
            { GameAction.Action8, new[] { Key.Digit8, Key.None } },
            { GameAction.Action9, new[] { Key.Digit9, Key.None } },
            { GameAction.Action10, new[] { Key.Digit0, Key.None } },
            { GameAction.Action11, new[] { Key.Minus, Key.None } },
            { GameAction.Action12, new[] { Key.Equals, Key.None } },
        };

        private static readonly Dictionary<GameAction, Key[]> Bindings = new Dictionary<GameAction, Key[]>();

        static KeyBindings()
        {
            ResetToDefaults(save: false);
            Load();
        }

        /// <summary>Raised after any binding changed (the action bar relabels its buttons).</summary>
        public static event Action Changed;

        public static IEnumerable<GameAction> Actions => Defaults.Keys;

        public static GameAction ActionButton(int index) => GameAction.Action1 + index;

        public static Key Get(GameAction action, int slot) => Bindings[action][slot];

        public static bool IsHeld(GameAction action)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;
            foreach (var key in Bindings[action])
                if (key != Key.None && keyboard[key].isPressed) return true;
            return false;
        }

        public static bool WasPressed(GameAction action)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;
            foreach (var key in Bindings[action])
                if (key != Key.None && keyboard[key].wasPressedThisFrame) return true;
            return false;
        }

        /// <summary>Binds a key (None clears the slot). A key belongs to one action: it is taken from any other.</summary>
        public static void Bind(GameAction action, int slot, Key key)
        {
            if (key == Key.Escape) return;
            if (key != Key.None)
            {
                foreach (var keys in Bindings.Values)
                    for (int i = 0; i < keys.Length; i++)
                        if (keys[i] == key) keys[i] = Key.None;
            }
            Bindings[action][slot] = key;
            Save();
            Changed?.Invoke();
        }

        public static void ResetToDefaults(bool save = true)
        {
            Bindings.Clear();
            foreach (var pair in Defaults) Bindings[pair.Key] = (Key[])pair.Value.Clone();
            if (!save) return;
            Save();
            Changed?.Invoke();
        }

        /// <summary>The first key that was pressed this frame, if any (rebinding).</summary>
        public static Key PressedKey()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return Key.None;
            foreach (KeyControl control in keyboard.allKeys)
                if (control != null && control.wasPressedThisFrame) return control.keyCode;
            return Key.None;
        }

        /// <summary>Short text for a key, as shown on buttons ("1", "Num Lock", "Up").</summary>
        public static string KeyName(Key key)
        {
            switch (key)
            {
                case Key.None: return "";
                case Key.Minus: return "-";
                case Key.Equals: return "=";
                case Key.Slash: return "/";
                case Key.Backslash: return "\\";
                case Key.Comma: return ",";
                case Key.Period: return ".";
                case Key.Semicolon: return ";";
                case Key.Quote: return "'";
                case Key.LeftBracket: return "[";
                case Key.RightBracket: return "]";
                case Key.Backquote: return "`";
                case Key.NumpadEnter: return "Num Enter";
                case Key.NumLock: return "Num Lock";
                case Key.UpArrow: return "Up";
                case Key.DownArrow: return "Down";
                case Key.LeftArrow: return "Left";
                case Key.RightArrow: return "Right";
                case Key.LeftShift: return "L Shift";
                case Key.RightShift: return "R Shift";
                case Key.LeftCtrl: return "L Ctrl";
                case Key.RightCtrl: return "R Ctrl";
                case Key.LeftAlt: return "L Alt";
                case Key.RightAlt: return "R Alt";
            }
            var name = key.ToString();
            if (name.StartsWith("Digit", StringComparison.Ordinal)) return name.Substring(5);
            if (name.StartsWith("Numpad", StringComparison.Ordinal)) return "Num " + name.Substring(6);
            return name;
        }

        /// <summary>The primary key of an action, or its secondary if the primary is unbound.</summary>
        public static string ShortcutText(GameAction action)
        {
            var keys = Bindings[action];
            return KeyName(keys[0] != Key.None ? keys[0] : keys[1]);
        }

        public static string Label(GameAction action)
        {
            switch (action)
            {
                case GameAction.MoveForward: return "Move forward";
                case GameAction.MoveBackward: return "Move backward";
                case GameAction.TurnLeft: return "Turn left";
                case GameAction.TurnRight: return "Turn right";
                case GameAction.StrafeLeft: return "Strafe left";
                case GameAction.StrafeRight: return "Strafe right";
                case GameAction.Jump: return "Jump";
                case GameAction.AutoRun: return "Autorun";
                case GameAction.TargetNextEnemy: return "Target next enemy";
                case GameAction.OpenChat: return "Open chat";
                case GameAction.ChatCommand: return "Chat command (/)";
                case GameAction.ToggleBags: return "Bags";
                case GameAction.ToggleCharacter: return "Character";
                case GameAction.ToggleQuestLog: return "Quest log";
                case GameAction.ToggleMap: return "World map";
                case GameAction.ToggleDebug: return "Debug overlay";
                default: return "Action button " + (action - GameAction.Action1 + 1);
            }
        }

        private static void Save()
        {
            var sb = new StringBuilder();
            foreach (var pair in Bindings)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(pair.Key).Append('=').Append(pair.Value[0]).Append(',').Append(pair.Value[1]);
            }
            try
            {
                PlayerPrefs.SetString(PrefsKey, sb.ToString());
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Keys] Could not save key bindings: " + ex.Message);
            }
        }

        /// <summary>Reads saved bindings; unknown actions or keys (from other versions) are skipped.</summary>
        private static void Load()
        {
            string saved;
            try
            {
                saved = PlayerPrefs.GetString(PrefsKey, "");
            }
            catch
            {
                return;
            }
            foreach (var entry in saved.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = entry.Split('=');
                if (parts.Length != 2 || !Enum.TryParse(parts[0], out GameAction action) || !Bindings.ContainsKey(action)) continue;
                var keys = parts[1].Split(',');
                for (int i = 0; i < Slots && i < keys.Length; i++)
                    if (Enum.TryParse(keys[i], out Key key) && key != Key.Escape) Bindings[action][i] = key;
            }
        }
    }
}
