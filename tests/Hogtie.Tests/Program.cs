using System;
using System.Globalization;
using System.IO;
using System.Numerics;
using Hogtie.Logic;

namespace Hogtie.Tests
{
    internal static class Program
    {
        private static int _passed, _failed;

        private static void Check(bool ok, string name)
        {
            if (ok) { _passed++; Console.WriteLine("  PASS  " + name); }
            else { _failed++; Console.WriteLine("  FAIL  " + name); }
        }

        private static bool Near(float a, float b, float eps = 1e-3f) => Math.Abs(a - b) <= eps;

        private static int Main()
        {
            string modelPath = Path.Combine(AppContext.BaseDirectory, "hogtie.htm");
            if (File.Exists(modelPath))
            {
                HogtieModel m;
                using (var s = File.OpenRead(modelPath)) m = HogtieModel.Read(s);
                ModelTests(m);
                PoseTests(m);
            }
            else Console.WriteLine("  SKIP  model tests (" + modelPath + " missing: export it with src/Hogtie/tools/export_model.py)");
            FormatErrorTests();
            FrameTests();
            CuffFitTests();

            Console.WriteLine(_passed + " passed, " + _failed + " failed");
            return _failed == 0 ? 0 : 1;
        }

        private static void ModelTests(HogtieModel m)
        {
            Check(m.Parts.Count == 12, "model: 12 groups (" + m.Parts.Count + ")");
            Check(m.Parts.TrueForAll(p => p.Positions.Length == p.Normals.Length && p.Indices.Length > 0), "model: every group has geometry");
            Check(m.Parts.TrueForAll(p => (p.KeyDeltas != null) == (m.CuffOf(p) != null)), "model: exactly the cuffs carry a size key");
            Check(Near(m.Upper[0].Center.X, -m.Upper[1].Center.X) && Near(m.Wrist[0].Center.X, -m.Wrist[1].Center.X)
                  && Near(m.Ankle[0].Center.X, -m.Ankle[1].Center.X), "model: sides mirror");
            Check(m.Collar.Center.Y < m.Upper[1].Center.Y && m.Upper[1].Center.Y < m.Wrist[1].Center.Y && m.Wrist[1].Center.Y < m.PostY,
                  "model: collar, upper-arm cuffs, wrist cuffs, post from the front");
            Check(m.Collar.Axis.Y < 0 && m.Collar.Axis.Z > 0, "model: the collar's axis points up towards the head");
            Check(m.Ankle[1].Axis.Y < 0 && m.Ankle[1].Axis.Z > 0 && Near(Math.Abs(m.Ankle[1].Axis.Y), m.Ankle[1].Axis.Z, 0.02f),
                  "model: the ankle cuffs lean 45 degrees, up towards the head");
            // The size key doubles the ring about the anchor (the export checks the radius; here the centre).
            foreach (var c in new[] { m.Collar, m.Upper[1], m.Wrist[1], m.Ankle[1] })
                Check(Vector3.Distance(c.CenterAt(-1f), c.Anchor) < 1e-5f && Near(c.KeyFor(c.InnerRadius * 2f), 1f), "model: key arithmetic for r " + c.InnerRadius);
            var cuff = m.Parts.Find(p => p.Group == PartGroup.UpperCuff && p.Side == 1);
            float minR = float.MaxValue;
            var cu = m.Upper[1];
            foreach (var (v, d) in Zip(cuff.Positions, cuff.KeyDeltas))
            {
                Vector3 q = v + d * -0.5f - cu.CenterAt(-0.5f);
                minR = Math.Min(minR, new Vector2(q.X, q.Z).Length());
            }
            Check(Near(minR, cu.InnerRadius * 0.5f, 2e-4f), "model: key -0.5 halves the upper cuff's hole (" + minR + ")");
            Check(m.PostTop > m.BarZ && m.AnkleBarZ < m.BarZ && m.Collar.Center.Z < m.BarZ, "model: heights (post top > bar > ankle bar, collar below the bar)");
            float plateZ = float.MaxValue;
            foreach (var p in m.Parts) foreach (var v in p.Positions) plateZ = Math.Min(plateZ, v.Z);
            Check(Near(plateZ, 0f, 2e-3f), "model: stands on the floor (" + plateZ + ")");
        }

        private static System.Collections.Generic.IEnumerable<(Vector3, Vector3)> Zip(Vector3[] a, Vector3[] b)
        {
            for (int i = 0; i < a.Length; i++) yield return (a[i], b[i]);
        }

