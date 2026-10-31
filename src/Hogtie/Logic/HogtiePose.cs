using System;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Hogtie.Logic
{
    /// <summary>Her proportions (metres), measured from the bones once per lock; lengths do not depend on the pose.</summary>
    public struct BodyDims
    {
        public float Spine;        // hips bone -> neck bone
        public float HipHalf;      // hips centre -> thigh joint, sideways
        public float HipDrop;      // hips bone from the middle of the thigh joints
        public float Thigh, Shin;
        public float ShoulderHalf; // neck -> upper arm joint, sideways
        public float ShoulderDrop; // upper arm joints below the neck bone, along the spine
        public float ShoulderFront; // upper arm joints in front of the neck bone (towards her chest)
        public float UpperArm, Forearm;
        public float NeckLength;   // neck bone -> head bone
        public float BodyDepth;    // hips / chest bone -> skin of the back (and of the belly)
        public float ChestFront;   // the chest's front (nipples) in front of the spine, at ChestAt
        public float ChestAt;      // where along the spine (hips 0 .. neck 1) the chest's front is
        public float SpineLow, SpineHigh; // hips bone -> chest bone, chest bone -> neck bone (the arch bends between them)
        public float NeckRadius, ArmRadius, WristRadius, AnkleRadius, KneeRadius;
    }

    /// <summary>Tunable shape of the pose (config).</summary>
    public struct PoseParams
    {
        public float CuffOnUpperArm;   // upper-arm cuff along the upper arm (0 shoulder .. 1 elbow)
        public float CuffOnForearm;    // wrist cuff along the forearm (0 elbow .. 1 wrist)
        public float AnkleOnShin;      // ankle cuff place: ankle joint -> cuff, in shin lengths
        public float ArchAngle;        // her back's arch: lower back against chest (radians)
        public float AnkleBoneOff;     // the shin bone off the ankle cuff's centre towards the shin's front, in ankle radii (the
                                       // heel and calf behind the bone are the leg's thick side)
        public float HipsDown;         // how strongly the fit lets her hips hang down (towards the floor clearance)
        public float CollarOnNeck;     // the collar along the neck (0 neck joint .. 1 head joint)
        public float Clearance;        // gap between limb and cuff (m)
        public float FloorClearance;   // belly and knees above the floor (m)
        public float CrotchClearance;  // the thigh joints in front of the post, in thigh lengths
        public float MaxArmLift;       // upper arms rise at most this far from level (radians)

        public static PoseParams Default => new PoseParams
        {
            CuffOnUpperArm = 0.75f, CuffOnForearm = 0.85f, AnkleOnShin = 0.08f, CollarOnNeck = 0.45f, Clearance = 0.004f,
            FloorClearance = 0.03f, CrotchClearance = 0.15f, MaxArmLift = 60f * (float)Math.PI / 180f,
            ArchAngle = 35f * (float)Math.PI / 180f, HipsDown = 0.3f, AnkleBoneOff = 0.3f,
        };
    }

    /// <summary>
    /// The fitted device and her pose, in device coordinates (metres, already scaled: model point * <see cref="Scale"/>; x = her
    /// right, y = towards her feet, z = up from the floor). Index 0 = model x &lt; 0 = her left, 1 = her right.
    /// </summary>
    public sealed class HogtieLayout
    {
        public float Scale;                    // the whole device
        public float AnkleBarRaise;            // ankle bar and ankle cuffs moved up the post (model units)
        public float AnkleTilt;                // ankle cuffs turned on the ankle bar about their anchors (radians, + = back end up)
        public float ShinLean;                 // the shins' lean from upright towards her head (radians)
        // Per side: arm cuffs turned onto the limb's direction (about their anchor) and placed so the limb runs through their
        // centre: anchor position (model units) and turn; the slide along the bar (model units, + = towards the post) carries the
        // mounts. They follow her drawn shoulders while she is held (FitArm).
        public Vector3[] UpperAnchor = new Vector3[2], WristAnchor = new Vector3[2];
        public Quaternion[] UpperTurn = { Quaternion.Identity, Quaternion.Identity }, WristTurn = { Quaternion.Identity, Quaternion.Identity };
        public float[] UpperSlide = new float[2], WristSlide = new float[2];
        public float CollarKey, UpperKey, WristKey, AnkleKey; // size keys from her estimated limbs
        // Per cuff (index: Cuffs.Collar, UpperL/R, WristL/R, AnkleL/R): the size key in use and a shift of the cuff (device m)
        // across the limb - both start from the estimate and are refitted to her baked skin while she is held (CuffFit).
        public float[] CuffKey = new float[Cuffs.Count];
        public Vector3[] CuffShift = new Vector3[Cuffs.Count];
        // Per cuff: the ring squashed to an oval (factor along a direction across its axis, in the cuff's model coordinates);
        // 1 = round.
        public float[] CuffSquash = { 1, 1, 1, 1, 1, 1, 1 };
        public Vector3[] CuffSquashDir = new Vector3[Cuffs.Count];
        public Vector3 Hips, Chest, Neck, Head;
        public float Belly;                    // lowest point of her belly above the floor (m)
        public Vector3[] Thigh = new Vector3[2], Knee = new Vector3[2], Foot = new Vector3[2];
        public Vector3[] Shoulder = new Vector3[2], Elbow = new Vector3[2], Hand = new Vector3[2];
        public float Cost;                     // 0 = every rule met; see Costs
        public string Costs = "";              // the rules not met, with their amounts
        public string Problem;                 // null when a pose was found
    }

    /// <summary>
    /// The hogtie: she hangs face down in the device, hips floating low with her back arched. The collar holds her neck (the head
    /// follows its axis); the ankle cuffs hold her ankles, the shins lean from the knees (low, behind the post) up towards her
    /// hands; the thighs reach forward past the post to her hips, the arched spine runs from there to the collar. The arms
    /// reach back through the cuffs on the bar but do not shape the fit yet.
    /// The device is fitted to her: scaled as a whole, the cuffs sized to her limbs with their size keys, the ankle bar moved up
    /// or down the post and the ankle cuffs turned on it to her shins, the arm cuffs slid along and turned on the bar. A small
    /// search picks the fit that breaks the fewest rules (belly, chest and knees above the floor, thighs past the post, back
    /// under the bar) and prefers the hips hanging low and the ankle cuffs near the model's 45 degrees.
    /// </summary>
    /// <summary>Cuff indices: the collar, then the upper-arm, wrist and ankle cuffs per side (0 = her left, 1 = her right).</summary>
    public static class Cuffs
    {
        public const int Collar = 0, Count = 7;
        public static int Upper(int side) => 1 + side;
        public static int Wrist(int side) => 3 + side;
        public static int Ankle(int side) => 5 + side;
        public static int Side(int cuff) => cuff == Collar ? 2 : (cuff - 1) % 2;
        public static PartGroup Kind(int cuff) => cuff == Collar ? PartGroup.Collar : cuff <= 2 ? PartGroup.UpperCuff : cuff <= 4 ? PartGroup.WristCuff : PartGroup.AnkleCuff;
        public static string Name(int cuff) => cuff == Collar ? "collar" : Kind(cuff) switch
        {
            PartGroup.UpperCuff => "upper arm", PartGroup.WristCuff => "wrist", _ => "ankle",
        } + (Side(cuff) == 0 ? " L" : " R");

        /// <summary>The cuff index of a model part (-1 for parts that are not cuffs).</summary>
        public static int Of(ModelPart p) => p.Group switch
        {
            PartGroup.Collar => Collar,
            PartGroup.UpperCuff => Upper(p.Side),
            PartGroup.WristCuff => Wrist(p.Side),
            PartGroup.AnkleCuff => Ankle(p.Side),
            _ => -1,
        };

        public static Cuff Model(HogtieModel m, int cuff) => Kind(cuff) switch
        {
            PartGroup.Collar => m.Collar,
            PartGroup.UpperCuff => m.Upper[Side(cuff)],
            PartGroup.WristCuff => m.Wrist[Side(cuff)],
            _ => m.Ankle[Side(cuff)],
        };
    }

    public static class HogtiePose
    {
        public const float ScaleMin = 0.3f, ScaleMax = 1.6f, ScaleStep = 0.01f, AnkleStep = 0.01f;
        public const float ShinMin = 0.1f, ShinMax = 1.3f, ShinStep = 0.05f; // shin lean from upright (radians, ~6 .. 75 degrees)

        public static HogtieLayout Layout(HogtieModel m, in BodyDims b, in PoseParams p)
        {
            float bestCost = float.MaxValue;
            float bs = 0, baz = 0, bphi = 0;
            int bsol = -1;
            for (float s = ScaleMin; s <= ScaleMax + 1e-4f; s += ScaleStep)
            {
                float azLow = 0.12f * s, azHigh = (m.PostTop - 0.03f) * s;
                for (float az = azLow; az <= azHigh + 1e-4f; az += AnkleStep)
                    for (float phi = ShinMin; phi <= ShinMax + 1e-4f; phi += ShinStep)
                        for (int sol = 0; sol < 2; sol++)
                        {
                            float c = Evaluate(m, b, p, s, az, phi, sol, null);
                            if (c < bestCost - 1e-9f) { bestCost = c; bs = s; baz = az; bphi = phi; bsol = sol; }
                        }
            }
            var o = new HogtieLayout();
            if (bsol < 0)
            {
                o.Problem = "her proportions do not fit the device";
                return o;
            }
            Evaluate(m, b, p, bs, baz, bphi, bsol, o);
            return o;
        }

        /// <summary>The model's lean of the ankle cuffs: from upright towards the head (radians; 45 degrees in the model).</summary>
        public static float ModelShinLean(HogtieModel m)
        {
            Vector3 a = Vector3.Normalize(m.Ankle[1].Axis);
            return (float)Math.Atan2(-a.Y, a.Z);
        }

        /// <summary>An ankle cuff's centre in device metres: sized, turned on the ankle bar by <see cref="HogtieLayout.AnkleTilt"/>
        /// about its anchor, moved up the post.</summary>
        public static Vector3 AnkleCenter(HogtieModel m, HogtieLayout o, int side) => AnkleCenter(m.Ankle[side], o.AnkleKey, o.AnkleTilt, o.AnkleBarRaise, o.Scale);

        private static Vector3 AnkleCenter(Cuff cuff, float key, float tilt, float raise, float s)
        {
            Vector3 d = cuff.CenterAt(key) - cuff.Anchor;
            float c = (float)Math.Cos(tilt), sn = (float)Math.Sin(tilt);
            var turned = new Vector3(d.X, d.Y * c - d.Z * sn, d.Y * sn + d.Z * c);
            return (cuff.Anchor + turned + new Vector3(0, 0, raise)) * s;
        }

        /// <summary>The rules' cost of one fit (scale, ankle bar height in m, the shins' lean from upright towards the head, which
        /// way the thighs meet the spine); fills <paramref name="o"/> when given. <see cref="float.MaxValue"/> when the body
        /// cannot reach.</summary>
        public static float Evaluate(HogtieModel m, in BodyDims b, in PoseParams p, float s, float az, float phi, int sol, HogtieLayout o)
        {
            // Sizes: each cuff's key from her limb (model units, the device is scaled by s).
            float collarKey = m.Collar.KeyFor((b.NeckRadius + p.Clearance) / s);
            float upperKey = m.Upper[1].KeyFor((b.ArmRadius + p.Clearance) / s);
            float wristKey = m.Wrist[1].KeyFor((b.WristRadius + p.Clearance) / s);
            float ankleKey = m.Ankle[1].KeyFor((b.AnkleRadius + p.Clearance) / s);
            if (collarKey <= -0.9f || upperKey <= -0.9f || wristKey <= -0.9f || ankleKey <= -0.9f) return float.MaxValue;
            float raise = az / s - m.AnkleBarZ;

            // Neck and head along the collar's axis.
            Vector3 cAxis = Vector3.Normalize(m.Collar.Axis);
            Vector3 collar = m.Collar.CenterAt(collarKey) * s;
            Vector3 neck = collar - cAxis * (p.CollarOnNeck * b.NeckLength);

            // Legs: the ankle cuffs turn on the ankle bar to the shin's lean phi; the shin goes through the cuff, the ankle
            // joint AnkleOnShin below it.
            float tilt = phi - ModelShinLean(m);
            var shinUp = new Vector3(0, -(float)Math.Sin(phi), (float)Math.Cos(phi)); // from the knee up towards the foot
            Vector3 ankleCuff = AnkleCenter(m.Ankle[1], ankleKey, tilt, raise, s);
            // The shin bone's side of the leg: the bones are turned with the smallest rotation from standing, so the shin's front
            // ends up facing forward and down, towards her thighs; the calf faces back and up [observed 2026-10-27: offset the
            // other way the calf came through the cuff's back].
            var shinFront = new Vector3(0, -(float)Math.Cos(phi), -(float)Math.Sin(phi));
            Vector3 foot = ankleCuff + shinUp * (p.AnkleOnShin * b.Shin) + shinFront * (p.AnkleBoneOff * b.AnkleRadius);
            Vector3 knee = foot - shinUp * b.Shin;

            // Spine, arched: the lower back (thigh joints -> chest bone, with the hip drop) and the chest (chest bone -> neck)
            // meet at ArchAngle, bending her front down and her hips down. The thigh joints sit where the thigh from the knee
            // meets the arched spine from the neck - two circles in the middle plane.
            float low = b.SpineLow + b.HipDrop, high = b.SpineHigh;
            float ca = (float)Math.Cos(p.ArchAngle);
            float reach = (float)Math.Sqrt(low * low + high * high + 2 * low * high * ca);
            float dx = knee.X - b.HipHalf;
            float t2 = b.Thigh * b.Thigh - dx * dx;
            if (t2 <= 0) return float.MaxValue;
            if (!Intersect(new Vector2(knee.Y, knee.Z), (float)Math.Sqrt(t2), new Vector2(neck.Y, neck.Z), reach, sol, out Vector2 mid2))
                return float.MaxValue;
            var mid = new Vector3(0, mid2.X, mid2.Y);
            // The chest bone: the apex of the triangle (mid, chest, neck) on her front side (below the line).
            Vector3 line = neck - mid;
            float dl = line.Length();
            Vector3 e = line / dl;
            var below = new Vector3(0, -e.Z, e.Y);
            if (below.Z > 0) below = -below;
            float along = (low * low - high * high + dl * dl) / (2 * dl);
            float h = (float)Math.Sqrt(Math.Max(0, low * low - along * along));
            Vector3 chest = mid + e * along + below * h;
            Vector3 lowDir = Vector3.Normalize(chest - mid), highDir = Vector3.Normalize(neck - chest);
            Vector3 hips = mid + lowDir * b.HipDrop;
            Vector3 Front(Vector3 dir) { var n = new Vector3(0, -dir.Z, dir.Y); return n.Z > 0 ? -n : n; }

            // The rules. Her front is below the spine (face down).
            var c = new Rules(o != null);
            float belly = (hips + Front(lowDir) * b.BodyDepth).Z;
            c.Add("belly on the floor", p.FloorClearance - belly);
            float chestAlong = b.ChestAt * (b.SpineLow + b.SpineHigh);
            Vector3 chestSpine = chestAlong <= b.SpineLow ? hips + lowDir * chestAlong : chest + highDir * (chestAlong - b.SpineLow);
            Vector3 chestFrontDir = chestAlong <= b.SpineLow ? Front(lowDir) : Front(highDir);
            c.Add("chest on the floor", p.FloorClearance - (chestSpine + chestFrontDir * Math.Max(b.ChestFront, b.BodyDepth)).Z);
            c.Add("knees on the floor", b.KneeRadius + p.FloorClearance - knee.Z);
            c.Add("hips against the post", mid.Y - ((m.PostY - m.PostRadius) * s - p.CrotchClearance * b.Thigh));
            float barLow = (m.BarZ - m.BarRadius) * s;
            float back = 0f;
            for (int i = 0; i <= 8; i++)
            {
                float u = i / 8f;
                Vector3 q = u < 0.5f ? hips + (chest - hips) * (u * 2) : chest + (neck - chest) * ((u - 0.5f) * 2);
                Vector3 n = u < 0.5f ? Front(lowDir) : Front(highDir);
                if (q.Y >= m.BarFrontY * s && q.Y <= m.BarBackY * s) back = Math.Max(back, (q - n * b.BodyDepth).Z - barLow);
            }
            c.Add("back against the bar", back);
            c.Add("legs crossed", 0.6f * b.KneeRadius - (knee.X - b.KneeRadius));
            // Preferences: hips hanging low (the belly just above the floor), the ankle cuffs near the model's lean, a scale near
            // her size.
            c.Prefer((belly - p.FloorClearance) * p.HipsDown);
            c.Prefer(tilt * 0.03f);
            c.Prefer((s - b.Spine / 0.41f) * 0.005f);

            if (o != null)
            {
                o.Scale = s;
                o.AnkleBarRaise = raise;
                o.AnkleTilt = tilt;
                o.ShinLean = phi;
                o.CollarKey = collarKey; o.UpperKey = upperKey; o.WristKey = wristKey; o.AnkleKey = ankleKey;
                o.CuffKey[Cuffs.Collar] = collarKey;
                for (int side = 0; side < 2; side++)
                {
                    o.CuffKey[Cuffs.Upper(side)] = upperKey;
                    o.CuffKey[Cuffs.Wrist(side)] = wristKey;
                    o.CuffKey[Cuffs.Ankle(side)] = ankleKey;
                }
                o.Hips = hips; o.Chest = chest; o.Neck = neck; o.Head = neck + cAxis * b.NeckLength;
                o.Belly = belly;
                var shoulder = neck - highDir * b.ShoulderDrop + Front(highDir) * b.ShoulderFront + new Vector3(b.ShoulderHalf, 0, 0);
                for (int side = 0; side < 2; side++)
                {
                    var mx = new Vector3(side == 0 ? -1 : 1, 1, 1);
                    o.Thigh[side] = (mid + new Vector3(b.HipHalf, 0, 0)) * mx;
                    o.Knee[side] = knee * mx;
                    o.Foot[side] = foot * mx;
                    if (!FitArm(m, b, p, o, side, shoulder * mx))
                    {
                        // Arms are not part of the fit yet: unreachable cuffs leave them pointing back along the bar.
                        o.Shoulder[side] = shoulder * mx;
                        o.Elbow[side] = o.Shoulder[side] + new Vector3(0, b.UpperArm, 0);
                        o.Hand[side] = o.Elbow[side] + new Vector3(0, b.Forearm, 0);
                    }
                }
                o.Cost = c.Total;
                o.Costs = c.Text;
            }
            return c.Total;
        }

        /// <summary>
        /// One arm from its shoulder joint (device metres): the upper arm through its cuff at CuffOnUpperArm, the forearm through
        /// the wrist cuff at CuffOnForearm, the cuffs slid along the bar and turned to the limb. False (nothing changed) when the
        /// arm cannot reach the bar from there.
        /// </summary>
        public static bool FitArm(HogtieModel m, in BodyDims b, in PoseParams p, HogtieLayout o, int side, Vector3 shoulder)
        {
            float s = o.Scale;
            float upperKey = o.CuffKey[Cuffs.Upper(side)], wristKey = o.CuffKey[Cuffs.Wrist(side)];
            Vector3 upperC = m.Upper[side].CenterAt(upperKey) * s;
            if (!Through(shoulder, upperC, p.CuffOnUpperArm * b.UpperArm, out Vector3 upperAt)) return false;
            Vector3 elbow = shoulder + Vector3.Normalize(upperAt - shoulder) * b.UpperArm;
            Vector3 wristC = m.Wrist[side].CenterAt(wristKey) * s;
            if (!Through(elbow, wristC, p.CuffOnForearm * b.Forearm, out Vector3 wristAt)) return false;
            o.Shoulder[side] = shoulder;
            o.Elbow[side] = elbow;
            o.Hand[side] = elbow + Vector3.Normalize(wristAt - elbow) * b.Forearm;
            o.UpperSlide[side] = (upperAt.Y - upperC.Y) / s;
            o.WristSlide[side] = (wristAt.Y - wristC.Y) / s;
            // The cuffs' axes (+y in the model) onto the limbs - up / down and sideways (the arms come together towards the bar:
            // turned up and down only, the 6 cm bands crossed her arms by more than their clearance [observed 2026-10-27]).
            o.UpperTurn[side] = FromTo(Vector3.UnitY, Vector3.Normalize(upperAt - shoulder));
            o.WristTurn[side] = FromTo(Vector3.UnitY, Vector3.Normalize(wristAt - elbow));
            o.UpperAnchor[side] = upperAt / s - Vector3.Transform(m.Upper[side].CenterAt(upperKey) - m.Upper[side].Anchor, o.UpperTurn[side]);
            o.WristAnchor[side] = wristAt / s - Vector3.Transform(m.Wrist[side].CenterAt(wristKey) - m.Wrist[side].Anchor, o.WristTurn[side]);
            return true;
        }

        /// <summary>Arm cuff centres in device metres, as placed (anchor + turned offset).</summary>
        public static Vector3 UpperCenter(HogtieModel m, HogtieLayout o, int side) =>
            (o.UpperAnchor[side] + Vector3.Transform(m.Upper[side].CenterAt(o.CuffKey[Cuffs.Upper(side)]) - m.Upper[side].Anchor, o.UpperTurn[side])) * o.Scale;

        public static Vector3 WristCenter(HogtieModel m, HogtieLayout o, int side) =>
            (o.WristAnchor[side] + Vector3.Transform(m.Wrist[side].CenterAt(o.CuffKey[Cuffs.Wrist(side)]) - m.Wrist[side].Anchor, o.WristTurn[side])) * o.Scale;

        /// <summary>A cuff's centre in device metres with its current key, before its <see cref="HogtieLayout.CuffShift"/>.</summary>
        public static Vector3 CuffCenter(HogtieModel m, HogtieLayout o, int cuff)
        {
            int side = Cuffs.Side(cuff);
            switch (Cuffs.Kind(cuff))
            {
                case PartGroup.Collar: return m.Collar.CenterAt(o.CuffKey[cuff]) * o.Scale;
                case PartGroup.UpperCuff: return UpperCenter(m, o, side);
                case PartGroup.WristCuff: return WristCenter(m, o, side);
                default: return AnkleCenter(m.Ankle[side], o.CuffKey[cuff], o.AnkleTilt, o.AnkleBarRaise, o.Scale);
            }
        }

        /// <summary>A cuff's axis in device coordinates (the limb's direction through it).</summary>
        public static Vector3 CuffAxis(HogtieModel m, HogtieLayout o, int cuff)
        {
            int side = Cuffs.Side(cuff);
            switch (Cuffs.Kind(cuff))
            {
                case PartGroup.Collar: return Vector3.Normalize(m.Collar.Axis);
                case PartGroup.UpperCuff: return Vector3.Transform(Vector3.UnitY, o.UpperTurn[side]);
                case PartGroup.WristCuff: return Vector3.Transform(Vector3.UnitY, o.WristTurn[side]);
                default: return new Vector3(0, -(float)Math.Sin(o.ShinLean), (float)Math.Cos(o.ShinLean));
            }
        }

        /// <summary>Half a cuff's width along its axis (device metres; the collar's key scales its width too).</summary>
        public static float CuffHalfWidth(HogtieModel m, HogtieLayout o, int cuff)
        {
            Cuff c = Cuffs.Model(m, cuff);
            float w = c.Width * o.Scale / 2f;
            return Cuffs.Kind(cuff) == PartGroup.Collar ? w * (1f + o.CuffKey[cuff]) : w;
        }

        /// <summary>The shortest turn taking unit vector <paramref name="a"/> to unit vector <paramref name="b"/>.</summary>
        public static Quaternion FromTo(Vector3 a, Vector3 b)
        {
            float d = Vector3.Dot(a, b);
            if (d > 0.999999f) return Quaternion.Identity;
            if (d < -0.999999f) return Quaternion.CreateFromAxisAngle(Math.Abs(a.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ, (float)Math.PI);
            Vector3 axis = Vector3.Normalize(Vector3.Cross(a, b));
            return Quaternion.CreateFromAxisAngle(axis, (float)Math.Acos(d));
        }

        /// <summary>Of the two crossings of circle (a, ra) and circle (b, rb), the one nearer the head (sol 0) or nearer the
        /// feet (sol 1).</summary>
        private static bool Intersect(Vector2 a, float ra, Vector2 b, float rb, int sol, out Vector2 at)
        {
            at = default;
            Vector2 ab = b - a;
            float d = ab.Length();
            if (d < 1e-6f || d > ra + rb || d < Math.Abs(ra - rb)) return false;
            float along = (ra * ra - rb * rb + d * d) / (2 * d);
            float h = (float)Math.Sqrt(Math.Max(0, ra * ra - along * along));
            Vector2 e = ab / d, n = new Vector2(-e.Y, e.X);
            Vector2 p0 = a + e * along + n * h, p1 = a + e * along - n * h;
            at = (p0.X <= p1.X) == (sol == 0) ? p0 : p1;
            return true;
        }

        /// <summary>The point at distance <paramref name="along"/> from <paramref name="from"/> with the x and z of
        /// <paramref name="cuff"/>, further towards the feet (a limb reaching back to a cuff on the bar).</summary>
        private static bool Through(Vector3 from, Vector3 cuff, float along, out Vector3 at)
        {
            float dx = cuff.X - from.X, dz = cuff.Z - from.Z;
            float rem = along * along - dx * dx - dz * dz;
            at = default;
            if (rem <= 0) return false;
            at = new Vector3(cuff.X, from.Y + (float)Math.Sqrt(rem), cuff.Z);
            return true;
        }

        /// <summary>Sum of squared rule violations (m; angles are scaled to comparable amounts) with their text.</summary>
        private struct Rules
        {
            private readonly StringBuilder _text;
            public float Total;

            public Rules(bool text) { _text = text ? new StringBuilder() : null; Total = 0f; }

            public void Add(string rule, float amount)
            {
                if (amount <= 0f) return;
                Total += amount * amount;
                _text?.Append(_text.Length == 0 ? "" : ", ").Append(rule).Append(' ').Append((amount * 100f).ToString("0.0", CultureInfo.InvariantCulture)).Append(" cm");
            }

            public void Prefer(float amount) => Total += amount * amount;

            public string Text => _text == null ? "" : _text.ToString();
        }
    }
}
