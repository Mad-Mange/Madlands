using System.Collections.Generic;
using Madlands.ClientCore;
using Madlands.Shared;
using Madlands.Shared.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace Madlands.Client
{
    /// <summary>
    /// Screen-space elements that follow units: nameplates (name, health, cast) and floating combat
    /// text (damage, heals, misses, experience). Positions are projected from the unit views each frame.
    /// Nameplates can be clicked like the unit itself (left: select, right: select and use).
    /// </summary>
    public sealed class WorldOverlay
    {
        private const float NameplateRange = 45f;
        private const float TextSeconds = 1.4f;
        private const float TextRisePixels = 70f;

        private sealed class Nameplate
        {
            public VisualElement Root;
            public Label Mark;
            public VisualElement Icon;
            public Label Name;
            public Label Title;
            public NpcRole Role;
            public VisualElement Fill;
            public VisualElement Cast;
            public VisualElement CastFill;
            public bool Seen;
        }

        private sealed class FloatingText
        {
            public Label Label;
            public EntityGuid Anchor;
            public Vector3 LastWorld;
            public float Start;
            public float OffsetX;
        }

        private readonly VisualElement _nameplateLayer;
        private readonly VisualElement _textLayer;
        private readonly Dictionary<EntityGuid, Nameplate> _nameplates = new Dictionary<EntityGuid, Nameplate>();
        private readonly List<EntityGuid> _stale = new List<EntityGuid>();
        private readonly List<FloatingText> _texts = new List<FloatingText>();

        /// <summary>A nameplate was clicked: the unit, and whether it was the right button.</summary>
        public event System.Action<EntityGuid, bool> Clicked;

        public WorldOverlay(VisualElement nameplateLayer, VisualElement textLayer)
        {
            _nameplateLayer = nameplateLayer;
            _textLayer = textLayer;
        }

        public void Clear()
        {
            _nameplateLayer.Clear();
            _textLayer.Clear();
            _nameplates.Clear();
            _texts.Clear();
        }

        // ------------------------------------------------------------------ floating combat text

        public void Show(EntityGuid anchor, string text, string cssClass)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("fct");
            if (cssClass != null) foreach (var c in cssClass.Split(' ')) label.AddToClassList(c);
            _textLayer.Add(label);
            _texts.Add(new FloatingText { Label = label, Anchor = anchor, Start = Time.time, OffsetX = Random.Range(-40f, 40f) });
        }

        // ------------------------------------------------------------------ per frame

        public void Update(GameClient client, WorldView world, Camera cam, IPanel panel)
        {
            if (cam == null || panel == null) return;
            UpdateNameplates(client, world, cam, panel);
            UpdateTexts(world, cam, panel);
        }

        private void UpdateNameplates(GameClient client, WorldView world, Camera cam, IPanel panel)
        {
            foreach (var plate in _nameplates.Values) plate.Seen = false;
            var camPos = cam.transform.position;
            double now = client.LocalTimeMs;
            foreach (var entity in world.Remote)
            {
                if (entity.Unit == null || !world.TryGetView(entity.Guid, out var view)) continue;
                var head = view.HeadPosition;
                if ((head - camPos).sqrMagnitude > NameplateRange * NameplateRange) continue;
                var viewport = cam.WorldToViewportPoint(head);
                if (viewport.z <= 0f) continue;

                if (!_nameplates.TryGetValue(entity.Guid, out var plate))
                {
                    plate = CreateNameplate(entity.Guid, entity.Name);
                    _nameplates.Add(entity.Guid, plate);
                }
                plate.Seen = true;
                var values = entity.Unit.Values;
                var reaction = client.GetReaction(entity.Guid);
                bool isPlayer = entity.Guid.Type == EntityType.Player;
                plate.Root.EnableInClassList("hostile", reaction == Reaction.Hostile);
                plate.Root.EnableInClassList("neutral", reaction == Reaction.Neutral);
                plate.Root.EnableInClassList("player", isPlayer && reaction == Reaction.Friendly);
                plate.Root.EnableInClassList("targeted", entity.Guid == client.Target);
                plate.Root.EnableInClassList("dead", values.IsDead);
                plate.Root.EnableInClassList("npc", values.NpcFlags != NpcFlags.None && reaction == Reaction.Friendly);
                var status = values.IsDead ? QuestGiverStatus.None : client.GetQuestGiverStatus(entity.Guid);
                plate.Mark.text = status == QuestGiverStatus.Available ? "!" : status == QuestGiverStatus.None ? "" : "?";
                plate.Mark.EnableInClassList("incomplete", status == QuestGiverStatus.Incomplete);
                plate.Mark.style.display = status == QuestGiverStatus.None ? DisplayStyle.None : DisplayStyle.Flex;
                // What the NPC is: its symbol (unless a quest mark is shown) and its title under the name.
                var role = reaction == Reaction.Friendly ? values.NpcRole : NpcRole.None;
                if (role != plate.Role)
                {
                    plate.Role = role;
                    var icon = NpcRoleUi.Icon(role);
                    plate.Icon.style.backgroundImage = icon != null ? new StyleBackground(icon) : new StyleBackground(StyleKeyword.None);
                    plate.Title.text = NpcRoleUi.Title(role) ?? "";
                }
                bool showIcon = status == QuestGiverStatus.None && role != NpcRole.None && !values.IsDead;
                bool showTitle = role != NpcRole.None;
                plate.Icon.style.display = showIcon ? DisplayStyle.Flex : DisplayStyle.None;
                plate.Title.style.display = showTitle ? DisplayStyle.Flex : DisplayStyle.None;
                plate.Name.text = values.Level > 0 && !isPlayer ? entity.Name + "  " + values.Level : entity.Name;
                plate.Fill.style.width = Length.Percent(values.HealthFraction * 100f);

                var cast = entity.Unit.Cast;
                plate.Cast.style.display = cast != null ? DisplayStyle.Flex : DisplayStyle.None;
                if (cast != null) plate.CastFill.style.width = Length.Percent(cast.Progress(now) * 100f);

                var p = RuntimePanelUtils.CameraTransformWorldToPanel(panel, head, cam);
                plate.Root.style.left = p.x;
                plate.Root.style.top = p.y - 33f - (showTitle ? 17f : 0f) - (status != QuestGiverStatus.None ? 42f : showIcon ? 34f : 0f);
                plate.Root.style.display = DisplayStyle.Flex;
            }

            _stale.Clear();
            foreach (var (guid, plate) in _nameplates)
            {
                if (plate.Seen) continue;
                if (world.TryGetView(guid, out _)) plate.Root.style.display = DisplayStyle.None;
                else _stale.Add(guid);
            }
            foreach (var guid in _stale)
            {
                _nameplates[guid].Root.RemoveFromHierarchy();
                _nameplates.Remove(guid);
            }
        }

        private Nameplate CreateNameplate(EntityGuid guid, string name)
        {
            var root = new VisualElement { pickingMode = PickingMode.Position };
            root.AddToClassList("nameplate");
            root.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            root.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.button == 0 || e.button == 1) Clicked?.Invoke(guid, e.button == 1);
                e.StopPropagation();
            });
            var mark = new Label { pickingMode = PickingMode.Ignore };
            mark.AddToClassList("quest-mark");
            mark.style.display = DisplayStyle.None;
            root.Add(mark);
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("nameplate-icon");
            icon.style.display = DisplayStyle.None;
            root.Add(icon);
            var label = new Label(name) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("nameplate-name");
            var title = new Label { pickingMode = PickingMode.Ignore };
            title.AddToClassList("nameplate-title");
            title.style.display = DisplayStyle.None;
            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.AddToClassList("nameplate-bar");
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("bar-fill");
            bar.Add(fill);
            var cast = new VisualElement { pickingMode = PickingMode.Ignore };
            cast.AddToClassList("nameplate-cast");
            var castFill = new VisualElement { pickingMode = PickingMode.Ignore };
            castFill.AddToClassList("bar-fill");
            cast.Add(castFill);
            root.Add(label);
            root.Add(title);
            root.Add(bar);
            root.Add(cast);
            _nameplateLayer.Add(root);
            return new Nameplate { Root = root, Mark = mark, Icon = icon, Name = label, Title = title, Fill = fill, Cast = cast, CastFill = castFill };
        }

        private void UpdateTexts(WorldView world, Camera cam, IPanel panel)
        {
            float now = Time.time;
            for (int i = _texts.Count - 1; i >= 0; i--)
            {
                var t = _texts[i];
                float age = (now - t.Start) / TextSeconds;
                if (age >= 1f)
                {
                    t.Label.RemoveFromHierarchy();
                    _texts.RemoveAt(i);
                    continue;
                }
                if (world.TryGetView(t.Anchor, out var view)) t.LastWorld = view.HeadPosition;
                else if (t.LastWorld == Vector3.zero) continue;
                var viewport = cam.WorldToViewportPoint(t.LastWorld);
                if (viewport.z <= 0f)
                {
                    t.Label.style.display = DisplayStyle.None;
                    continue;
                }
                var p = RuntimePanelUtils.CameraTransformWorldToPanel(panel, t.LastWorld, cam);
                t.Label.style.display = DisplayStyle.Flex;
                t.Label.style.left = p.x + t.OffsetX;
                t.Label.style.top = p.y - 50f - age * TextRisePixels;
                t.Label.style.opacity = age < 0.6f ? 1f : 1f - (age - 0.6f) / 0.4f;
            }
        }
    }
}