        // Proportions measured in game (WallRestraint's WallController.Bones, 2026-10-24): the first save's character (about 1.5 m)
        // and the much smaller "Amateur" character. Neck lengths and limb radii are estimates (see docs/knowledge).
        internal static BodyDims Adult => new BodyDims
        {
            Spine = 0.407f, HipHalf = 0.10f, HipDrop = 0.115f, Thigh = 0.318f, Shin = 0.432f, ShoulderHalf = 0.104f, ShoulderDrop = 0.034f,
            UpperArm = 0.263f, Forearm = 0.252f, NeckLength = 0.071f, BodyDepth = 0.09f, ChestFront = 0.20f, ChestAt = 0.53f, SpineLow = 0.19f, SpineHigh = 0.217f,
            NeckRadius = 0.0285f, ArmRadius = 0.0197f, WristRadius = 0.0176f, AnkleRadius = 0.026f, KneeRadius = 0.048f,
        };

        internal static BodyDims Small => new BodyDims
        {
            Spine = 0.206f, HipHalf = 0.037f, HipDrop = 0.08f, Thigh = 0.218f, Shin = 0.232f, ShoulderHalf = 0.05f, ShoulderDrop = 0.02f,
            UpperArm = 0.179f, Forearm = 0.172f, NeckLength = 0.04f, BodyDepth = 0.05f, ChestFront = 0.08f, ChestAt = 0.53f, SpineLow = 0.097f, SpineHigh = 0.109f,
            NeckRadius = 0.0144f, ArmRadius = 0.0134f, WristRadius = 0.012f, AnkleRadius = 0.014f, KneeRadius = 0.033f,
        };

