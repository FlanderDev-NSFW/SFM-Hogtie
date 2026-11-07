using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;
using Hogtie.Logic;
using NVector3 = System.Numerics.Vector3;

namespace Hogtie
{
    /// <summary>
    /// The visible device: one transform per model group under a root that carries the frame and the scale. The cuffs hang on
    /// their anchors (the point their mount holds), so a size key never moves that point and the arm cuffs turn about it;
    /// the arm cuffs slide along the bar with their mounts, the ankle bar and its cuffs move up and down the post.
    /// </summary>
    internal sealed class HogtieRig
    {
        private readonly HogtieModel _model;
        private GameObject _root;
        private readonly List<(ModelPart Part, Transform T, Mesh Mesh, NVector3 Pivot)> _groups = new List<(ModelPart, Transform, Mesh, NVector3)>();
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>(); // meshes and materials
        private Material _metal;

        public HogtieRig(HogtieModel model) { _model = model; }

        public bool Built => _root != null;

        private NVector3 Pivot(ModelPart p) => _model.CuffOf(p)?.Anchor ?? NVector3.Zero;

        /// <summary>Make the device's objects: under <paramref name="parent"/> (a placed device: goes with its map), else on its own
        /// across maps.</summary>
        public void Build(int layer, Transform parent = null)
        {
            Destroy();
            _root = new GameObject("Hogtie");
            if (parent != null) _root.transform.SetParent(parent, false);
            else UnityEngine.Object.DontDestroyOnLoad(_root);
            _root.layer = layer;
            _metal = MakeMaterial(FindHandcuffMaterial());
            foreach (var part in _model.Parts)
            {
                var go = new GameObject(part.Group + (part.Side == 2 ? "" : part.Side == 0 ? "L" : "R"));
                go.layer = layer;
                go.transform.SetParent(_root.transform, false);
                var mesh = new Mesh { name = "Hogtie." + go.name };
                int count = part.Positions.Length;
                if (2 * count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                NVector3 pivot = Pivot(part);
                mesh.vertices = Vertices(part, pivot, 0f);
                // Both sides, like Blender draws it: the model's own winding is not consistent (the right cuffs are mirrored
                // copies wound inside out, signed volume < 0), and drawn one-sided the plate vanished seen from above while the
                // rings showed their far half [observed 2026-10-27]. The second copy faces the other way with turned normals.
                var n = new Vector3[2 * count];
                for (int i = 0; i < count; i++)
                {
                    n[i] = HogtiePlacer.U(part.Normals[i]);
                    n[count + i] = -n[i];
                }
                mesh.normals = n;
                mesh.triangles = TwoSided(part.Indices, count);
                mesh.RecalculateBounds();
                _owned.Add(mesh);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _metal;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                // The level's baked occlusion hid the whole device from some camera spots behind her (she stayed drawn)
                // [observed 2026-10-27].
                renderer.allowOcclusionWhenDynamic = false;
                _groups.Add((part, go.transform, mesh, pivot));
            }
            _root.SetActive(false);
        }

        /// <summary>The triangles as modelled, then each turned round on the second copy of the vertices.</summary>
        private static int[] TwoSided(int[] indices, int count)
        {
            var f = new int[2 * indices.Length];
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                f[i] = indices[i]; f[i + 1] = indices[i + 1]; f[i + 2] = indices[i + 2];
                int j = indices.Length + i;
                f[j] = count + indices[i]; f[j + 1] = count + indices[i + 2]; f[j + 2] = count + indices[i + 1];
            }
            return f;
        }

        /// <summary>The part's vertices relative to its pivot, both sides (see Build); cuffs at size <paramref name="key"/> and
        /// squashed by <paramref name="k"/> along <paramref name="dir"/> about their centre <paramref name="center"/>.</summary>
        private static Vector3[] Vertices(ModelPart p, NVector3 pivot, float key, NVector3 center = default, NVector3 dir = default, float k = 1f)
        {
            int count = p.Positions.Length;
            var v = new Vector3[2 * count];
            bool squash = Math.Abs(k - 1f) > 1e-4f && dir.LengthSquared() > 0.5f;
            for (int i = 0; i < count; i++)
            {
                NVector3 q = p.Positions[i];
                if (p.KeyDeltas != null) q += p.KeyDeltas[i] * key;
                if (squash) q += dir * (NVector3.Dot(q - center, dir) * (k - 1f));
                v[i] = v[count + i] = HogtiePlacer.U(q - pivot);
            }
            return v;
        }

        /// <summary>Normals for <see cref="Vertices"/>: a squash turns them by its inverse (both sides, the back turned round).</summary>
        private static Vector3[] Normals(ModelPart p, NVector3 dir, float k)
        {
            int count = p.Normals.Length;
            var n = new Vector3[2 * count];
            bool squash = Math.Abs(k - 1f) > 1e-4f && dir.LengthSquared() > 0.5f;
            for (int i = 0; i < count; i++)
            {
                NVector3 q = p.Normals[i];
                if (squash) q = NVector3.Normalize(q + dir * (NVector3.Dot(q, dir) * (1f / k - 1f)));
                n[i] = HogtiePlacer.U(q);
                n[count + i] = -n[i];
            }
            return n;
        }

