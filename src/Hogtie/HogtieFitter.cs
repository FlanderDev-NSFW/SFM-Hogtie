using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using Hogtie.Logic;
using NVector3 = System.Numerics.Vector3;

namespace Hogtie
{
    /// <summary>
    /// Refits the cuffs to her skin while she is held: her body mesh is baked as drawn (onPreCull), cut at every cuff and the
    /// cuff resized and moved across the limb so the smallest circle around that limb's skin fits inside with Model.ClearanceCm
    /// to spare (<see cref="CuffFit"/>). The estimates from bone lengths left the wrists and upper arms clipping (they are wider than
    /// deep) and the ankles' heel side [observed 2026-10-27]. A cuff only grows across fits (her breathing moves the skin).
    /// The bake needs her body object's scale (BakeMesh(mesh, true), then position + rotation * (lossyScale x vertex)) - Binders'
    /// finding, see its BinderFitter.
    /// </summary>
    internal sealed class HogtieFitter
    {
        private Mesh _bake;
        private SkinnedMeshRenderer _body;
        private readonly List<NVector3> _skin = new List<NVector3>();
        private readonly float[] _major = new float[Cuffs.Count], _minor = new float[Cuffs.Count];
        public string Status = "not fitted";

        public void Reset()
        {
            ClearMarkers();
            Array.Clear(_major, 0, _major.Length);
            Array.Clear(_minor, 0, _minor.Length);
            Status = "not fitted";
        }

        /// <summary>Bake, cut and refit every cuff (render time). Returns true when a cuff changed.</summary>
        public bool Fit(Animator anim, DeviceFrame f, HogtieModel m, HogtieLayout o, in BodyDims b, float clearance)
        {
            if (!Bake(anim, f)) { Status = "no body to bake"; return false; }
            NVector3 D(HumanBodyBones bone)
            {
                var t = anim.GetBoneTransform(bone);
                return t == null ? new NVector3(float.NaN) : f.ToDevice(HogtiePlacer.N(t.position));
            }
            // Her skeleton as segments, to tell the limbs apart (hands rest at her feet in this pose).
            var named = new List<(string Name, Segment Seg)>();
            void Add(string name, HumanBodyBones a, HumanBodyBones c, float radius)
            {
                NVector3 pa = D(a), pc = D(c);
                if (!float.IsNaN(pa.X) && !float.IsNaN(pc.X)) named.Add((name, new Segment(pa, pc, radius)));
            }
            // Rough thicknesses: limbs from the same estimates as the cuffs, hands and feet a bit more than the wrist and ankle.
            // The torso counts by its bone line (with its depth it took the neck's skin from the collar).
            Add("lowback", HumanBodyBones.Hips, HumanBodyBones.Chest, 0f);
            Add("chest", HumanBodyBones.Chest, HumanBodyBones.Neck, 0f);
            Add("neck", HumanBodyBones.Neck, HumanBodyBones.Head, b.NeckRadius);
            Add("upperL", HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, b.ArmRadius);
            Add("upperR", HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, b.ArmRadius);
            Add("foreL", HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, b.WristRadius);
            Add("foreR", HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, b.WristRadius);
            Add("handL", HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleProximal, b.WristRadius);
            Add("handR", HumanBodyBones.RightHand, HumanBodyBones.RightMiddleProximal, b.WristRadius);
            Add("thighL", HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, b.KneeRadius);
            Add("thighR", HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, b.KneeRadius);
            Add("shinL", HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, b.AnkleRadius);
            Add("shinR", HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, b.AnkleRadius);
            Add("footL", HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes, 1.3f * b.AnkleRadius);
            Add("footR", HumanBodyBones.RightFoot, HumanBodyBones.RightToes, 1.3f * b.AnkleRadius);
            string[] own = { "neck", "upperL", "upperR", "foreL", "foreR", "shinL", "shinR" };
            float[] estimate = { b.NeckRadius, b.ArmRadius, b.ArmRadius, b.WristRadius, b.WristRadius, b.AnkleRadius, b.AnkleRadius };

            var sb = new StringBuilder("cuffs fitted to her skin (hole radius, moved; cm):");
            bool changed = false;
            for (int cuff = 0; cuff < Cuffs.Count; cuff++)
            {
                int ownIndex = named.FindIndex(n => n.Name == own[cuff]);
                if (ownIndex < 0) { sb.Append(' ').Append(Cuffs.Name(cuff)).Append(" no bone;"); continue; }
                var others = new List<Segment>();
                for (int i = 0; i < named.Count; i++) if (i != ownIndex) others.Add(named[i].Seg);
                NVector3 center = HogtiePose.CuffCenter(m, o, cuff) + o.CuffShift[cuff];
                var r = CuffFit.Fit(_skin, center, HogtiePose.CuffAxis(m, o, cuff), HogtiePose.CuffHalfWidth(m, o, cuff),
                                    named[ownIndex].Seg, others, 3f * estimate[cuff]);
                sb.Append(' ').Append(Cuffs.Name(cuff)).Append(' ');
                if ((Plugin.DebugDisable.Value & 8) != 0 && (cuff == Cuffs.Wrist(1) || cuff == Cuffs.Upper(1)) && r.Kept != null)
                    foreach (var q in r.Kept) Marker(f, q);
                if (!r.Ok) { sb.Append("too few points (").Append(r.Points).Append(", ").Append(r.Profile).Append(");"); continue; }
                bool grew = r.Major > _major[cuff] + 1e-4f || r.Minor > _minor[cuff] + 1e-4f;
                if (grew)
                {
                    _major[cuff] = Math.Max(_major[cuff], r.Major);
                    _minor[cuff] = Math.Min(_major[cuff], Math.Max(_minor[cuff], r.Minor));
                    o.CuffKey[cuff] = Cuffs.Model(m, cuff).KeyFor((_major[cuff] + clearance) / o.Scale);
                    o.CuffSquash[cuff] = (_minor[cuff] + clearance) / (_major[cuff] + clearance);
                    o.CuffSquashDir[cuff] = ToCuffModel(m, o, cuff, r.MinorDir);
                    var kind = Cuffs.Kind(cuff);
                    // Arm cuffs are put back on the limb's line every frame (FitArm, with the new key): their shift is from there.
                    // Others: the new size moves the ring's centre (the key scales it about its anchor) - shift it onto the ellipse's.
                    o.CuffShift[cuff] = kind == PartGroup.UpperCuff || kind == PartGroup.WristCuff
                        ? o.CuffShift[cuff] + r.Offset
                        : center + r.Offset - HogtiePose.CuffCenter(m, o, cuff);
                    changed = true;
                }
                sb.Append(Cm(_major[cuff])).Append(" x ").Append(Cm(_minor[cuff])).Append(" (est. ").Append(Cm(estimate[cuff])).Append("), moved ")
                  .Append(Cm(o.CuffShift[cuff].Length())).Append(", ").Append(r.Points).Append(" pts (").Append(r.Profile).Append(");");
            }
            Status = sb.ToString();
            return changed;
        }

