using System.Collections.Generic;
using Madlands.ClientCore;
using Madlands.Shared.Game;
using Madlands.Shared.Net;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// GM debug overlays (docs/GMMenu): the server's collision triangles, navmesh polygons and creature spawn
    /// points around the player, drawn as lines. The client has none of this data itself: while an overlay is
    /// on it asks the server (debug.geometry) and asks again after the player moved half the radius away.
    /// Registers the client-side commands debug.collision, debug.navmesh and debug.spawn_points.
    /// </summary>
    public sealed class DebugGeometryView
    {
        private const int Radius = 40;
        private const float RefetchDistance = Radius * 0.5f;
        private const float LineLift = 0.08f;

        private sealed class Layer
        {
            public string Kind;
            public string CommandId;
            public Color Color;
            public bool Enabled;
            public GameObject Object;
            public Mesh Mesh;
            public Vector3 FetchedAt;
            public bool Fetched;
            public int Segments;
            public bool Truncated;
        }

        private readonly GameClient _client;
        private readonly Dictionary<GmGeometryKind, Layer> _layers = new Dictionary<GmGeometryKind, Layer>();
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<int> _indices = new List<int>();
        private Material _template;

        public DebugGeometryView(GameClient client)
        {
            _client = client;
            Add(GmGeometryKind.Collision, GmCommands.GeometryCollision, GmCommands.ShowCollision, new Color(1f, 0.35f, 0.25f));
            Add(GmGeometryKind.NavMesh, GmCommands.GeometryNavMesh, GmCommands.ShowNavMesh, new Color(0.25f, 0.85f, 1f));
            Add(GmGeometryKind.SpawnPoints, GmCommands.GeometrySpawnPoints, GmCommands.ShowSpawnPoints, new Color(1f, 0.85f, 0.2f));
            client.GmGeometryReceived += OnGeometry;
            client.EnteredWorld += _ => DisableAll();
        }

        public bool IsEnabled(string commandId)
        {
            foreach (var layer in _layers.Values)
                if (layer.CommandId == commandId) return layer.Enabled;
            return false;
        }

        /// <summary>Segments shown for an overlay, and whether the server had to cut the list short.</summary>
        public (int Segments, bool Truncated) Stats(string commandId)
        {
            foreach (var layer in _layers.Values)
                if (layer.CommandId == commandId) return (layer.Segments, layer.Truncated);
            return (0, false);
        }

        private void Add(GmGeometryKind kind, string geometry, string commandId, Color color)
        {
            var layer = new Layer { Kind = geometry, CommandId = commandId, Color = color };
            _layers.Add(kind, layer);
            _client.Gm.RegisterLocal(commandId, args => Toggle(layer, args.Bool("enabled")));
        }

        private GmCommandResult Toggle(Layer layer, bool enabled)
        {
            layer.Enabled = enabled;
            layer.Fetched = false;
            if (!enabled) Clear(layer);
            else Fetch(layer);
            return GmCommandClient.Local(layer.CommandId, GmResultCode.Ok,
                enabled ? $"Showing {layer.Kind.Replace('_', ' ')} within {Radius} m (from the server)." : $"{layer.Kind.Replace('_', ' ')} overlay off.");
        }

        /// <summary>Every frame: asks for fresh geometry once the player moved away from where it was fetched.</summary>
        public void Update(Vector3 playerPosition)
        {
            foreach (var layer in _layers.Values)
            {
                if (!layer.Enabled) continue;
                if (!layer.Fetched || (playerPosition - layer.FetchedAt).sqrMagnitude > RefetchDistance * RefetchDistance)
                {
                    layer.FetchedAt = playerPosition;
                    Fetch(layer);
                }
            }
        }

        private void Fetch(Layer layer)
        {
            // One request at a time: another overlay's answer is still due, Update asks again next frame.
            if (_client.Gm.IsPending(GmCommands.DebugGeometry)) return;
            layer.Fetched = true;
            _client.Gm.Execute(GmCommands.DebugGeometry, new[]
            {
                new KeyValuePair<string, string>("kind", layer.Kind),
                new KeyValuePair<string, string>("radius", GmCommands.Format((long)Radius)),
            });
        }

        private void OnGeometry(GmDebugGeometry geometry)
        {
            if (!_layers.TryGetValue(geometry.Kind, out var layer) || !layer.Enabled) return;
            layer.FetchedAt = new Vector3(geometry.Center.X, geometry.Center.Y, geometry.Center.Z);
            layer.Segments = geometry.SegmentCount;
            layer.Truncated = geometry.Truncated;
            _vertices.Clear();
            _indices.Clear();
            for (int i = 0; i + 1 < geometry.Points.Count; i += 2)
            {
                var a = geometry.Points[i];
                var b = geometry.Points[i + 1];
                _indices.Add(_vertices.Count);
                _vertices.Add(new Vector3(a.X, a.Y + LineLift, a.Z));
                _indices.Add(_vertices.Count);
                _vertices.Add(new Vector3(b.X, b.Y + LineLift, b.Z));
            }
            if (layer.Object == null)
            {
                layer.Object = new GameObject("GmOverlay " + layer.Kind);
                Object.DontDestroyOnLoad(layer.Object);
                layer.Mesh = new Mesh { name = "GmOverlay " + layer.Kind, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                layer.Object.AddComponent<MeshFilter>().sharedMesh = layer.Mesh;
                var renderer = layer.Object.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = MakeMaterial(layer.Color);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            layer.Mesh.Clear();
            layer.Mesh.SetVertices(_vertices);
            layer.Mesh.SetIndices(_indices, MeshTopology.Lines, 0);
            layer.Mesh.RecalculateBounds();
            layer.Object.SetActive(true);
        }

        private Material MakeMaterial(Color color)
        {
            if (_template == null) _template = Resources.Load<Material>("Unlit");
            var material = _template != null ? new Material(_template) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", color);
            material.color = color;
            return material;
        }

        private static void Clear(Layer layer)
        {
            layer.Segments = 0;
            layer.Truncated = false;
            if (layer.Object != null) layer.Object.SetActive(false);
        }

        /// <summary>Turns every overlay off (entering the world again, or reset).</summary>
        public void DisableAll()
        {
            foreach (var layer in _layers.Values)
            {
                layer.Enabled = false;
                layer.Fetched = false;
                Clear(layer);
            }
        }
    }
}