        /// <summary>Size the cuffs (their size keys) and put the device in place (device frame, layout in metres).</summary>
        public void Place(DeviceFrame frame, HogtieLayout o)
        {
            if (_root == null) return;
            var q = frame.Rotation();
            _root.transform.SetPositionAndRotation(HogtiePlacer.U(frame.Origin), new Quaternion(q.X, q.Y, q.Z, q.W));
            _root.transform.localScale = Vector3.one * o.Scale;
            foreach (var (part, t, mesh, pivot) in _groups)
            {
                if (part.KeyDeltas == null) continue;
                int cuff = Cuffs.Of(part);
                float key = o.CuffKey[cuff], k = o.CuffSquash[cuff];
                NVector3 c = Cuffs.Model(_model, cuff).CenterAt(key), dir = o.CuffSquashDir[cuff];
                mesh.vertices = Vertices(part, pivot, key, c, dir, k);
                mesh.normals = Normals(part, dir, k);
                mesh.RecalculateBounds();
            }
            Move(o);
        }

        /// <summary>The movable parts to the layout's places (transforms only: the arm cuffs follow her arms every frame).</summary>
        public void Move(HogtieLayout o)
        {
            if (_root == null) return;
            foreach (var (part, t, mesh, pivot) in _groups)
            {
                NVector3 at = pivot;
                float tilt = 0f;
                switch (part.Group)
                {
                    case PartGroup.UpperCuff: Turned(t, o.UpperAnchor[part.Side] + o.CuffShift[Cuffs.Of(part)] / o.Scale, o.UpperTurn[part.Side]); continue;
                    case PartGroup.UpperMount: at += new NVector3(0, (o.UpperSlide[0] + o.UpperSlide[1]) / 2f, 0); break;
                    case PartGroup.WristCuff: Turned(t, o.WristAnchor[part.Side] + o.CuffShift[Cuffs.Of(part)] / o.Scale, o.WristTurn[part.Side]); continue;
                    case PartGroup.WristMount: at += new NVector3(0, (o.WristSlide[0] + o.WristSlide[1]) / 2f, 0); break;
                    case PartGroup.AnkleBar: at += new NVector3(0, 0, o.AnkleBarRaise); break;
                    case PartGroup.AnkleCuff: at += new NVector3(0, 0, o.AnkleBarRaise) + o.CuffShift[Cuffs.Of(part)] / o.Scale; tilt = o.AnkleTilt; break;
                    case PartGroup.Collar: at += o.CuffShift[Cuffs.Collar] / o.Scale; break;
                }
                t.localPosition = HogtiePlacer.U(at);
                // Device axes are right-handed in their numbers like System.Numerics: a turn about +x by tilt lifts +y towards +z.
                var r = System.Numerics.Quaternion.CreateFromAxisAngle(NVector3.UnitX, tilt);
                t.localRotation = new Quaternion(r.X, r.Y, r.Z, r.W);
            }
        }

        private static void Turned(Transform t, NVector3 anchor, System.Numerics.Quaternion turn)
        {
            t.localPosition = HogtiePlacer.U(anchor);
            t.localRotation = new Quaternion(turn.X, turn.Y, turn.Z, turn.W);
        }

        public void SetVisible(bool visible)
        {
            if (_root != null && _root.activeSelf != visible) _root.SetActive(visible);
        }

        public void SetShade(float shade)
        {
            if (_metal == null) return;
            Color c = new Color(shade, shade, shade, 1f);
            if (_metal.HasProperty("_BaseColor")) _metal.SetColor("_BaseColor", c);
            if (_metal.HasProperty("_Color")) _metal.SetColor("_Color", c);
            if (_metal.HasProperty("_1st_ShadeColor")) _metal.SetColor("_1st_ShadeColor", c * 0.62f);
            if (_metal.HasProperty("_2nd_ShadeColor")) _metal.SetColor("_2nd_ShadeColor", c * 0.38f);
        }

        /// <summary>The game's handcuff material (UnityChan toon shader), so the device is lit and outlined like the game's own items.</summary>
        private static Material FindHandcuffMaterial()
        {
            try
            {
                var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Material>());
                foreach (var o in all)
                {
                    var mat = o.TryCast<Material>();
                    if (mat != null && mat.name != null && mat.name.StartsWith("tejyou", StringComparison.Ordinal)) return mat;
                }
            }
            catch (Exception e) { Plugin.Logger.LogWarning("handcuff material lookup failed: " + e.Message); }
            Plugin.Logger.LogWarning("handcuff material not found; using a toon/standard shader");
            return null;
        }

        private Material MakeMaterial(Material source)
        {
            Material mat;
            if (source != null) mat = new Material(source);
            else
            {
                Shader shader = Shader.Find("UnityChanToonShader/Toon_DoubleShadeWithFeather");
                if (shader == null) shader = Shader.Find("Standard");
                mat = new Material(shader);
            }
            mat.name = "Hogtie.Metal";
            Texture white = Texture2D.whiteTexture;
            foreach (string tex in new[] { "_MainTex", "_BaseMap", "_1st_ShadeMap", "_2nd_ShadeMap" })
                if (mat.HasProperty(tex)) mat.SetTexture(tex, white);
            _owned.Add(mat);
            _metal = mat;
            // The model's metal is glossy black; toon shading has no reflections, so it is a dark gunmetal (config).
            SetShade(Plugin.MetalShade.Value);
            return mat;
        }

        public void Destroy()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _groups.Clear();
            foreach (var o in _owned) if (o != null) UnityEngine.Object.Destroy(o);
            _owned.Clear();
            _metal = null;
        }
    }
}
