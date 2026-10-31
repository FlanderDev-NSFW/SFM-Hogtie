using System;
using UnityEngine;
using Hogtie.Logic;
using NVector3 = System.Numerics.Vector3;

namespace Hogtie
{
    /// <summary>
    /// Measures her body from the bones and holds her in the <see cref="HogtieLayout"/>: every <c>LateUpdate</c> (after the
    /// animator, before skinning) the hips are put in place and each bone is turned so its child points at its target joint,
    /// from the hips outwards. Only directions are set, so the avatar keeps its own bone lengths. (The same approach as
    /// WallRestraint's WallPoser, see docs/knowledge/patterns.md "Bend the body on top of any animation".)
    /// </summary>
    internal static class HogtiePoser
    {
        /// <summary>Lengths straight from the bones (pose independent); limb radii and body depth estimated from them.</summary>
        public static bool Measure(Animator anim, out BodyDims b, out string problem)
        {
            b = default;
            problem = null;
            Transform B(HumanBodyBones bone) => anim.GetBoneTransform(bone);
            Transform hips = B(HumanBodyBones.Hips), neck = B(HumanBodyBones.Neck), head = B(HumanBodyBones.Head);
            Transform ulL = B(HumanBodyBones.LeftUpperLeg), ulR = B(HumanBodyBones.RightUpperLeg);
            Transform llL = B(HumanBodyBones.LeftLowerLeg), llR = B(HumanBodyBones.RightLowerLeg);
            Transform ftL = B(HumanBodyBones.LeftFoot), ftR = B(HumanBodyBones.RightFoot);
            Transform uaL = B(HumanBodyBones.LeftUpperArm), uaR = B(HumanBodyBones.RightUpperArm);
            Transform laL = B(HumanBodyBones.LeftLowerArm), laR = B(HumanBodyBones.RightLowerArm);
            Transform hdL = B(HumanBodyBones.LeftHand), hdR = B(HumanBodyBones.RightHand);
            if (hips == null || neck == null || head == null || ulL == null || ulR == null || llL == null || llR == null || ftL == null
                || ftR == null || uaL == null || uaR == null || laL == null || laR == null || hdL == null || hdR == null)
            {
                problem = "her skeleton is missing a bone";
                return false;
            }
            float D(Transform a, Transform c) => Vector3.Distance(a.position, c.position);
            b.Spine = D(hips, neck);
            Transform chestBone0 = B(HumanBodyBones.Chest);
            b.SpineLow = chestBone0 != null ? D(hips, chestBone0) : 0.47f * b.Spine;
            b.SpineHigh = chestBone0 != null ? D(chestBone0, neck) : 0.53f * b.Spine;
            b.HipHalf = D(ulL, ulR) / 2f;
            b.HipDrop = Vector3.Distance(hips.position, (ulL.position + ulR.position) / 2f);
            b.Thigh = (D(ulL, llL) + D(ulR, llR)) / 2f;
            b.Shin = (D(llL, ftL) + D(llR, ftR)) / 2f;
            b.ShoulderHalf = D(uaL, uaR) / 2f;
            // Shoulders relative to the neck in her own axes (the drawn pose at lock time: the idle animation moves them a little).
            Vector3 up = (neck.position - hips.position).normalized;
            Vector3 ahead = Vector3.Cross(uaR.position - uaL.position, up).normalized;
            Vector3 toShoulders = (uaL.position + uaR.position) / 2f - neck.position;
            b.ShoulderDrop = -Vector3.Dot(toShoulders, up);
            b.ShoulderFront = Vector3.Dot(toShoulders, ahead);
            b.UpperArm = (D(uaL, laL) + D(uaR, laR)) / 2f;
            b.Forearm = (D(laL, hdL) + D(laR, hdR)) / 2f;
            b.NeckLength = D(neck, head);
            // Estimates from bone lengths (docs/knowledge/api-notes/player-and-movement.md "Limb radii from bone lengths"): neck
            // 7 % of the spine, upper arm near the elbow 7.5 %, wrist 7 % of the forearm, knee 15 % of the thigh; the lower shin
            // ~3.2 cm on the first save = 7.5 % of the shin; back / belly 22 % of the spine (WallRestraint).
            b.BodyDepth = 0.22f * b.Spine;
            b.NeckRadius = 0.07f * b.Spine;
            b.ArmRadius = 0.075f * b.UpperArm;
            b.WristRadius = 0.07f * b.Forearm;
            // Ankle cuffs close just above the ankle joint: 5.5 % of the shin (2.4 cm on the first save; 6 % looked loose; the 3.2 cm measured 8.5 cm
            // higher up is the calf's start).
            b.AnkleRadius = 0.055f * b.Shin;
            b.KneeRadius = 0.15f * b.Thigh;
            // Her chest's front: the nipple points (ChikubiPiasController, follow her breast size setting) in front of the spine.
            // First save: 20 cm in front of the chest bone, 47 % up the spine [observed 2026-10-27]; the breasts would go
            // through the floor with the belly's depth alone.
            Transform chestBone = B(HumanBodyBones.Chest);
            Vector3 front = ahead, spineDir = up;
            b.ChestFront = 0f;
            foreach (var pias in anim.GetComponentsInChildren<ExposureUnnoticed2.Object3D.Player.Scripts.Other.ChikubiPiasController>(true))
            {
                if (pias == null) continue;
                Vector3 d = pias.transform.position - hips.position;
                float inFront = Vector3.Dot(d, front);
                if (inFront <= b.ChestFront) continue;
                b.ChestFront = inFront;
                b.ChestAt = Mathf.Clamp01(Vector3.Dot(d, spineDir) / b.Spine);
            }
            if (b.ChestFront <= 0f)
            {
                b.ChestFront = 0.45f * b.Spine; // estimate (first save's ratio)
                b.ChestAt = chestBone != null ? Mathf.Clamp01(Vector3.Dot(chestBone.position - hips.position, spineDir) / b.Spine) + 0.06f : 0.53f;
            }
            b.ChestFront += 0.01f; // the nipple itself and some give of the breasts when she lies on them
            if (b.Spine < 0.05f || b.Thigh < 0.05f || b.Shin < 0.05f || b.UpperArm < 0.05f || b.NeckLength < 0.01f)
            {
                problem = "her proportions could not be measured";
                return false;
            }
            return true;
        }

