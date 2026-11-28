using System;
using System.Collections.Generic;
using System.Reflection;
using Madlands.Shared;
using Madlands.Shared.Movement;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Madlands.Client.Editor
{
    /// <summary>Batch-mode regression check for jumping, landing and death on every playable hero.</summary>
    public static class JumpLandingCheck
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Run()
        {
            try
            {
                foreach (uint display in new uint[] { 3, 4, 5 }) CheckHero(display);
                Debug.Log("[JumpLandingCheck] All three heroes return to locomotion on landing; death is preserved.");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        private static void CheckHero(uint display)
        {
            var go = new GameObject("JumpLandingCheck " + display);
            UnitAnimator animator = null;
            try
            {
                var view = UnitView.Attach(go, EntityGuid.Create(EntityType.Player, display), display, isLocal: true);
                var model = (DisplayModel)typeof(UnitView).GetField("_model", PrivateInstance).GetValue(view);
                animator = (UnitAnimator)typeof(UnitView).GetField("_animator", PrivateInstance).GetValue(view);
                if (model == null || model.Animator == null || !model.Animator.isHuman || animator == null || !animator.Has("Jump"))
                    throw new InvalidOperationException("Hero " + display + " has no humanoid jump animation.");
                var slot = typeof(UnitAnimator).GetField("_fullBody", PrivateInstance).GetValue(animator);
                var playing = slot.GetType().GetField("Playing");
                var inputs = (Dictionary<string, int>)slot.GetType().GetField("Inputs").GetValue(slot);
                var layers = (AnimationLayerMixerPlayable)typeof(UnitAnimator).GetField("_layers", PrivateInstance).GetValue(animator);

                foreach (var (flags, expected) in new[]
                {
                    (MovementFlags.None, Locomotion.Idle),
                    (MovementFlags.Forward, Locomotion.Run),
                    (MovementFlags.Backward, Locomotion.RunBack),
                    (MovementFlags.StrafeLeft, Locomotion.StrafeLeft),
                })
                {
                    view.SetMovement(flags | MovementFlags.Airborne, new Vec3(0, 7f, 5f));
                    if ((int)playing.GetValue(slot) != inputs["Jump"]) throw new InvalidOperationException("Jump did not start: " + display);
                    animator.Update(0.15f);
                    view.SetMovement(flags, new Vec3(0, 0, flags == MovementFlags.None ? 0 : 5f));
                    if ((int)playing.GetValue(slot) != -1) throw new InvalidOperationException("Jump did not stop on landing: " + display);
                    var current = (Locomotion)typeof(UnitAnimator).GetField("_current", PrivateInstance).GetValue(animator);
                    if (current != expected) throw new InvalidOperationException($"Hero {display}: expected {expected}, got {current} after landing.");
                    animator.Update(0.15f);
                    if (layers.GetInputWeight(2) > 0.001f) throw new InvalidOperationException("Jump layer still overrides the landing: " + display);
                }

                // Dying during a jump replaces the jump. Landing must not cancel the held death pose.
                view.SetMovement(MovementFlags.Airborne, new Vec3(0, 7f, 0));
                view.SetDead(true);
                if ((int)playing.GetValue(slot) != inputs["Death"]) throw new InvalidOperationException("Death did not replace jump: " + display);
                view.SetMovement(MovementFlags.None, Vec3.Zero);
                if ((int)playing.GetValue(slot) != inputs["Death"]) throw new InvalidOperationException("Landing interrupted death: " + display);
                Debug.Log("[JumpLandingCheck] Hero " + display + " passed.");
            }
            finally
            {
                animator?.Destroy();
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
