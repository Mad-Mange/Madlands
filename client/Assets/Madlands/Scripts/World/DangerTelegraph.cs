using UnityEngine;
using UnityEngine.Rendering;
using Madlands.Shared.Game;

namespace Madlands.Client
{
    /// <summary>
    /// A terrain-following warning for Cinder Quake. The ring is the horizontal projection of the
    /// server's 12m area in content/spells.json; height and line of sight still affect actual hits.
    /// Its parent owns its lifetime, and WorldView hides it as soon as the cast ends or is interrupted.
    /// </summary>
    public sealed class DangerTelegraph : MonoBehaviour
    {
        private const float Radius = TelegraphHints.CinderQuakeRadius;
        private const int Segments = 64, Rings = 8;
        private Mesh _fill, _rim;
        private Transform _owner;
        private Vector3 _lastCenter = new Vector3(float.PositiveInfinity, 0f, 0f);
        private Renderer _fillRenderer;
        private float _progress;

        public static DangerTelegraph Create(Transform owner)
        {
            var go = new GameObject("CinderQuakeWarning");
            go.transform.SetParent(owner, false);
            var warning = go.AddComponent<DangerTelegraph>();
            warning._owner = owner;
            warning._fill = new Mesh { name = "CinderQuakeFill" };
            warning._rim = new Mesh { name = "CinderQuakeRim" };
            warning._fillRenderer = warning.Part("Fill", warning._fill, new Color(1f, 0.22f, 0.04f, 0.15f));
            warning.Part("Edge", warning._rim, new Color(1f, 0.32f, 0.04f, 0.8f));
            warning.LateUpdate();
            return warning;
        }

        private Renderer Part(string name, Mesh mesh, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Effects.FadeMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Effects.SetColor(go.transform, color);
            return renderer;
        }

        public void SetProgress(float progress)
        {
            _progress = Mathf.Clamp01(progress);
            if (_fillRenderer != null)
                Effects.SetColor(_fillRenderer.transform, new Color(1f, 0.22f, 0.04f, 0.12f + 0.16f * _progress));
        }

        private void LateUpdate()
        {
            if (_owner == null) return;
            var center = _owner.position;
            transform.position = center;
            transform.rotation = Quaternion.identity;
            if ((center - _lastCenter).sqrMagnitude > 0.16f)
            {
                Build(_fill, center, 0f, Radius, Rings);
                Build(_rim, center, Radius - 0.24f, Radius, 1);
                _lastCenter = center;
            }
        }

        private static void Build(Mesh mesh, Vector3 center, float inner, float outer, int rings)
        {
            var vertices = new Vector3[(rings + 1) * (Segments + 1)];
            var triangles = new int[rings * Segments * 6];
            for (int r = 0; r <= rings; r++)
            {
                float radius = Mathf.Lerp(inner, outer, (float)r / rings);
                for (int s = 0; s <= Segments; s++)
                {
                    float angle = 2f * Mathf.PI * s / Segments;
                    float x = Mathf.Sin(angle) * radius, z = Mathf.Cos(angle) * radius;
                    var point = center + new Vector3(x, 0f, z);
                    vertices[r * (Segments + 1) + s] = new Vector3(x, TerrainUtil.GroundHeight(point, center.y) - center.y + 0.08f, z);
                }
            }
            int index = 0;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < Segments; s++)
                {
                    int a = r * (Segments + 1) + s, b = a + Segments + 1;
                    triangles[index++] = a; triangles[index++] = b; triangles[index++] = a + 1;
                    triangles[index++] = a + 1; triangles[index++] = b; triangles[index++] = b + 1;
                }
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (_fill != null) Destroy(_fill);
            if (_rim != null) Destroy(_rim);
        }
    }
}