        // Bone lengths as drawn: the game changes the bones' local positions between LateUpdate and the render (WallRestraint
        // found the knee joint 6 cm along the thigh). The offset it adds is recorded at render time and used for the next aim.
        private static readonly HumanBodyBones[] Posed = {
            HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightHand };
        private static readonly Vector3[] Animated = new Vector3[Posed.Length], Offset = new Vector3[Posed.Length];
        private static bool _haveAnimated, _haveOffset;

        /// <summary>LateUpdate, before aiming: remember the animator's local positions and, once the offset is known, give the
        /// bones their drawn lengths for the aim.</summary>
        public static void UseDrawnLengths(Animator anim, bool enabled)
        {
            for (int i = 0; i < Posed.Length; i++)
            {
                var t = anim.GetBoneTransform(Posed[i]);
                if (t == null) continue;
                Animated[i] = t.localPosition;
                if (enabled && _haveOffset) t.localPosition = Animated[i] + Offset[i];
            }
            _haveAnimated = true;
        }

        /// <summary>LateUpdate, after aiming: the animator's local positions back (the game adds its offset again).</summary>
        public static void RestoreAnimatedLengths(Animator anim)
        {
            if (!_haveAnimated) return;
            for (int i = 0; i < Posed.Length; i++)
            {
                var t = anim.GetBoneTransform(Posed[i]);
                if (t != null) t.localPosition = Animated[i];
            }
        }

        /// <summary>At render time (onPreCull): the offset the game added since LateUpdate.</summary>
        public static void RecordDrawn(Animator anim)
        {
            if (!_haveAnimated) return;
            for (int i = 0; i < Posed.Length; i++)
            {
                var t = anim.GetBoneTransform(Posed[i]);
                if (t != null) Offset[i] = t.localPosition - Animated[i];
            }
            _haveOffset = true;
        }

