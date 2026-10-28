using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Madlands.Client
{
    /// <summary>
    /// The effect models of art/blender/effects.py (Art/Effects: FxShard, FxFleck, FxRing ...) in a color.
    /// Solids glow in three tones of the color (their EffectLight, Effect and EffectDark faces), so a crystal
    /// stays bright in shadow and still reads as cut. Parts that fade (rings, waves, flashes, trails) use the
    /// transparent EffectFade material with a color per renderer, and flecks fly as particles of the same
    /// meshes, their tones baked into vertex colors.
    /// </summary>
    public static class Effects
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly Dictionary<string, Mesh> ParticleMeshes = new Dictionary<string, Mesh>();
        private static Material _fade;
        private static MaterialPropertyBlock _block;

        /// <summary>Transparent, unlit and two-sided; the color comes from <see cref="SetColor"/> or the particles.</summary>
        public static Material FadeMaterial
        {
            get
            {
                if (_fade == null) _fade = Resources.Load<Material>("EffectFade");
                return _fade != null ? _fade : EntityVisuals.GlowMaterial(Color.white);
            }
        }

        /// <summary>The tone of faces toward the light.</summary>
        public static Color Light(Color c) => Color.Lerp(c, Color.white, 0.45f);

        /// <summary>The tone of faces away from it.</summary>
        public static Color Dark(Color c) => new Color(c.r * 0.55f, c.g * 0.55f, c.b * 0.55f, 1f);

        /// <summary>
        /// A glowing solid in three tones of <paramref name="color"/>, at <paramref name="position"/> under
        /// <paramref name="parent"/> (world space without one), <paramref name="scale"/> meters long.
        /// </summary>
        public static Transform Spawn(string name, Transform parent, Vector3 position, float scale, Color color)
        {
            var t = Instantiate(name, parent, position, scale, color);
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                var materials = r.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    string tone = materials[i] != null ? materials[i].name : "";
                    materials[i] = EntityVisuals.GlowMaterial(tone.Contains("Light") ? Light(color) : tone.Contains("Dark") ? Dark(color) : color);
                }
                r.sharedMaterials = materials;
            }
            return t;
        }

        /// <summary>A shape that fades (rings, waves, flashes): see <see cref="SetColor"/>.</summary>
        public static Transform SpawnFading(string name, Transform parent, Vector3 position, float scale, Color color)
        {
            var t = Instantiate(name, parent, position, scale, color);
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                var materials = r.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = FadeMaterial;
                r.sharedMaterials = materials;
            }
            SetColor(t, color);
            return t;
        }

        /// <summary>Colors (and with alpha, fades) every renderer of a fading shape.</summary>
        public static void SetColor(Transform shape, Color color)
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            _block.SetColor(BaseColor, color);
            foreach (var r in shape.GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(_block);
        }

        private static Transform Instantiate(string name, Transform parent, Vector3 position, float scale, Color color)
        {
            var catalog = ArtCatalog.Instance;
            var prefab = catalog != null ? catalog.GetModel(name) : null;
            if (prefab == null) return EntityVisuals.Glow(parent, position, Vector3.one * scale * 0.4f, color, PrimitiveType.Sphere);
            // The model keeps the rotation it was imported with (Blender's axes, turned to face +Z) under a
            // holder that callers move, turn and scale freely.
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = position;
            t.localScale = Vector3.one * scale;
            var model = Object.Instantiate(prefab, t, false);
            foreach (var r in model.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            return t;
        }

        /// <summary>
        /// A particle system of one effect mesh: flecks of <paramref name="color"/> that spin, shrink and fade
        /// out, simulated in world space. The caller sets how they are emitted and plays it.
        /// </summary>
        public static ParticleSystem Particles(string mesh, Transform parent, Vector3 position, Color color, float minSize, float maxSize)
        {
            var go = new GameObject(mesh + "Particles");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startColor = Color.Lerp(color, Color.white, 0.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 200;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.separateAxes = true;
            spin.x = new ParticleSystem.MinMaxCurve(-6f, 6f);
            spin.y = new ParticleSystem.MinMaxCurve(-6f, 6f);
            spin.z = new ParticleSystem.MinMaxCurve(-6f, 6f);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.6f, 0.8f), new Keyframe(1f, 0f)));

            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = ParticleMesh(mesh);
            renderer.alignment = ParticleSystemRenderSpace.Local;
            renderer.sharedMaterial = FadeMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        /// <summary>
        /// An effect mesh as one submesh with its tones in the vertex colors (particles draw one material).
        /// A cube if the model is missing.
        /// </summary>
        public static Mesh ParticleMesh(string name)
        {
            if (ParticleMeshes.TryGetValue(name, out var cached)) return cached;
            var catalog = ArtCatalog.Instance;
            var prefab = catalog != null ? catalog.GetModel(name) : null;
            var filter = prefab != null ? prefab.GetComponentInChildren<MeshFilter>() : null;
            var renderer = filter != null ? filter.GetComponent<Renderer>() : null;
            Mesh mesh;
            if (filter == null || renderer == null || !filter.sharedMesh.isReadable)
            {
                mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            }
            else
            {
                var source = filter.sharedMesh;
                var colors = new Color[source.vertexCount];
                var triangles = new List<int>();
                var materials = renderer.sharedMaterials;
                for (int s = 0; s < source.subMeshCount; s++)
                {
                    string tone = s < materials.Length && materials[s] != null ? materials[s].name : "";
                    var shade = tone.Contains("Light") ? Color.white : tone.Contains("Dark") ? new Color(0.5f, 0.5f, 0.5f) : new Color(0.78f, 0.78f, 0.78f);
                    foreach (int i in source.GetTriangles(s))
                    {
                        colors[i] = shade;
                        triangles.Add(i);
                    }
                }
                // As the model stands in the prefab (the imported axes turned so that up is +Y).
                var placed = filter.transform.localToWorldMatrix;
                var vertices = source.vertices;
                var normals = source.normals;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = placed.MultiplyPoint3x4(vertices[i]);
                for (int i = 0; i < normals.Length; i++) normals[i] = placed.MultiplyVector(normals[i]).normalized;
                mesh = new Mesh { name = name + "Particle", vertices = vertices, normals = normals, colors = colors };
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
            }
            ParticleMeshes[name] = mesh;
            return mesh;
        }
    }
}