        private static void PoseTests(HogtieModel m)
        {
            foreach (var (name, b) in new[] { ("adult", Adult), ("small", Small) })
            {
                var p = PoseParams.Default;
                var o = HogtiePose.Layout(m, b, p);
                Check(o.Problem == null, name + ": a pose was found (" + o.Problem + ")");
                if (o.Problem != null) continue;
                Console.WriteLine("        " + name + ": scale " + F(o.Scale) + ", ankle bar raised " + F(o.AnkleBarRaise * o.Scale) + " m, shins lean "
                                  + F(o.ShinLean * 57.3f) + " deg (cuffs turned " + F(o.AnkleTilt * 57.3f) + "), belly " + F(o.Belly) + " m up, arm cuffs slid " + F(o.UpperSlide[1] * o.Scale) + " / " + F(o.WristSlide[1] * o.Scale)
                                  + " m; hips " + V(o.Hips) + ", knee " + V(o.Knee[1])
                                  + ", foot " + V(o.Foot[1]) + ", hand " + V(o.Hand[1]) + "; cost " + o.Cost.ToString("0.000000", CultureInfo.InvariantCulture)
                                  + (o.Costs.Length > 0 ? " (" + o.Costs + ")" : ""));
                Check(o.Costs.Length == 0 || o.Cost < 0.03f * 0.03f, name + ": every rule met within 3 cm (" + o.Costs + ")");
                for (int s = 0; s < 2; s++)
                {
                    Check(Near(Vector3.Distance(o.Thigh[s], o.Knee[s]), b.Thigh) && Near(Vector3.Distance(o.Knee[s], o.Foot[s]), b.Shin)
                          && Near(Vector3.Distance(o.Shoulder[s], o.Elbow[s]), b.UpperArm) && Near(Vector3.Distance(o.Elbow[s], o.Hand[s]), b.Forearm),
                          name + ": limbs keep their lengths (side " + s + ")");
                    Check(Math.Sign(o.Knee[s].X) == (s == 0 ? -1 : 1) && Math.Sign(o.Hand[s].X) == (s == 0 ? -1 : 1), name + ": side " + s + " is on its side");
                }
                Check(Near(Vector3.Distance(o.Hips, o.Chest), b.SpineLow, 2e-3f) && Near(Vector3.Distance(o.Chest, o.Neck), b.SpineHigh, 2e-3f),
                      name + ": spine keeps its lengths");
                float arch = (float)Math.Acos(Vector3.Dot(Vector3.Normalize(o.Chest - o.Hips), Vector3.Normalize(o.Neck - o.Chest)));
                Check(Near(arch, p.ArchAngle, 0.03f), name + ": back arched by ArchAngle (" + F(arch * 57.3f) + " deg)");
                Check(PointToLine(o.Chest, o.Hips, o.Neck) > 0.01f && o.Chest.Z < o.Hips.Z + (o.Neck.Z - o.Hips.Z) * (o.Chest.Y - o.Hips.Y) / (o.Neck.Y - o.Hips.Y),
                      name + ": the arch bends her front down");
                Check(o.Belly >= p.FloorClearance - 0.003f && o.Belly < p.FloorClearance + 0.04f && o.Knee[0].Z > b.KneeRadius * 0.9f,
                      name + ": hips hang low but float (belly " + F(o.Belly) + " m), knees off the floor");
                Check(o.Thigh[1].Y < m.PostY * o.Scale && o.Knee[1].Y > m.PostY * o.Scale, name + ": the post between her thighs");
                Check(o.Neck.Y < o.Hips.Y && o.Foot[1].Z > o.Knee[1].Z && o.Foot[1].Y < o.Knee[1].Y, name + ": head forward, feet up towards her hands");

                // The limbs pass through the cuffs where they are placed.
                Vector3 collar = m.Collar.CenterAt(o.CollarKey) * o.Scale;
                Check(PointToLine(collar, o.Neck, o.Head) < 2e-3f, name + ": the neck goes through the collar");
                Check(PointToLine(HogtiePose.UpperCenter(m, o, 1), o.Shoulder[1], o.Elbow[1]) < 2e-3f, name + ": the upper arm goes through its cuff");
                Check(PointToLine(HogtiePose.WristCenter(m, o, 1), o.Elbow[1], o.Hand[1]) < 2e-3f, name + ": the forearm goes through its cuff");
                for (int s = 0; s < 2; s++)
                    Check(Vector3.Dot(Vector3.Transform(Vector3.UnitY, o.UpperTurn[s]), Vector3.Normalize(o.Elbow[s] - o.Shoulder[s])) > 0.99999f
                          && Vector3.Dot(Vector3.Transform(Vector3.UnitY, o.WristTurn[s]), Vector3.Normalize(o.Hand[s] - o.Elbow[s])) > 0.99999f,
                          name + ": arm cuffs turned onto the limbs (side " + s + ")");
                Vector3 ankle = HogtiePose.AnkleCenter(m, o, 1);
                Check(Near(PointToLine(ankle, o.Knee[1], o.Foot[1]), p.AnkleBoneOff * b.AnkleRadius, 1e-3f),
                      name + ": the shin goes through its cuff, the bone towards the cuff's front");
                Check(Near(Vector3.Dot(o.Foot[1] - ankle, Vector3.Normalize(o.Foot[1] - o.Knee[1])), p.AnkleOnShin * b.Shin, 2e-3f),
                      name + ": the ankle cuff sits at the ankle");
                Vector3 a = m.Ankle[1].Axis;
                float ct = (float)Math.Cos(o.AnkleTilt), st = (float)Math.Sin(o.AnkleTilt);
                var turnedAxis = new Vector3(a.X, a.Y * ct - a.Z * st, a.Y * st + a.Z * ct);
                Check(Vector3.Dot(Vector3.Normalize(turnedAxis), Vector3.Normalize(o.Foot[1] - o.Knee[1])) > 0.9999f, name + ": the ankle cuff turned to the shin's angle");
                Check(Near(o.UpperSlide[0], o.UpperSlide[1], 1e-5f) && Near(o.WristSlide[0], o.WristSlide[1], 1e-5f), name + ": arm cuffs placed alike on both sides");
                var moved = HogtiePose.Layout(m, b, p);
                Vector3 sh = moved.Shoulder[1] + new Vector3(0, 0.02f, 0.03f);
                Check(HogtiePose.FitArm(m, b, p, moved, 1, sh) && Near(Vector3.Distance(moved.Shoulder[1], moved.Elbow[1]), b.UpperArm)
                      && PointToLine(HogtiePose.UpperCenter(m, moved, 1), sh, moved.Elbow[1]) < 2e-3f
                      && moved.UpperSlide[1] > o.UpperSlide[1], name + ": the arm follows a moved shoulder, its cuff slides back");
                Check(Near(m.Upper[1].InnerRadius * (1f + o.UpperKey) * o.Scale, b.ArmRadius + p.Clearance, 1e-4f)
                      && Near(m.Ankle[1].InnerRadius * (1f + o.AnkleKey) * o.Scale, b.AnkleRadius + p.Clearance, 1e-4f),
                      name + ": the cuffs are sized to her limbs");
            }
        }

        private static float PointToLine(Vector3 q, Vector3 a, Vector3 b)
        {
            Vector3 d = Vector3.Normalize(b - a);
            Vector3 v = q - a;
            return (v - d * Vector3.Dot(v, d)).Length();
        }

