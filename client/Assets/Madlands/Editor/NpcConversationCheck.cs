using System;
using System.Reflection;
using Madlands.Client;
using Madlands.Shared;
using Madlands.Shared.Movement;
using UnityEditor;
using UnityEngine;

namespace Madlands.Client.Editor
{
    /// <summary>Checks the visual-only conversation turn, its return, and movement/death priority.</summary>
    public static class NpcConversationCheck
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly MethodInfo Tick = typeof(UnitView).GetMethod("UpdateConversationTurn", Private);
        private static readonly FieldInfo Turn = typeof(UnitView).GetField("_conversationTurn", Private);

        public static void Run()
        {
            try
            {
                Check(110); // Captain Edda: animated quest giver
                Check(111); // Old Tamsin: animated NPC
                Check(999999); // a display without an animation model
                CheckLocalPlayer();
                Debug.Log("[NpcConversationCheck] Facing, tracking, return, movement, death and local-player isolation passed.");
                EditorApplication.Exit(0);
            }
            catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }

        private static void Check(uint display)
        {
            var npc = new GameObject("NPC " + display);
            var player = new GameObject("Player");
            try
            {
                var view = UnitView.Attach(npc, EntityGuid.Create(EntityType.Creature, display), display, isLocal: false);
                npc.transform.rotation = Quaternion.Euler(0f, 35f, 0f);
                player.transform.position = new Vector3(10f, 0f, 0f);
                view.SetDead(false);
                view.SetMovement(MovementFlags.None, Vec3.Zero);
                view.SetConversationTarget(player.transform);
                Step(view, 0.1f);
                float first = Angle(view);
                if (first <= 0f || first >= 55f) throw new InvalidOperationException("Turn was not smooth: " + display);
                Step(view, 1f);
                Near(Angle(view), 55f, "Face player " + display);
                Near(npc.transform.eulerAngles.y, 35f, "Authoritative rotation changed " + display);

                player.transform.position = new Vector3(-10f, 0f, 0f);
                Step(view, 1f);
                Near(Angle(view), -125f, "Follow moved player " + display);
                view.SetConversationTarget(null);
                Step(view, 1f);
                Near(Angle(view), 0f, "Return after dialog " + display);

                view.SetConversationTarget(player.transform);
                Step(view, 1f);
                view.SetMovement(MovementFlags.Forward, new Vec3(0f, 0f, 2f));
                Step(view, 0.1f);
                Near(Angle(view), 0f, "Movement takes priority " + display);
                view.SetMovement(MovementFlags.None, Vec3.Zero);
                view.SetDead(true);
                Step(view, 1f);
                Near(Angle(view), 0f, "Death takes priority " + display);
            }
            finally { UnityEngine.Object.DestroyImmediate(npc); UnityEngine.Object.DestroyImmediate(player); }
        }

        private static void CheckLocalPlayer()
        {
            var go = new GameObject("Local");
            var other = new GameObject("Other");
            try
            {
                var local = UnitView.Attach(go, EntityGuid.Create(EntityType.Player, 1), 999999, isLocal: true);
                other.transform.position = Vector3.right * 10f;
                local.SetConversationTarget(other.transform);
                Step(local, 1f);
                Near(Angle(local), 0f, "Local player changed facing");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(other); }
        }

        private static void Step(UnitView view, float delta) => Tick.Invoke(view, new object[] { delta });
        private static float Angle(UnitView view) => (float)Turn.GetValue(view);
        private static void Near(float actual, float expected, string context)
        {
            if (Mathf.Abs(Mathf.DeltaAngle(actual, expected)) > 0.01f)
                throw new InvalidOperationException(context + ": " + actual + " expected " + expected);
        }
    }
}
