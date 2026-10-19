using System.Collections.Generic;
using System.Globalization;
using Madlands.ClientCore;
using Madlands.Shared;
using Madlands.Shared.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace Madlands.Client
{
    /// <summary>
    /// The minimap (top right, north up, mouse wheel zooms) and the world map (M). Both draw the map
    /// image Export Map rendered from above (<see cref="MapInfo.worldMap"/>), the player's arrow,
    /// other players, NPCs with their quest marks, and on the world map the places active quests
    /// send you to explore.
    /// </summary>
    public sealed class MapUi
    {
        private const float MinimapPixels = 200f;
        private const float WorldMapPixels = 800f;
        private static readonly float[] Zooms = { 50f, 80f, 120f, 180f, 260f };

        private sealed class Marker
        {
            public VisualElement Root;
            public Label Text;
        }

        private readonly GameClient _client;
        private readonly VisualElement _minimap;
        private readonly VisualElement _miniImage;
        private readonly VisualElement _miniMarkers;
        private readonly VisualElement _miniArrow;
        private readonly Label _caption;
        private readonly GameWindow _window;
        private readonly VisualElement _worldView;
        private readonly VisualElement _worldImage;
        private readonly VisualElement _worldMarkers;
        private readonly VisualElement _worldAreas;
        private readonly VisualElement _worldArrow;
        private readonly List<Marker> _miniPool = new List<Marker>();
        private readonly List<Marker> _worldPool = new List<Marker>();
        private readonly Texture2D _arrowTexture;
        private MapInfo _map;
        private string _area;

        /// <summary>The player walked into a named place (also when entering the world in one).</summary>
        public event System.Action<string> AreaEntered;
        private int _zoom = 2;
        private int _areasForQuestLogVersion = -1;
        private int _questLogVersion;

        public MapUi(VisualElement layer, GameClient client)
        {
            _client = client;
            _arrowTexture = MakeArrow();

            _minimap = new VisualElement { pickingMode = PickingMode.Position };
            _minimap.AddToClassList("minimap");
            _miniImage = new VisualElement { pickingMode = PickingMode.Ignore };
            _miniImage.AddToClassList("map-image");
            _miniMarkers = new VisualElement { pickingMode = PickingMode.Ignore };
            _miniMarkers.AddToClassList("layer");
            _miniArrow = Arrow(_minimap);
            _minimap.Add(_miniImage);
            _minimap.Add(_miniMarkers);
            _minimap.Add(_miniArrow);
            _minimap.RegisterCallback<WheelEvent>(e =>
            {
                Zoom(e.delta.y > 0 ? 1 : -1);
                e.StopPropagation();
            });
            _minimap.RegisterCallback<ClickEvent>(_ => ToggleWorldMap());

            var frame = new VisualElement { pickingMode = PickingMode.Ignore };
            frame.AddToClassList("minimap-frame");
            frame.Add(_minimap);
            var buttons = new VisualElement { pickingMode = PickingMode.Ignore };
            buttons.AddToClassList("minimap-buttons");
            buttons.Add(SmallButton("+", () => Zoom(-1)));
            buttons.Add(SmallButton("-", () => Zoom(1)));
            frame.Add(buttons);
            _caption = new Label { pickingMode = PickingMode.Ignore };
            _caption.AddToClassList("minimap-caption");
            frame.Add(_caption);
            layer.Add(frame);

            _window = new GameWindow(layer, Loc.T("window.map"), "worldmap");
            _worldView = new VisualElement { pickingMode = PickingMode.Position };
            _worldView.AddToClassList("worldmap-view");
            _worldImage = new VisualElement { pickingMode = PickingMode.Ignore };
            _worldImage.AddToClassList("map-image");
            _worldAreas = new VisualElement { pickingMode = PickingMode.Ignore };
            _worldAreas.AddToClassList("layer");
            _worldMarkers = new VisualElement { pickingMode = PickingMode.Ignore };
            _worldMarkers.AddToClassList("layer");
            _worldView.Add(_worldImage);
            _worldView.Add(_worldAreas);
            _worldView.Add(_worldMarkers);
            _worldArrow = Arrow(_worldView);
            _worldView.Add(_worldArrow);
            _window.Body.Add(_worldView);
            GameWindow.Text(_window.Body, Loc.T("map.hint"), "window-hint");

            client.QuestLogChanged += () => _questLogVersion++;
        }

        public bool WorldMapOpen => _window.IsOpen;

        /// <summary>The map scene that was loaded (null hides the minimap).</summary>
        public void SetMap(MapInfo map)
        {
            _map = map;
            bool has = map != null && map.worldMap != null && map.worldMapSize > 0f;
            _minimap.parent.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
            if (!has) return;
            var image = new StyleBackground(map.worldMap);
            _miniImage.style.backgroundImage = image;
            _worldImage.style.backgroundImage = image;
            _worldImage.style.width = WorldMapPixels;
            _worldImage.style.height = WorldMapPixels;
            _worldImage.style.left = 0;
            _worldImage.style.top = 0;
            _window.Title = map.mapName;
            _areasForQuestLogVersion = -1;
            _area = null;
        }

        public void ToggleWorldMap()
        {
            if (_map == null || _map.worldMap == null) return;
            _window.Toggle();
            _areasForQuestLogVersion = -1;
        }

        public void Close() => _window.Close();

        public bool HandleEscape()
        {
            if (!_window.IsOpen) return false;
            _window.Close();
            return true;
        }

        private void Zoom(int step) => _zoom = Mathf.Clamp(_zoom + step, 0, Zooms.Length - 1);

        // ------------------------------------------------------------------ per frame

        public void Update()
        {
            if (_map == null || _map.worldMap == null || _map.worldMapSize <= 0f) return;
            var me = _client.LocalMovement;
            string area = _map.AreaAt(me.Position.X, me.Position.Z);
            if (area != _area)
            {
                _area = area;
                if (area != null) AreaEntered?.Invoke(area);
            }
            _caption.text = (area ?? _map.mapName) + "   " + Mathf.RoundToInt(me.Position.X).ToString(CultureInfo.InvariantCulture)
                            + ", " + Mathf.RoundToInt(me.Position.Z).ToString(CultureInfo.InvariantCulture);
            UpdateMinimap(me.Position, me.Yaw);
            if (_window.IsOpen) UpdateWorldMap(me.Position, me.Yaw);
        }

        private void UpdateMinimap(Vec3 me, float yaw)
        {
            float meters = Zooms[_zoom];
            float ppm = MinimapPixels / meters;
            float half = MinimapPixels * 0.5f;
            float size = _map.worldMapSize * ppm;
            _miniImage.style.width = size;
            _miniImage.style.height = size;
            _miniImage.style.left = half + (_map.worldMapMin.x - me.X) * ppm;
            _miniImage.style.top = half - (_map.worldMapMin.y + _map.worldMapSize - me.Z) * ppm;
            Place(_miniArrow, half, half);
            _miniArrow.style.rotate = new Rotate(new Angle(yaw * Mathf.Rad2Deg, AngleUnit.Degree));

            int used = 0;
            float radius = meters * 0.5f;
            foreach (var entity in _client.World.Entities)
            {
                if (!MarkerFor(entity, out string text, out string css)) continue;
                var p = entity.Movement.Sample(_client.RenderTimeMs).Position;
                float dx = p.X - me.X, dz = p.Z - me.Z;
                if (dx * dx + dz * dz > radius * radius * 0.92f) continue;
                var marker = Take(_miniPool, _miniMarkers, used++);
                SetMarker(marker, text, css);
                Place(marker.Root, half + dx * ppm, half - dz * ppm);
            }
            Hide(_miniPool, used);
        }

        private void UpdateWorldMap(Vec3 me, float yaw)
        {
            float ppm = WorldMapPixels / _map.worldMapSize;
            Vector2 ToView(float x, float z) => new Vector2((x - _map.worldMapMin.x) * ppm, WorldMapPixels - (z - _map.worldMapMin.y) * ppm);

            var arrow = ToView(me.X, me.Z);
            Place(_worldArrow, arrow.x, arrow.y);
            _worldArrow.style.rotate = new Rotate(new Angle(yaw * Mathf.Rad2Deg, AngleUnit.Degree));

            int used = 0;
            foreach (var entity in _client.World.Entities)
            {
                if (!MarkerFor(entity, out string text, out string css)) continue;
                var p = entity.Movement.Sample(_client.RenderTimeMs).Position;
                var marker = Take(_worldPool, _worldMarkers, used++);
                SetMarker(marker, text, css);
                var v = ToView(p.X, p.Z);
                Place(marker.Root, v.x, v.y);
            }
            Hide(_worldPool, used);

            if (_areasForQuestLogVersion == _questLogVersion) return;
            _areasForQuestLogVersion = _questLogVersion;
            _worldAreas.Clear();
            foreach (var quest in _client.Quests.Active)
            {
                var info = _client.Data.GetQuest(quest.QuestId);
                if (info == null) continue;
                for (int i = 0; i < info.ObjectiveList.Count; i++)
                {
                    var o = info.ObjectiveList[i];
                    bool done = i < quest.Progress.Length && quest.Progress[i] >= o.Count;
                    if (o.Type != ObjectiveType.Explore || done || o.Map != _map.mapId) continue;
                    var center = ToView(o.X, o.Z);
                    float r = Mathf.Max(6f, o.Radius * ppm);
                    var area = new VisualElement { pickingMode = PickingMode.Ignore };
                    area.AddToClassList("map-area");
                    area.style.left = center.x - r;
                    area.style.top = center.y - r;
                    area.style.width = r * 2f;
                    area.style.height = r * 2f;
                    area.style.borderTopLeftRadius = area.style.borderTopRightRadius = area.style.borderBottomLeftRadius = area.style.borderBottomRightRadius = r;
                    _worldAreas.Add(area);
                    var label = new Label(o.Text) { pickingMode = PickingMode.Ignore };
                    label.AddToClassList("map-area-label");
                    label.style.left = center.x - 100f;
                    label.style.top = center.y + r + 2f;
                    _worldAreas.Add(label);
                }
            }
        }

        /// <summary>What an entity looks like on the maps: players as blue dots, NPCs as green dots or their quest mark.</summary>
        private bool MarkerFor(RemoteEntity entity, out string text, out string css)
        {
            text = "";
            css = null;
            if (entity.Unit == null || entity.Unit.Values.IsDead) return false;
            if (entity.Guid.Type == EntityType.Player)
            {
                css = "player";
                return true;
            }
            if (entity.Unit.Values.NpcFlags == NpcFlags.None || _client.GetReaction(entity.Guid) != Reaction.Friendly) return false;
            switch (_client.GetQuestGiverStatus(entity.Guid))
            {
                case QuestGiverStatus.Available: text = "!"; css = "quest"; break;
                case QuestGiverStatus.Reward: text = "?"; css = "quest"; break;
                case QuestGiverStatus.Incomplete: text = "?"; css = "quest incomplete"; break;
                default: css = "npc"; break;
            }
            return true;
        }

        // ------------------------------------------------------------------ helpers

        private static Marker Take(List<Marker> pool, VisualElement parent, int index)
        {
            if (index < pool.Count)
            {
                pool[index].Root.style.display = DisplayStyle.Flex;
                return pool[index];
            }
            var root = new VisualElement { pickingMode = PickingMode.Ignore };
            var text = new Label { pickingMode = PickingMode.Ignore };
            root.Add(text);
            parent.Add(root);
            var marker = new Marker { Root = root, Text = text };
            pool.Add(marker);
            return marker;
        }

        private static void SetMarker(Marker marker, string text, string css)
        {
            marker.Root.ClearClassList();
            marker.Root.AddToClassList("map-marker");
            foreach (var c in css.Split(' ')) marker.Root.AddToClassList(c);
            marker.Text.text = text;
        }

        private static void Hide(List<Marker> pool, int used)
        {
            for (int i = used; i < pool.Count; i++) pool[i].Root.style.display = DisplayStyle.None;
        }

        /// <summary>Puts an element on a point of its parent (USS centers it there with negative margins).</summary>
        private static void Place(VisualElement element, float x, float y)
        {
            element.style.left = x;
            element.style.top = y;
        }

        private VisualElement Arrow(VisualElement parent)
        {
            var arrow = new VisualElement { pickingMode = PickingMode.Ignore };
            arrow.AddToClassList("map-arrow");
            arrow.style.backgroundImage = new StyleBackground(_arrowTexture);
            return arrow;
        }

        private static Button SmallButton(string text, System.Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("minimap-button");
            return button;
        }

        /// <summary>A gold arrowhead pointing up (north = the player's facing at rotation 0), drawn once.</summary>
        private static Texture2D MakeArrow()
        {
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var fill = new Color(1f, 0.84f, 0.3f, 1f);
            var edge = new Color(0.15f, 0.08f, 0f, 1f);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    // Texture rows go bottom-up: the tip is at the top row. A notch at the bottom makes it an arrowhead.
                    float v = (float)y / (n - 1), u = (float)x / (n - 1) - 0.5f;
                    float halfWidth = (1f - v) * 0.45f;
                    float notch = Mathf.Max(0f, 0.25f - Mathf.Abs(u)) * 0.9f;
                    bool inside = Mathf.Abs(u) <= halfWidth && v >= notch;
                    bool border = inside && (Mathf.Abs(u) > halfWidth - 0.07f || v < notch + 0.07f);
                    tex.SetPixel(x, y, inside ? (border ? edge : fill) : Color.clear);
                }
            }
            tex.Apply();
            return tex;
        }
    }
}
