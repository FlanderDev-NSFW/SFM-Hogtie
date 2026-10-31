using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace Hogtie.Logic
{
    /// <summary>A bone segment of her skeleton (device coordinates) with the body part's rough thickness around it, for telling
    /// which part a skin point belongs to.</summary>
    public readonly struct Segment
    {
        public readonly Vector3 A, B;
        public readonly float Radius;
        public Segment(Vector3 a, Vector3 b, float radius = 0f) { A = a; B = b; Radius = radius; }

        /// <summary>How far outside the part's rough surface a point is (negative: inside it).</summary>
        public float Surface(Vector3 p) => Distance(p) - Radius;

        public float Distance(Vector3 p)
        {
            Vector3 ab = B - A;
            float l2 = ab.LengthSquared();
            float t = l2 < 1e-12f ? 0f : Math.Max(0f, Math.Min(1f, Vector3.Dot(p - A, ab) / l2));
            return Vector3.Distance(p, A + ab * t);
        }
    }

    /// <summary>
    /// Fits a cuff to the limb inside it from her baked skin: the skin points within the cuff's width that belong to the limb
    /// (nearer its bone segment than to any other, within a window, and not far beyond the limb's typical distance from its
    /// bone), seen along the cuff's axis. An ellipse around them gives the hole the cuff needs - limbs are oval (her forearm
    /// near the wrist about 2.7 x 2 cm on the first save) and the bone is not in their middle (heel and calf behind the shin
    /// bone): a round cuff sized for the wide side gapped on the flat sides, one sized from bone lengths clipped [observed
    /// 2026-10-27].
    /// </summary>
    public static class CuffFit
    {
        public const int MinPoints = 12;
        public const float OutlierFactor = 1.8f; // farther from the bone than this times the median: not this limb's skin

        public struct Result
        {
            public bool Ok;
            public int Points;
            public Vector3 Offset;     // from the cuff's centre to the ellipse's centre, across the axis (m)
            public float Major, Minor; // the ellipse's half axes around the limb's skin (m), Major >= Minor
            public Vector3 MinorDir;   // the short half axis's direction (unit, across the cuff's axis)
            public List<Vector3> Kept; // diagnostics: the skin points used
            public string Profile;     // diagnostics
        }

        public static Result Fit(IReadOnlyList<Vector3> skin, Vector3 center, Vector3 axis, float halfWidth, Segment own,
                                 IReadOnlyList<Segment> others, float window)
        {
            axis = Vector3.Normalize(axis);
            Vector3 u = Vector3.Normalize(Vector3.Cross(axis, Math.Abs(axis.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY));
            Vector3 v = Vector3.Cross(axis, u);
            var cand = new List<(Vector3 P, float Bone)>();
            foreach (Vector3 p in skin)
            {
                Vector3 d = p - center;
                if (Math.Abs(Vector3.Dot(d, axis)) > halfWidth) continue;
                float mine = own.Distance(p);
                if (mine > window) continue;
                // Whose skin: the nearest part's surface (a foot is thicker than a forearm: heel skin beside her wrist is nearer
                // the forearm's bone than the foot's, but nearer the foot's surface) [observed 2026-10-27].
                float mySurface = own.Surface(p);
                bool theirs = false;
                foreach (var s in others)
                    if (s.Surface(p) < mySurface) { theirs = true; break; }
                if (!theirs) cand.Add((p, mine));
            }
            var r = new Result { Kept = new List<Vector3>() };
            if (cand.Count == 0) { r.Profile = "no skin"; return r; }
            var byBone = new List<float>();
            foreach (var c in cand) byBone.Add(c.Bone);
            byBone.Sort();
            float median = byBone[byBone.Count / 2];
            var pts = new List<Vector2>();
            foreach (var c in cand)
            {
                if (c.Bone > OutlierFactor * median) continue;
                Vector3 d = c.P - center;
                pts.Add(new Vector2(Vector3.Dot(d, u), Vector3.Dot(d, v)));
                r.Kept.Add(c.P);
            }
            r.Points = pts.Count;
            // Diagnostics: the farthest skin from the bone in each third of the cuff's width (a joint at one end shows here).
            var thirds = new float[3];
            foreach (var p in r.Kept)
            {
                int i = Math.Min(2, Math.Max(0, (int)((Vector3.Dot(p - center, axis) / halfWidth + 1f) * 1.5f)));
                thirds[i] = Math.Max(thirds[i], own.Distance(p));
            }
            r.Profile = cand.Count + " in the cuff, " + (cand.Count - pts.Count) + " dropped, bone median " + Cm(median)
                        + ", farthest per third " + Cm(thirds[0]) + "/" + Cm(thirds[1]) + "/" + Cm(thirds[2]);
            if (pts.Count < MinPoints) return r;
            var (c2, major, minor, minorDir2) = Ellipse(pts);
            r.Ok = true;
            r.Offset = u * c2.X + v * c2.Y;
            r.Major = major;
            r.Minor = minor;
            r.MinorDir = Vector3.Normalize(u * minorDir2.X + v * minorDir2.Y);
            return r;
        }

        /// <summary>
        /// An ellipse around every point: its axes along the points' principal directions, centred on their extent's middle,
        /// the half axes their half extents, both grown by the same factor until no point is outside.
        /// </summary>
        public static (Vector2 Center, float Major, float Minor, Vector2 MinorDir) Ellipse(IReadOnlyList<Vector2> p)
        {
            Vector2 mean = Vector2.Zero;
            foreach (var q in p) mean += q;
            mean /= p.Count;
            float sxx = 0, syy = 0, sxy = 0;
            foreach (var q in p)
            {
                Vector2 d = q - mean;
                sxx += d.X * d.X; syy += d.Y * d.Y; sxy += d.X * d.Y;
            }
            double ang = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
            var e1 = new Vector2((float)Math.Cos(ang), (float)Math.Sin(ang));
            var e2 = new Vector2(-e1.Y, e1.X);
            float min1 = float.MaxValue, max1 = float.MinValue, min2 = float.MaxValue, max2 = float.MinValue;
            foreach (var q in p)
            {
                float a = Vector2.Dot(q - mean, e1), b = Vector2.Dot(q - mean, e2);
                min1 = Math.Min(min1, a); max1 = Math.Max(max1, a); min2 = Math.Min(min2, b); max2 = Math.Max(max2, b);
            }
            float c1 = (min1 + max1) / 2, c2 = (min2 + max2) / 2;
            float h1 = Math.Max(1e-4f, (max1 - min1) / 2), h2 = Math.Max(1e-4f, (max2 - min2) / 2);
            float grow = 1f;
            foreach (var q in p)
            {
                float a = (Vector2.Dot(q - mean, e1) - c1) / h1, b = (Vector2.Dot(q - mean, e2) - c2) / h2;
                grow = Math.Max(grow, (float)Math.Sqrt(a * a + b * b));
            }
            h1 *= grow;
            h2 *= grow;
            Vector2 center = mean + e1 * c1 + e2 * c2;
            return h1 >= h2 ? (center, h1, h2, e2) : (center, h2, h1, e1);
        }

        private static string Cm(float m) => (m * 100f).ToString("0.0", CultureInfo.InvariantCulture);
    }
}
