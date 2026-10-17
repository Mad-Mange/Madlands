using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;

namespace Madlands.Client.Editor
{
    /// <summary>
    /// Generates the persistent FrontEnd scene (build index 0): camera, networking, world view,
    /// UI Toolkit document with the login/character screens and the in-game HUD, targeting and
    /// spell effects. Map scenes (World_NNNN) are loaded additively on top of it when the server
    /// says which map the character is on.
    ///   Batch: -executeMethod Madlands.Client.Editor.FrontEndBuilder.BuildFromCommandLine
    /// </summary>
    public static class FrontEndBuilder
    {
        public const string ScenePath = "Assets/Madlands/Scenes/FrontEnd.unity";
        private const string UiDir = "Assets/Madlands/UI";
        private const string PanelSettingsPath = UiDir + "/MadlandsPanelSettings.asset";

        [MenuItem("Madlands/Rebuild Front End Scene")]
        public static void BuildMenu() => Build();

        public static void BuildFromCommandLine()
        {
            try
            {
                Build();
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        public static void Build()
        {
            var panelSettings = CreatePanelSettings();
            CreateUnlitMaterial();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.2f;
            camera.farClipPlane = 800f;
            camera.clearFlags = CameraClearFlags.Skybox;
            cameraObject.AddComponent<AudioListener>();
            var cameraController = cameraObject.AddComponent<CameraController>();

            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();

            var game = new GameObject("Game");
            var network = game.AddComponent<NetworkController>();
            var worldView = game.AddComponent<WorldView>();
            game.AddComponent<DebugOverlay>();
            var document = game.AddComponent<UIDocument>();
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UiDir + "/FrontEnd.uxml")
                       ?? throw new FileNotFoundException(UiDir + "/FrontEnd.uxml");
            // Set the serialized fields directly: assigning UIDocument.panelSettings from an editor
            // script is not persisted, which leaves the document without a panel (renders nothing).
            SetReference(document, "m_PanelSettings", panelSettings);
            SetReference(document, "sourceAsset", tree);
            var frontEnd = game.AddComponent<FrontEndController>();
            var targeting = game.AddComponent<TargetingController>();
            var hud = game.AddComponent<HudController>();
            var effects = game.AddComponent<SpellEffects>();
            var autoPlay = game.AddComponent<AutoPlay>();

            SetReference(network, "worldView", worldView);
            SetReference(network, "cameraController", cameraController);
            SetReference(frontEnd, "network", network);
            SetReference(frontEnd, "hud", hud);
            SetReference(targeting, "network", network);
            SetReference(hud, "network", network);
            SetReference(hud, "targeting", targeting);
            SetReference(effects, "network", network);
            SetReference(autoPlay, "network", network);
            SetReference(autoPlay, "hud", hud);
            SetReference(autoPlay, "targeting", targeting);

            if (new SerializedObject(document).FindProperty("m_PanelSettings").objectReferenceValue != panelSettings)
                throw new InvalidOperationException("UIDocument panel settings were not assigned.");

            EditorSceneManager.SaveScene(scene, ScenePath);
            SceneList.Apply();
            AssetDatabase.SaveAssets();
            Debug.Log("[FrontEndBuilder] Built " + ScenePath);
        }

        /// <summary>Self-lit material for glowing placeholder effects; in Resources/ so player builds include the shader.</summary>
        private static void CreateUnlitMaterial()
        {
            const string path = "Assets/Madlands/Resources/Unlit.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? throw new InvalidOperationException("URP Unlit shader not found.");
            AssetDatabase.CreateAsset(new Material(shader) { color = Color.white }, path);
        }

        private static PanelSettings CreatePanelSettings()
        {
            var existing = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            var settings = existing != null ? existing : ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(UiDir + "/MadlandsTheme.tss")
                                       ?? throw new FileNotFoundException(UiDir + "/MadlandsTheme.tss");
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            if (existing == null) AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            else EditorUtility.SetDirty(settings);
            return settings;
        }

        private static void SetReference(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    /// <summary>Build settings: FrontEnd first (it boots the game), then every map scene World_NNNN.</summary>
    public static class SceneList
    {
        public static void Apply()
        {
            var scenes = new List<EditorBuildSettingsScene>();
            if (File.Exists(FrontEndBuilder.ScenePath)) scenes.Add(new EditorBuildSettingsScene(FrontEndBuilder.ScenePath, true));
            var maps = AssetDatabase.FindAssets("t:Scene World_", new[] { "Assets/Madlands/Scenes" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetFileNameWithoutExtension(p).StartsWith("World_", StringComparison.Ordinal))
                .OrderBy(p => p, StringComparer.Ordinal);
            foreach (var path in maps) scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }

    /// <summary>Pressing Play in the editor always boots from the FrontEnd scene, whichever scene is open.</summary>
    [InitializeOnLoad]
    internal static class PlayModeStartScene
    {
        static PlayModeStartScene()
        {
            EditorApplication.delayCall += () =>
            {
                var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(FrontEndBuilder.ScenePath);
                if (scene != null) EditorSceneManager.playModeStartScene = scene;
            };
        }
    }
}
