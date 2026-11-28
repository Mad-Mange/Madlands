using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Madlands.Shared;
using Madlands.Shared.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Madlands.Client.Editor
{
    /// <summary>Render the real warrior's idle, run and attack poses with sword and shield.</summary>
    public static class WarriorEquipmentCheck
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Run()
        {
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.65f, 0.65f, 0.65f);
                var light = new GameObject("PreviewLight").AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
                var go = new GameObject("WarriorPreview");
                var view = UnitView.Attach(go, EntityGuid.Create(EntityType.Player, 1), EntityVisuals.WarriorDisplay, true);
                view.SetEquipment(ItemVisuals.Shortsword, ItemVisuals.Buckler);
                var model = (DisplayModel)typeof(UnitView).GetField("_model", Flags).GetValue(view);
                var main = (Transform)typeof(UnitView).GetField("_mainHand", Flags).GetValue(view);
                var off = (Transform)typeof(UnitView).GetField("_offHand", Flags).GetValue(view);
                if (model == null || main == null || off == null || !model.Clips.ContainsKey("Run")) throw new InvalidOperationException("Warrior equipment or run clip missing");

                var camera = new GameObject("PreviewCamera").AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.18f, 0.19f, 0.21f);
                camera.orthographic = true;
                camera.orthographicSize = 1.3f;
                camera.targetTexture = new RenderTexture(768, 768, 24, RenderTextureFormat.ARGB32);
                var poseArms = typeof(UnitView).GetMethod("HoldArmsOut", Flags);
                var poseWeapon = typeof(UnitView).GetMethod("TurnWeaponForward", Flags);
                var steadyWeapon = typeof(UnitView).GetMethod("SteadyWarriorWeapon", Flags);
                var armsOut = typeof(UnitView).GetField("_armsOut", Flags);
                var weaponForward = typeof(UnitView).GetField("_weaponForward", Flags);
                var stability = typeof(UnitView).GetField("_runWeaponStability", Flags);
                string path = Path.Combine(MapExporter.RepoRoot, "builds", "warrior-equipment-preview");
                Directory.CreateDirectory(path);
                var runDirections = new List<Vector3>();
                AnimationMode.StartAnimationMode();
                try
                {
                    foreach (var (clipName, step) in new[] { ("Idle", 0), ("Run", 0), ("Run", 1), ("Run", 2), ("Run", 3), ("Attack1H", 1) })
                    {
                        var clip = model.Clips[clipName];
                        float fraction = clipName == "Idle" ? 0.25f : clipName == "Attack1H" ? 0.35f : step / 4f;
                        view.SetMovement(clipName == "Run" ? MovementFlags.Forward : MovementFlags.None, new Vec3(0, 0, clipName == "Run" ? 5.36f : 0));
                        AnimationMode.BeginSampling();
                        AnimationMode.SampleAnimationClip(model.Root.gameObject, clip, clip.length * fraction);
                        AnimationMode.EndSampling();
                        if (clipName != "Attack1H")
                        {
                            armsOut.SetValue(view, 1f);
                            weaponForward.SetValue(view, 1f);
                            poseArms.Invoke(view, null);
                            poseWeapon.Invoke(view, null);
                            stability.SetValue(view, clipName == "Run" ? 1f : 0f);
                            steadyWeapon.Invoke(view, null);
                        }
                        else
                        {
                            view.PlayLunge();
                            stability.SetValue(view, 1f);
                            var attackHand = model.RightHand.rotation;
                            steadyWeapon.Invoke(view, null);
                            if (Quaternion.Angle(attackHand, model.RightHand.rotation) > 0.01f)
                                throw new InvalidOperationException("Running correction changed the attack pose.");
                        }
                        var shieldMesh = off.GetComponentInChildren<MeshFilter>();
                        var size = shieldMesh.sharedMesh.bounds.size;
                        var localNormal = size.x < size.y && size.x < size.z ? Vector3.right : size.y < size.z ? Vector3.up : Vector3.forward;
                        var shieldNormal = shieldMesh.transform.TransformDirection(localNormal).normalized;
                        var leftHand = model.Bone("LeftHand");
                        var leftForearm = model.Bone("LeftLowerArm");
                        if (clipName == "Idle")
                        {
                            var outward = leftHand.position - model.Bone("Chest").position;
                            outward.y = 0f;
                            if (Vector3.Dot(shieldNormal, outward.normalized) < 0.5f)
                                throw new InvalidOperationException("Shield face points toward the body.");
                        }
                        if (clipName == "Run") runDirections.Add(main.up.normalized);
                        Debug.Log($"[WarriorEquipmentCheck] {clipName} {fraction:F2}: sword={main.up:F3}, shield normal={shieldNormal:F3}, arm={(leftHand.position - leftForearm.position).normalized:F3}, shield center={shieldMesh.GetComponent<Renderer>().bounds.center:F3}, hand={leftHand.position:F3}");
                        foreach (var (side, eye) in new[] { ("front", new Vector3(3.3f, 2.0f, 4.4f)), ("shield", new Vector3(-3.7f, 1.9f, 2.3f)) })
                        {
                            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(new Vector3(0f, 1f, 0f) - eye));
                            camera.Render();
                            var old = RenderTexture.active;
                            RenderTexture.active = camera.targetTexture;
                            var image = new Texture2D(768, 768, TextureFormat.RGB24, false);
                            image.ReadPixels(new Rect(0, 0, 768, 768), 0, 0);
                            image.Apply();
                            RenderTexture.active = old;
                            File.WriteAllBytes(Path.Combine(path, $"{clipName}-{step}-{side}.png"), image.EncodeToPNG());
                            UnityEngine.Object.DestroyImmediate(image);
                        }
                    }
                    float largestSwing = 0f;
                    foreach (var a in runDirections)
                        foreach (var b in runDirections) largestSwing = Mathf.Max(largestSwing, Vector3.Angle(a, b));
                    if (largestSwing > 20f || largestSwing < 3f)
                        throw new InvalidOperationException($"Run sword variation was {largestSwing:F1} degrees.");
                    Debug.Log($"[WarriorEquipmentCheck] Sword variation across the run: {largestSwing:F1} degrees; attack pose unchanged.");
                }
                finally { AnimationMode.StopAnimationMode(); }
                CheckIsolation(EntityVisuals.KorrinWardenDisplay, ItemVisuals.Shortsword);
                CheckIsolation(EntityVisuals.WarriorDisplay, ItemVisuals.GreatAxe);
                foreach (uint shield in new[] { ItemVisuals.Buckler, ItemVisuals.KnightShield, ItemVisuals.WardenShield }) CheckShield(shield);
                ((UnitAnimator)typeof(UnitView).GetField("_animator", Flags).GetValue(view)).Destroy();
                UnityEngine.Object.DestroyImmediate(go);
                Debug.Log("[WarriorEquipmentCheck] Preview complete: " + path);
                EditorApplication.Exit(0);
            }
            catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }

        private static void CheckShield(uint shield)
        {
            var go = new GameObject("ShieldIsolation");
            UnitAnimator animator = null;
            try
            {
                var view = UnitView.Attach(go, EntityGuid.Create(EntityType.Player, shield), EntityVisuals.WarriorDisplay, true);
                view.SetEquipment(0, shield);
                var model = (DisplayModel)typeof(UnitView).GetField("_model", Flags).GetValue(view);
                animator = (UnitAnimator)typeof(UnitView).GetField("_animator", Flags).GetValue(view);
                AnimationMode.StartAnimationMode();
                try
                {
                    var idle = model.Clips["Idle"];
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(model.Root.gameObject, idle, idle.length * 0.25f);
                    AnimationMode.EndSampling();
                    typeof(UnitView).GetField("_armsOut", Flags).SetValue(view, 1f);
                    typeof(UnitView).GetMethod("HoldArmsOut", Flags).Invoke(view, null);
                    var off = (Transform)typeof(UnitView).GetField("_offHand", Flags).GetValue(view);
                    var mesh = off.GetComponentInChildren<MeshFilter>();
                    var size = mesh.sharedMesh.bounds.size;
                    var axis = size.x < size.y && size.x < size.z ? Vector3.right : size.y < size.z ? Vector3.up : Vector3.forward;
                    var normal = mesh.transform.TransformDirection(axis).normalized;
                    var outward = model.Bone("LeftHand").position - model.Bone("Chest").position;
                    outward.y = 0f;
                    if (Vector3.Dot(normal, outward.normalized) < 0.5f)
                        throw new InvalidOperationException("Shield " + shield + " faces the body in idle.");
                }
                finally { AnimationMode.StopAnimationMode(); }
            }
            finally
            {
                animator?.Destroy();
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static void CheckIsolation(uint display, uint item)
        {
            var go = new GameObject("EquipmentIsolation");
            UnitAnimator animator = null;
            try
            {
                var view = UnitView.Attach(go, EntityGuid.Create(EntityType.Player, display), display, true);
                view.SetEquipment(item, 0);
                view.SetMovement(MovementFlags.Forward, new Vec3(0, 0, 5.36f));
                var model = (DisplayModel)typeof(UnitView).GetField("_model", Flags).GetValue(view);
                animator = (UnitAnimator)typeof(UnitView).GetField("_animator", Flags).GetValue(view);
                typeof(UnitView).GetField("_runWeaponStability", Flags).SetValue(view, 1f);
                var before = model.RightHand.rotation;
                typeof(UnitView).GetMethod("SteadyWarriorWeapon", Flags).Invoke(view, null);
                if (Quaternion.Angle(before, model.RightHand.rotation) > 0.01f)
                    throw new InvalidOperationException($"Running correction affected display {display}, item {item}.");
            }
            finally
            {
                animator?.Destroy();
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