        /// <summary>A direction across a cuff from device coordinates into the cuff's model coordinates (before its turn).</summary>
        private static NVector3 ToCuffModel(HogtieModel m, HogtieLayout o, int cuff, NVector3 dir)
        {
            int side = Cuffs.Side(cuff);
            switch (Cuffs.Kind(cuff))
            {
                case PartGroup.UpperCuff: dir = NVector3.Transform(dir, System.Numerics.Quaternion.Inverse(o.UpperTurn[side])); break;
                case PartGroup.WristCuff: dir = NVector3.Transform(dir, System.Numerics.Quaternion.Inverse(o.WristTurn[side])); break;
                case PartGroup.AnkleCuff: dir = NVector3.Transform(dir, System.Numerics.Quaternion.CreateFromAxisAngle(NVector3.UnitX, -o.AnkleTilt)); break;
            }
            NVector3 axis = NVector3.Normalize(Cuffs.Model(m, cuff).Axis);
            dir -= axis * NVector3.Dot(dir, axis);
            return dir.LengthSquared() < 1e-8f ? NVector3.Zero : NVector3.Normalize(dir);
        }

        private readonly List<GameObject> _markers = new List<GameObject>();

        /// <summary>Diagnostics (Debug.Disable bit 8): a 4 mm sphere at a skin point the fit used. They showed the bake on her
        /// drawn skin (the upper arm really 3.5 cm from its bone on the first save, the estimate said 2) [observed 2026-10-27].</summary>
        private void Marker(DeviceFrame f, NVector3 at)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.transform.position = HogtiePlacer.U(f.Point(at));
            go.transform.localScale = Vector3.one * 0.004f;
            _markers.Add(go);
        }

        public void ClearMarkers()
        {
            foreach (var m in _markers) if (m != null) UnityEngine.Object.Destroy(m);
            _markers.Clear();
        }

        private static string Cm(float m) => (m * 100f).ToString("0.0", CultureInfo.InvariantCulture);

        /// <summary>Her body mesh as drawn now, in device coordinates.</summary>
        private bool Bake(Animator anim, DeviceFrame f)
        {
            if (_body == null)
                foreach (var r in anim.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                    if (r != null && r.name == "Selestia_body") _body = r;
            if (_body == null || !_body.enabled) return false;
            if (_bake == null) _bake = new Mesh { name = "Hogtie.BodyBake", hideFlags = HideFlags.DontUnloadUnusedAsset };
            _body.BakeMesh(_bake, true);
            var verts = _bake.vertices;
            Transform t = _body.transform;
            NVector3 np = HogtiePlacer.N(t.position), ns = HogtiePlacer.N(t.lossyScale);
            Quaternion rot = t.rotation;
            var nr = new System.Numerics.Quaternion(rot.x, rot.y, rot.z, rot.w);
            _skin.Clear();
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                _skin.Add(f.ToDevice(np + NVector3.Transform(new NVector3(v.x, v.y, v.z) * ns, nr)));
            }
            return _skin.Count > 0;
        }

        public void Destroy()
        {
            if (_bake != null) UnityEngine.Object.Destroy(_bake);
            _bake = null;
            _body = null;
        }
    }
}
