using System;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using UnityEngine;
using Hogtie.Logic;
using NVector3 = System.Numerics.Vector3;

namespace Hogtie
{
    /// <summary>
    /// Puts the device on the floor where she stands: her head in the direction she faces, her hips over her feet. Refused when
    /// the floor under the device is not level and solid, or something stands where the device and she would be.
    /// </summary>
    internal static class HogtiePlacer
    {
        public struct Spot
        {
            public bool Ok;
            public string Reason;
            public DeviceFrame Frame;
            public string Floor;     // the floor collider's name
        }

        private const float FloorTolerance = 0.04f; // m up or down from her feet

        /// <summary>The device for her: its hips point over her feet, or <paramref name="ahead"/> metres in front of them.</summary>
        public static Spot Find(PlayerController player, HogtieModel m, HogtieLayout o, bool force, float ahead = 0f)
        {
            var spot = new Spot();
            Transform pt = player.transform;
            Vector3 up = Vector3.up;
            Vector3 forward = Vector3.ProjectOnPlane(pt.forward, up).normalized;
            float floorY = pt.position.y;
            Vector3 feet = pt.position + forward * ahead;
            // Device origin: the hips' floor point over her feet.
            var probe = new DeviceFrame(N(feet), N(-forward), N(up));
            NVector3 origin = probe.Point(new NVector3(-o.Hips.X, -o.Hips.Y, 0f));
            var frame = new DeviceFrame(origin, N(-forward), N(up));
            spot.Frame = frame;

            // The floor: level and within reach under the plate's corners and middle, and under the collar's end.
            float s = o.Scale, hw = m.PlateHalfWidth * s;
            float front = Math.Min(m.PlateFrontY, m.Collar.Center.Y - m.Collar.InnerRadius) * s, back = m.PlateBackY * s;
            var samples = new[] {
                new NVector3(-hw, front, 0), new NVector3(hw, front, 0), new NVector3(-hw, back, 0), new NVector3(hw, back, 0),
                new NVector3(0, (front + back) / 2f, 0), new NVector3(0, front, 0) };
            float highest = float.MinValue;
            foreach (var p in samples)
            {
                Vector3 at = U(frame.Point(p));
                if (!Cast(at + up * 0.5f, -up, 1.0f, pt, out RaycastHit hit))
                {
                    spot.Reason = "there is no floor under the device";
                    if (!force) return spot;
                    continue;
                }
                spot.Floor ??= hit.collider.name;
                highest = Math.Max(highest, hit.point.y);
                if (Math.Abs(hit.point.y - floorY) > FloorTolerance)
                {
                    spot.Reason = "the floor is not level there (" + (hit.point.y - floorY).ToString("+0.00;-0.00", System.Globalization.CultureInfo.InvariantCulture) + " m at '" + hit.collider.name + "')";
                    if (!force) return spot;
                }
            }

            // The device stands on the highest floor point under it (the plate is a few mm thick: on uneven ground it would sink in).
            if (highest > float.MinValue)
            {
                frame = new DeviceFrame(origin + N(up) * (highest - floorY), N(-forward), N(up));
                spot.Frame = frame;
            }

            // Room: a box over the device's footprint, from a few cm above the floor to the top of the device.
            float top = m.Top() * s + 0.05f;
            NVector3 mid = new NVector3(0, (front + back) / 2f, (0.05f + top) / 2f);
            Vector3 half = new Vector3(hw, (top - 0.05f) / 2f, (back - front) / 2f);
            string blocked = InTheWay(U(frame.Point(mid)), half, Quaternion.LookRotation(U(frame.Back), up), pt);
            if (blocked != null)
            {
                spot.Reason = "something is in the way (" + blocked + ")";
                if (!force) return spot;
            }
            spot.Ok = spot.Reason == null || force; // forced (diagnostics): placed anyway, Reason says what would refuse it
            return spot;
        }

        private static string InTheWay(Vector3 center, Vector3 half, Quaternion rot, Transform player)
        {
            var cols = Physics.OverlapBox(center, half, rot, ~0, QueryTriggerInteraction.Ignore);
            int npc = LayerMask.NameToLayer("Npc");
            for (int i = 0; i < cols.Length; i++)
            {
                var c = cols[i];
                if (c == null || c.transform.IsChildOf(player) || c.gameObject.layer == npc) continue;
                return c.name;
            }
            return null;
        }

        /// <summary>Nearest solid (non-trigger) hit that does not belong to the player.</summary>
        internal static bool Cast(Vector3 origin, Vector3 dir, float max, Transform player, out RaycastHit best)
        {
            best = default;
            float bestDist = float.MaxValue;
            var hits = Physics.RaycastAll(origin, dir, max, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(player) || h.distance >= bestDist) continue;
                bestDist = h.distance;
                best = h;
            }
            return bestDist < float.MaxValue;
        }

        internal static NVector3 N(Vector3 v) => new NVector3(v.x, v.y, v.z);
        internal static Vector3 U(NVector3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}
