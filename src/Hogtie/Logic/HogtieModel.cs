using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Hogtie.Logic
{
    /// <summary>The pieces of the device the mod moves separately (see tools/export_model.py).</summary>
    public enum PartGroup : byte { Base = 0, Bar = 1, Collar = 2, UpperCuff = 3, WristCuff = 4, UpperMount = 5, WristMount = 6, AnkleBar = 7, AnkleCuff = 8 }

    /// <summary>One group of the model (model coordinates, see <see cref="HogtieModel"/>).</summary>
    public sealed class ModelPart
    {
        public PartGroup Group;
        public int Side;               // 0 = model x < 0 (her left), 1 = x > 0 (her right), 2 = middle
        public Vector3[] Positions;
        public Vector3[] Normals;
        public Vector3[] KeyDeltas;    // the cuff's size key at 1 (null for parts without one): position = basis + v * delta
        public int[] Indices;
    }

    /// <summary>
    /// A cuff ring. Its size key scales it about <see cref="Anchor"/> (where it is held): centre(v) = anchor + (centre - anchor)
    /// * (1 + v), inner radius(v) = radius * (1 + v).
    /// </summary>
    public struct Cuff
    {
        public Vector3 Center, Anchor, Axis;
        public float InnerRadius, Width;

        /// <summary>The key value that gives the ring an inner radius of <paramref name="radius"/> (model units).</summary>
        public float KeyFor(float radius) => radius / InnerRadius - 1f;

        public Vector3 CenterAt(float key) => Anchor + (Center - Anchor) * (1f + key);
    }

    /// <summary>
    /// The hogtie device exported by tools/export_model.py, in model coordinates (Blender axes, metres): x = sideways (0 = middle),
    /// y = along the device (the collar at -y, the post at +y), z = up from the floor. She lies along it face down, head at -y.
    /// </summary>
    public sealed class HogtieModel
    {
        public const uint Magic = 0x44544748; // "HGTD"
        public Cuff Collar;
        public Cuff[] Upper = new Cuff[2], Wrist = new Cuff[2], Ankle = new Cuff[2];
        public float BarZ, BarRadius, BarFrontY, BarBackY;  // the bar's straight part
        public float PostY, PostRadius, PostTop;
        public float AnkleBarZ;
        public float PlateHalfWidth, PlateFrontY, PlateBackY;
        public List<ModelPart> Parts = new List<ModelPart>();

        public static HogtieModel Read(Stream stream)
        {
            using var r = new BinaryReader(stream);
            if (r.ReadUInt32() != Magic) throw new InvalidDataException("not a hogtie model (magic)");
            uint version = r.ReadUInt32();
            if (version != 1) throw new InvalidDataException("unsupported model version " + version);
            var m = new HogtieModel { Collar = C(r) };
            for (int s = 0; s < 2; s++) m.Upper[s] = C(r);
            for (int s = 0; s < 2; s++) m.Wrist[s] = C(r);
            for (int s = 0; s < 2; s++) m.Ankle[s] = C(r);
            m.BarZ = r.ReadSingle(); m.BarRadius = r.ReadSingle(); m.BarFrontY = r.ReadSingle(); m.BarBackY = r.ReadSingle();
            m.PostY = r.ReadSingle(); m.PostRadius = r.ReadSingle(); m.PostTop = r.ReadSingle();
            m.AnkleBarZ = r.ReadSingle();
            m.PlateHalfWidth = r.ReadSingle(); m.PlateFrontY = r.ReadSingle(); m.PlateBackY = r.ReadSingle();
            uint count = r.ReadUInt32();
            for (int i = 0; i < count; i++)
            {
                var p = new ModelPart { Group = (PartGroup)r.ReadByte(), Side = r.ReadByte() };
                bool key = r.ReadByte() == 1;
                if ((byte)p.Group > 8 || p.Side > 2) throw new InvalidDataException("bad part header");
                int n = checked((int)r.ReadUInt32());
                p.Positions = new Vector3[n];
                p.Normals = new Vector3[n];
                for (int k = 0; k < n; k++) p.Positions[k] = V(r);
                for (int k = 0; k < n; k++) p.Normals[k] = V(r);
                if (key)
                {
                    p.KeyDeltas = new Vector3[n];
                    for (int k = 0; k < n; k++) p.KeyDeltas[k] = V(r);
                }
                int ni = checked((int)r.ReadUInt32());
                if (ni % 3 != 0) throw new InvalidDataException("index count is not a multiple of 3");
                p.Indices = new int[ni];
                for (int k = 0; k < ni; k++)
                {
                    uint idx = r.ReadUInt32();
                    if (idx >= n) throw new InvalidDataException("index out of range");
                    p.Indices[k] = (int)idx;
                }
                m.Parts.Add(p);
            }
            return m;
        }

        private static Cuff C(BinaryReader r) => new Cuff { Center = V(r), Anchor = V(r), Axis = V(r), InnerRadius = r.ReadSingle(), Width = r.ReadSingle() };

        private static Vector3 V(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        /// <summary>The cuff a sized part belongs to (null for parts without a size key).</summary>
        public Cuff? CuffOf(ModelPart p) => p.Group switch
        {
            PartGroup.Collar => Collar,
            PartGroup.UpperCuff => Upper[p.Side],
            PartGroup.WristCuff => Wrist[p.Side],
            PartGroup.AnkleCuff => Ankle[p.Side],
            _ => null,
        };

        /// <summary>The highest point of the model (m, unscaled).</summary>
        public float Top()
        {
            float top = 0f;
            foreach (var p in Parts)
                foreach (var v in p.Positions) top = Math.Max(top, v.Z);
            return top;
        }
    }

    /// <summary>
    /// Device coordinates -> world (any y-up frame, e.g. Unity's numbers): x along <see cref="Side"/>, y along <see cref="Back"/>
    /// (from her head towards her feet), z along <see cref="Up"/>, from <see cref="Origin"/> (the model origin on the floor).
    /// Side = Back x Up, so the basis has determinant +1 and triangle winding is kept; with Unity's axes Side is her right.
    /// Positions are not scaled here: the layout is in metres already (model * scale).
    /// </summary>
    public readonly struct DeviceFrame
    {
        public readonly Vector3 Origin, Side, Back, Up;

        public DeviceFrame(Vector3 origin, Vector3 back, Vector3 up)
        {
            Up = Vector3.Normalize(up);
            Back = Vector3.Normalize(back - Up * Vector3.Dot(back, Up)); // level
            Side = Vector3.Cross(Back, Up);
            Origin = origin;
        }

        public Vector3 Point(Vector3 device) => Origin + Direction(device);

        public Vector3 Direction(Vector3 device) => Side * device.X + Back * device.Y + Up * device.Z;

        public Vector3 ToDevice(Vector3 world)
        {
            Vector3 d = world - Origin;
            return new Vector3(Vector3.Dot(d, Side), Vector3.Dot(d, Back), Vector3.Dot(d, Up));
        }

        /// <summary>The rotation that turns device axes into world axes (for a transform that carries the meshes).</summary>
        public Quaternion Rotation()
        {
            // System.Numerics uses row vectors: row i is the image of device axis i.
            var m = new Matrix4x4(Side.X, Side.Y, Side.Z, 0, Back.X, Back.Y, Back.Z, 0, Up.X, Up.Y, Up.Z, 0, 0, 0, 0, 1);
            return Quaternion.CreateFromRotationMatrix(m);
        }
    }
}