        private static void FormatErrorTests()
        {
            bool Throws(byte[] data)
            {
                try { HogtieModel.Read(new MemoryStream(data)); return false; }
                catch (InvalidDataException) { return true; }
                catch (EndOfStreamException) { return true; }
            }
            Check(Throws(new byte[] { 1, 2, 3, 4, 1, 0, 0, 0 }), "format: wrong magic rejected");
            Check(Throws(new byte[] { 0x48, 0x47, 0x54, 0x44, 9, 0, 0, 0 }), "format: unknown version rejected");
            Check(Throws(new byte[] { 0x48, 0x47, 0x54, 0x44, 1, 0, 0, 0, 0, 0 }), "format: truncated file rejected");
        }

        private static void FrameTests()
        {
            // Unity's numbers: she faces +z (forward), up +y; the device's Back is -forward, so its Side is +x = her right.
            var f = new DeviceFrame(new Vector3(1, 2, 3), new Vector3(0, 0, -1), Vector3.UnitY);
            Check(Vector3.Distance(f.Side, Vector3.UnitX) < 1e-6f, "frame: side = her right (+x when she faces +z)");
            Vector3 p = new Vector3(0.1f, -0.4f, 0.3f);
            Check(Vector3.Distance(f.ToDevice(f.Point(p)), p) < 1e-5f, "frame: device -> world -> device");
            var q = f.Rotation();
            Check(Vector3.Distance(Vector3.Transform(Vector3.UnitX, q), f.Side) < 1e-5f && Vector3.Distance(Vector3.Transform(Vector3.UnitY, q), f.Back) < 1e-5f
                  && Vector3.Distance(Vector3.Transform(Vector3.UnitZ, q), f.Up) < 1e-5f, "frame: rotation maps the axes");
        }

        private static void CuffFitTests()
        {
            // Points on a circle of radius 2 around (1, -1), plus some inside: the smallest circle is that circle.
            var pts = new System.Collections.Generic.List<Vector2>();
            for (int i = 0; i < 40; i++)
            {
                float a = i * 0.157f;
                pts.Add(new Vector2(1 + 2 * (float)Math.Cos(a), -1 + 2 * (float)Math.Sin(a)));
                pts.Add(new Vector2(1 + 0.5f * (float)Math.Cos(a * 3), -1 + 0.7f * (float)Math.Sin(a)));
            }
            var (c, major, minor, _) = CuffFit.Ellipse(pts);
            Check(Near(c.X, 1f, 1e-3f) && Near(c.Y, -1f, 1e-3f) && Near(major, 2f, 2e-2f) && Near(minor, 2f, 2e-2f), "cuff fit: round points -> their circle (" + c + ", " + major + " x " + minor + ")");

            // A limb along +y: an oval skin 3 x 2 cm around a bone 1 cm off its middle; a second limb nearby is left out.
            var skin = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < 60; i++)
                for (int k = -2; k <= 2; k++)
                {
                    float a = i * 0.1047f;
                    skin.Add(new Vector3(0.01f + 0.03f * (float)Math.Cos(a), k * 0.01f, 0.02f * (float)Math.Sin(a)));
                    skin.Add(new Vector3(0.12f + 0.03f * (float)Math.Cos(a), k * 0.01f, 0.02f * (float)Math.Sin(a))); // the other limb
                }
            var own = new Segment(new Vector3(0, -0.2f, 0), new Vector3(0, 0.2f, 0));
            var other = new Segment(new Vector3(0.12f, -0.2f, 0), new Vector3(0.12f, 0.2f, 0));
            var fit = CuffFit.Fit(skin, Vector3.Zero, Vector3.UnitY, 0.025f, own, new[] { other }, 0.08f);
            Check(fit.Ok && Near(fit.Major, 0.03f, 1e-3f) && Near(fit.Minor, 0.02f, 1e-3f) && Near(fit.Offset.X, 0.01f, 1e-3f) && Near(fit.Offset.Z, 0f, 1e-3f)
                  && Math.Abs(fit.MinorDir.Z) > 0.999f,
                  "cuff fit: oval limb off its bone -> 3 x 2 cm, centre 1 cm off, short axis across (" + fit.Major + " x " + fit.Minor + ", " + fit.Offset + ", " + fit.Points + " points)");
            var none = CuffFit.Fit(skin, new Vector3(0, 1, 0), Vector3.UnitY, 0.025f, own, new[] { other }, 0.08f);
            Check(!none.Ok, "cuff fit: no skin within the cuff's width -> no fit");
        }

        private static string F(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);
        private static string V(Vector3 v) => "(" + F(v.X) + ", " + F(v.Y) + ", " + F(v.Z) + ")";
    }
}
