using System;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// The player's settings (mouse, interface scale, graphics, sound volumes), saved in PlayerPrefs. Window mode and
    /// resolution are our own keys too (Unity's own memory of them is overwritten by every start with
    /// -screen-width/-screen-height, such as the screenshot runs); they, quality and vsync are applied at startup.
    /// </summary>
    public static class Preferences
    {
        private const string Prefix = "madlands.pref.";

        public const float MinSensitivity = 0.03f;
        public const float MaxSensitivity = 0.5f;
        public const float MinUiScale = 0.75f;
        public const float MaxUiScale = 1.5f;

        static Preferences()
        {
            MouseSensitivity = Mathf.Clamp(GetFloat("sensitivity", 0.15f), MinSensitivity, MaxSensitivity);
            InvertMouseY = GetInt("invertY", 0) != 0;
            UiScale = Mathf.Clamp(GetFloat("uiScale", 1f), MinUiScale, MaxUiScale);
            VSync = GetInt("vsync", 1) != 0;
            QualityLevel = Mathf.Clamp(GetInt("quality", QualitySettings.GetQualityLevel()), 0, QualitySettings.names.Length - 1);
            MasterVolume = Mathf.Clamp01(GetFloat("volume.master", 0.8f));
            MusicVolume = Mathf.Clamp01(GetFloat("volume.music", 0.6f));
            EffectsVolume = Mathf.Clamp01(GetFloat("volume.effects", 0.9f));
            AmbienceVolume = Mathf.Clamp01(GetFloat("volume.ambience", 0.7f));
            Language = GetString("language", "en");
            DebugPanelPosition = new Vector2(GetFloat("debugPanel.x", -1f), GetFloat("debugPanel.y", -1f));
            ScreenWidth = GetInt("screen.width", 0);
            ScreenHeight = GetInt("screen.height", 0);
            WindowMode = (FullScreenMode)GetInt("screen.mode", (int)FullScreenMode.FullScreenWindow);
        }

        public static float MouseSensitivity { get; private set; }
        public static bool InvertMouseY { get; private set; }
        public static float UiScale { get; private set; }
        public static bool VSync { get; private set; }
        public static int QualityLevel { get; private set; }
        public static float MasterVolume { get; private set; }
        public static float MusicVolume { get; private set; }
        public static float EffectsVolume { get; private set; }
        public static float AmbienceVolume { get; private set; }
        /// <summary>Interface language code (Resources/Localization); applied on the next start.</summary>
        public static string Language { get; private set; }
        /// <summary>The chosen resolution (0 = never chosen: Unity's default) and window mode.</summary>
        public static int ScreenWidth { get; private set; }
        public static int ScreenHeight { get; private set; }
        public static FullScreenMode WindowMode { get; private set; }
        /// <summary>Where the F3 panel was dragged to (HUD coordinates); negative = not moved yet.</summary>
        public static Vector2 DebugPanelPosition { get; private set; }

        /// <summary>Raised after a setting changed.</summary>
        public static event Action Changed;

        /// <summary>Applies the graphics settings that Unity does not remember on its own.</summary>
        public static void ApplyGraphics()
        {
            if (QualitySettings.GetQualityLevel() != QualityLevel) QualitySettings.SetQualityLevel(QualityLevel, applyExpensiveChanges: true);
            QualitySettings.vSyncCount = VSync ? 1 : 0;
        }

        /// <summary>
        /// Puts the saved window mode and resolution back at startup. Not in the editor, and not when the
        /// command line chose a screen size itself (test and screenshot runs).
        /// </summary>
        public static void ApplyDisplay()
        {
            if (Application.isEditor || ScreenWidth <= 0 || ScreenHeight <= 0) return;
            foreach (var arg in Environment.GetCommandLineArgs())
                if (arg.StartsWith("-screen-", StringComparison.OrdinalIgnoreCase) || arg.Equals("-popupwindow", StringComparison.OrdinalIgnoreCase)) return;
            Screen.SetResolution(ScreenWidth, ScreenHeight, WindowMode);
        }

        /// <summary>Changes and remembers the window mode and resolution.</summary>
        public static void SetDisplay(int width, int height, FullScreenMode mode)
        {
            ScreenWidth = width;
            ScreenHeight = height;
            WindowMode = mode;
            try
            {
                PlayerPrefs.SetInt(Prefix + "screen.width", width);
                PlayerPrefs.SetInt(Prefix + "screen.height", height);
                PlayerPrefs.SetInt(Prefix + "screen.mode", (int)mode);
                PlayerPrefs.Save();
            }
            catch (Exception ex) { Debug.LogWarning("[Prefs] " + ex.Message); }
            Screen.SetResolution(width, height, mode);
            Changed?.Invoke();
        }

        public static void SetMouseSensitivity(float value)
        {
            MouseSensitivity = Mathf.Clamp(value, MinSensitivity, MaxSensitivity);
            SetFloat("sensitivity", MouseSensitivity);
        }

        public static void SetInvertMouseY(bool value)
        {
            InvertMouseY = value;
            SetInt("invertY", value ? 1 : 0);
        }

        public static void SetUiScale(float value)
        {
            UiScale = Mathf.Clamp(value, MinUiScale, MaxUiScale);
            SetFloat("uiScale", UiScale);
        }

        public static void SetVSync(bool value)
        {
            VSync = value;
            SetInt("vsync", value ? 1 : 0);
            ApplyGraphics();
        }

        public static void SetQualityLevel(int value)
        {
            QualityLevel = Mathf.Clamp(value, 0, QualitySettings.names.Length - 1);
            SetInt("quality", QualityLevel);
            ApplyGraphics();
        }

        public static void SetMasterVolume(float value) => SetFloat("volume.master", MasterVolume = Mathf.Clamp01(value));

        public static void SetMusicVolume(float value) => SetFloat("volume.music", MusicVolume = Mathf.Clamp01(value));

        public static void SetEffectsVolume(float value) => SetFloat("volume.effects", EffectsVolume = Mathf.Clamp01(value));

        public static void SetAmbienceVolume(float value) => SetFloat("volume.ambience", AmbienceVolume = Mathf.Clamp01(value));

        public static void SetDebugPanelPosition(Vector2 position)
        {
            DebugPanelPosition = position;
            try
            {
                PlayerPrefs.SetFloat(Prefix + "debugPanel.x", position.x);
                PlayerPrefs.SetFloat(Prefix + "debugPanel.y", position.y);
                PlayerPrefs.Save();
            }
            catch (Exception ex) { Debug.LogWarning("[Prefs] " + ex.Message); }
        }

        public static void SetLanguage(string code)
        {
            Language = code;
            try { PlayerPrefs.SetString(Prefix + "language", code); PlayerPrefs.Save(); }
            catch (Exception ex) { Debug.LogWarning("[Prefs] " + ex.Message); }
            Changed?.Invoke();
        }

        private static string GetString(string key, string fallback)
        {
            try { return PlayerPrefs.GetString(Prefix + key, fallback); }
            catch { return fallback; }
        }

        private static float GetFloat(string key, float fallback)
        {
            try { return PlayerPrefs.GetFloat(Prefix + key, fallback); }
            catch { return fallback; }
        }

        private static int GetInt(string key, int fallback)
        {
            try { return PlayerPrefs.GetInt(Prefix + key, fallback); }
            catch { return fallback; }
        }

        private static void SetFloat(string key, float value)
        {
            try { PlayerPrefs.SetFloat(Prefix + key, value); PlayerPrefs.Save(); }
            catch (Exception ex) { Debug.LogWarning("[Prefs] " + ex.Message); }
            Changed?.Invoke();
        }

        private static void SetInt(string key, int value)
        {
            try { PlayerPrefs.SetInt(Prefix + key, value); PlayerPrefs.Save(); }
            catch (Exception ex) { Debug.LogWarning("[Prefs] " + ex.Message); }
            Changed?.Invoke();
        }
    }
}