        public static void ForgetDrawn() => _haveAnimated = _haveOffset = false;

        /// <summary>Turn <paramref name="bone"/> so that <paramref name="child"/> lies in the direction of <paramref name="target"/>.</summary>
        private static void Aim(Transform bone, Transform child, Vector3 target)
        {
            Vector3 cur = child.position - bone.position, want = target - bone.position;
            if (cur.sqrMagnitude < 1e-8f || want.sqrMagnitude < 1e-8f) return;
            bone.rotation = Quaternion.FromToRotation(cur, want) * bone.rotation;
        }

        /// <summary>The rotation taking direction a1 to b1 exactly and a2 as near to b2 as possible.</summary>
        private static Quaternion Align(Vector3 a1, Vector3 a2, Vector3 b1, Vector3 b2)
        {
            return Quaternion.LookRotation(b1, Vector3.Cross(b1, b2)) * Quaternion.Inverse(Quaternion.LookRotation(a1, Vector3.Cross(a1, a2)));
        }

        /// <summary>Put her body (hips, spine, head, legs) into the layout (LateUpdate). Layout index 0 = her left, 1 = her right.
        /// The arms come after (<see cref="ApplyArms"/>), fitted to where her shoulders then are.</summary>
        public static void ApplyBody(Animator anim, DeviceFrame f, HogtieLayout o)
        {
            Vector3 W(NVector3 p) => HogtiePlacer.U(f.Point(p));
            Transform B(HumanBodyBones bone) => anim.GetBoneTransform(bone);
            Transform hips = B(HumanBodyBones.Hips), spine = B(HumanBodyBones.Spine), chest = B(HumanBodyBones.Chest);
            Transform neck = B(HumanBodyBones.Neck), head = B(HumanBodyBones.Head);
            Transform ulL = B(HumanBodyBones.LeftUpperLeg), ulR = B(HumanBodyBones.RightUpperLeg);
            Transform llL = B(HumanBodyBones.LeftLowerLeg), llR = B(HumanBodyBones.RightLowerLeg);
            Transform ftL = B(HumanBodyBones.LeftFoot), ftR = B(HumanBodyBones.RightFoot);

            // Hips: the lower back's direction exactly, the thigh joints' line as near as possible; then moved into place.
            Transform upTo = chest != null ? chest : neck;
            NVector3 upToAt = chest != null ? o.Chest : o.Neck;
            Vector3 spineNow = upTo.position - hips.position, legsNow = ulR.position - ulL.position;
            Vector3 spineWant = W(upToAt) - W(o.Hips), legsWant = W(o.Thigh[1]) - W(o.Thigh[0]);
            hips.rotation = Align(spineNow, legsNow, spineWant, legsWant) * hips.rotation;
            hips.position = W(o.Hips);

            // The arched spine: lower back to the chest bone, chest to the neck; the head along the collar's axis.
            if (chest != null)
            {
                Aim(spine, chest, W(o.Chest));
                Aim(chest, neck, W(o.Neck));
            }
            else Aim(spine, neck, W(o.Neck));
            if (head != null) Aim(neck, head, W(o.Head));

            Aim(ulL, llL, W(o.Knee[0]));
            Aim(ulR, llR, W(o.Knee[1]));
            Aim(llL, ftL, W(o.Foot[0]));
            Aim(llR, ftR, W(o.Foot[1]));
        }

        /// <summary>Her drawn shoulder joints in device coordinates (after <see cref="ApplyBody"/>): the arms are fitted to them.
        /// Straightening her spine turns her chest, so they end up a few cm (the "Amateur" character: 5 cm) from where the
        /// layout guessed them from the standing pose [observed 2026-10-27].</summary>
        public static NVector3 Shoulder(Animator anim, DeviceFrame f, int side) =>
            f.ToDevice(HogtiePlacer.N(anim.GetBoneTransform(side == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm).position));

