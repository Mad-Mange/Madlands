using System.Collections.Generic;
using Madlands.Shared.Terrain;
using UnityEngine;

namespace Madlands.Client.Editor
{
    /// <summary>
    /// Turns the scene's static colliders into one world-space triangle mesh for the server (navmesh
    /// obstacles and line of sight). Terrain colliders are skipped (the height map covers them),
    /// and so are triggers. Primitive colliders become low-poly shapes of the same size.
    /// </summary>
    public static class CollisionExporter
    {
        private const int Segments = 10;
        private const int Rings = 6;

        public static CollisionMesh Build(out int colliderCount)
        {
            var vertices = new List<float>();
            var indices = new List<int>();
            colliderCount = 0;
            foreach (var collider in Object.FindObjectsByType<Collider>())
            {
                if (collider is TerrainCollider || collider.isTrigger || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
                int before = indices.Count;
                switch (collider)
                {
                    case BoxCollider box:
                        AddBox(box, vertices, indices);
                        break;
                    case SphereCollider sphere:
                        AddSphere(sphere, vertices, indices);
                        break;
                    case CapsuleCollider capsule:
                        AddCapsule(capsule, vertices, indices);
                        break;
                    case MeshCollider mesh when mesh.sharedMesh != null:
                        AddMesh(mesh, vertices, indices);
                        break;
                }
                if (indices.Count > before) colliderCount++;
            }
            return new CollisionMesh(vertices.ToArray(), indices.ToArray());
        }

        private static int AddVertex(List<float> vertices, Vector3 v)
        {
            vertices.Add(v.x);
            vertices.Add(v.y);
            vertices.Add(v.z);
            return vertices.Count / 3 - 1;
        }

        private static void AddBox(BoxCollider box, List<float> vertices, List<int> indices)
        {
            var m = box.transform.localToWorldMatrix;
            var half = box.size * 0.5f;
            int first = vertices.Count / 3;
            for (int i = 0; i < 8; i++)
            {
                var local = box.center + new Vector3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z);
                AddVertex(vertices, m.MultiplyPoint3x4(local));
            }
            int[] quads = { 0, 1, 3, 2, 4, 6, 7, 5, 0, 4, 5, 1, 2, 3, 7, 6, 0, 2, 6, 4, 1, 5, 7, 3 };
            for (int q = 0; q < quads.Length; q += 4)
            {
                indices.Add(first + quads[q]);
                indices.Add(first + quads[q + 1]);
                indices.Add(first + quads[q + 2]);
                indices.Add(first + quads[q]);
                indices.Add(first + quads[q + 2]);
                indices.Add(first + quads[q + 3]);
            }
        }

        private static void AddSphere(SphereCollider sphere, List<float> vertices, List<int> indices)
        {
            var t = sphere.transform;
            var s = t.lossyScale;
            float radius = sphere.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            AddCapsuleShape(t.TransformPoint(sphere.center), Vector3.up, radius, 0f, vertices, indices);
        }

        private static void AddCapsule(CapsuleCollider capsule, List<float> vertices, List<int> indices)
        {
            var t = capsule.transform;
            var s = t.lossyScale;
            var axisLocal = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
            float axisScale = Mathf.Abs(capsule.direction == 0 ? s.x : capsule.direction == 1 ? s.y : s.z);
            float radiusScale = capsule.direction == 0 ? Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z))
                : capsule.direction == 1 ? Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z))
                : Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y));
            float radius = capsule.radius * radiusScale;
            float halfCylinder = Mathf.Max(0f, capsule.height * axisScale * 0.5f - radius);
            AddCapsuleShape(t.TransformPoint(capsule.center), (t.rotation * axisLocal).normalized, radius, halfCylinder, vertices, indices);
        }

        /// <summary>A UV sphere split at the equator, the halves pushed apart along the axis (a sphere when halfCylinder is 0).</summary>
        private static void AddCapsuleShape(Vector3 center, Vector3 axis, float radius, float halfCylinder, List<float> vertices, List<int> indices)
        {
            var rotation = Quaternion.FromToRotation(Vector3.up, axis);
            int first = vertices.Count / 3;
            // Rings from the bottom pole to the top pole; the equator appears twice (bottom and top cap).
            var rings = new List<(float Latitude, float Offset)>();
            for (int i = 0; i <= Rings / 2; i++) rings.Add((-90f + 180f * i / Rings, -halfCylinder));
            for (int i = Rings / 2; i <= Rings; i++) rings.Add((-90f + 180f * i / Rings, halfCylinder));

            foreach (var (latitude, offset) in rings)
            {
                float y = Mathf.Sin(latitude * Mathf.Deg2Rad) * radius;
                float r = Mathf.Cos(latitude * Mathf.Deg2Rad) * radius;
                for (int j = 0; j < Segments; j++)
                {
                    float a = j * Mathf.PI * 2f / Segments;
                    var local = new Vector3(Mathf.Sin(a) * r, y + offset, Mathf.Cos(a) * r);
                    AddVertex(vertices, center + rotation * local);
                }
            }
            for (int ring = 0; ring < rings.Count - 1; ring++)
            {
                for (int j = 0; j < Segments; j++)
                {
                    int a = first + ring * Segments + j;
                    int b = first + ring * Segments + (j + 1) % Segments;
                    int c = a + Segments;
                    int d = b + Segments;
                    indices.Add(a); indices.Add(c); indices.Add(b);
                    indices.Add(b); indices.Add(c); indices.Add(d);
                }
            }
        }

        private static void AddMesh(MeshCollider collider, List<float> vertices, List<int> indices)
        {
            var mesh = collider.sharedMesh;
            var m = collider.transform.localToWorldMatrix;
            int first = vertices.Count / 3;
            foreach (var v in mesh.vertices) AddVertex(vertices, m.MultiplyPoint3x4(v));
            foreach (int i in mesh.triangles) indices.Add(first + i);
        }
    }
}