        /// <summary>The arms to the layout's elbows and hands (LateUpdate, after <see cref="ApplyBody"/>).</summary>
        public static void ApplyArms(Animator anim, DeviceFrame f, HogtieLayout o)
        {
            Vector3 W(NVector3 p) => HogtiePlacer.U(f.Point(p));
            Transform B(HumanBodyBones bone) => anim.GetBoneTransform(bone);
            Transform uaL = B(HumanBodyBones.LeftUpperArm), uaR = B(HumanBodyBones.RightUpperArm);
            Transform laL = B(HumanBodyBones.LeftLowerArm), laR = B(HumanBodyBones.RightLowerArm);
            Transform hdL = B(HumanBodyBones.LeftHand), hdR = B(HumanBodyBones.RightHand);
            Aim(uaL, laL, W(o.Elbow[0]));
            Aim(uaR, laR, W(o.Elbow[1]));
            Aim(laL, hdL, W(o.Hand[0]));
            Aim(laR, hdR, W(o.Hand[1]));
        }

        /// <summary>Where the bones are drawn, in device coordinates (diagnostics: how far each joint is from its target).</summary>
        public static string Misses(Animator anim, DeviceFrame f, HogtieLayout o)
        {
            float Miss(HumanBodyBones bone, NVector3 want)
            {
                var t = anim.GetBoneTransform(bone);
                return t == null ? -1f : NVector3.Distance(f.ToDevice(HogtiePlacer.N(t.position)), want);
            }
            string F(float v) => (v * 100f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            // Restraint axes against the drawn bones through them (degrees): collar vs neck -> head, ankle cuffs vs knee -> foot.
            string Deg(HumanBodyBones from, HumanBodyBones to, NVector3 axis)
            {
                Transform a = anim.GetBoneTransform(from), c = anim.GetBoneTransform(to);
                if (a == null || c == null) return "-";
                NVector3 d = NVector3.Normalize(f.ToDevice(HogtiePlacer.N(c.position)) - f.ToDevice(HogtiePlacer.N(a.position)));
                double cos = Math.Max(-1, Math.Min(1, NVector3.Dot(d, NVector3.Normalize(axis))));
                return (Math.Acos(cos) * 180 / Math.PI).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            }
            var shinUp = new NVector3(0, -(float)Math.Sin(o.ShinLean), (float)Math.Cos(o.ShinLean));
            string angles = "; axes off (deg): collar " + Deg(HumanBodyBones.Neck, HumanBodyBones.Head, HogtieController.Model.Collar.Axis)
                            + " ankle L/R " + Deg(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, shinUp) + "/"
                            + Deg(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, shinUp)
                            + " upper arm L/R " + Deg(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, NVector3.Transform(NVector3.UnitY, o.UpperTurn[0]))
                            + "/" + Deg(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, NVector3.Transform(NVector3.UnitY, o.UpperTurn[1]))
                            + " wrist L/R " + Deg(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, NVector3.Transform(NVector3.UnitY, o.WristTurn[0]))
                            + "/" + Deg(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, NVector3.Transform(NVector3.UnitY, o.WristTurn[1]));
            return angles.Substring(2) + "; misses (cm): hips " + F(Miss(HumanBodyBones.Hips, o.Hips)) + " neck " + F(Miss(HumanBodyBones.Neck, o.Neck))
                   + " head " + F(Miss(HumanBodyBones.Head, o.Head))
                   + " knee L/R " + F(Miss(HumanBodyBones.LeftLowerLeg, o.Knee[0])) + "/" + F(Miss(HumanBodyBones.RightLowerLeg, o.Knee[1]))
                   + " foot " + F(Miss(HumanBodyBones.LeftFoot, o.Foot[0])) + "/" + F(Miss(HumanBodyBones.RightFoot, o.Foot[1]))
                   + " shoulder " + F(Miss(HumanBodyBones.LeftUpperArm, o.Shoulder[0])) + "/" + F(Miss(HumanBodyBones.RightUpperArm, o.Shoulder[1]))
                   + " elbow " + F(Miss(HumanBodyBones.LeftLowerArm, o.Elbow[0])) + "/" + F(Miss(HumanBodyBones.RightLowerArm, o.Elbow[1]))
                   + " hand " + F(Miss(HumanBodyBones.LeftHand, o.Hand[0])) + "/" + F(Miss(HumanBodyBones.RightHand, o.Hand[1]));
        }
    }
}
