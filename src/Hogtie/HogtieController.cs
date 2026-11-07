using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Scripts.InGame;
using ExposureUnnoticed2.Scripts.Sound;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using SfmKit.Game;
using SfmKit.Pure;
using UnityEngine;
using Hogtie.Logic;
using RestraintHub;

namespace Hogtie
{
    /// <summary>
    /// Owns the device in the scene. Bridge diagnostics (session with --allow-invoke):
    /// <c>game call Hogtie.HogtieController LockHere</c> / <c>LockForced</c> / <c>Release</c> / <c>Bones</c> /
    /// <c>View side along up</c> / <c>ViewOff</c>;
    /// <c>game get Hogtie.HogtieController::Status</c>; details in the BepInEx log.
    /// </summary>
    public sealed class HogtieController : MonoBehaviour
    {
        internal static HogtieModel Model;
        internal static HogtieController Instance;
        private HogtieRig _rig;
        private readonly HogtieFitter _fitter = new HogtieFitter();
        private int _lockFrame;
        private HogtiePlacer.Spot _spot;

        public HogtieController(IntPtr pointer) : base(pointer) { }

        /// <summary>Diagnostics: the last lock's fit and, while locked, how far the drawn joints are from their targets.</summary>
        public static string Status = "";
        private Camera.CameraCallback _preCull;
        private int _drawnFrame = -1;
        private bool _viewing;
        private System.Numerics.Vector3 _viewFrom;
        private GameObject _viewLight;

        // Lock state. The panel, the device's "F Hogtie", F routing and the input blocking are RestraintHub's (Plugin.Entry,
        // Plugin.Device).
        internal readonly LockMachine Machine = new LockMachine();
        private static int _failures;

        internal static void Fail(string where, Exception e)
        {
            if (_failures++ < 20) Plugin.Logger.LogWarning(where + " failed: " + e);
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Update()
        {
            try { Tick(); }
            catch (Exception e) { Fail("Update", e); }
        }

        [HideFromIl2Cpp]
        private void Tick()
        {
            if (PlayerController.Instance == null || GameState.GameStateData == null) return;
            if (_locked) HoldCamera();
            Machine.AttemptSeconds = Plugin.AttemptSeconds.Value; // live: config edits apply without a restart
            Machine.BaseChance = Plugin.SuccessChance.Value;
            // F counts only while the hogtie is the selected restraint, outside menus and interaction areas (the hub decides).
            bool unlockAllowed = Plugin.Entry.StruggleHeld && Machine.State == LockState.Locked;
            switch (Machine.Tick(Time.deltaTime, unlockAllowed, UnityEngine.Random.value))
            {
                case LockMachine.Event.Locked:
                    GameUi.PlaySe(InGameSoundManager.SeType.AttachHandcuffs);
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] lock: hogtied");
                    break;
                case LockMachine.Event.AttemptFailed:
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] attempt: failed, chance now " + Machine.Chance.ToString("0.00", CultureInfo.InvariantCulture));
                    break;
                case LockMachine.Event.Unlocking:
                    GameUi.PlaySe(InGameSoundManager.SeType.Unlock);
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] attempt: success after " + Machine.Failures + " failures");
                    break;
                case LockMachine.Event.Unlocked:
                    Release();
                    break;
            }
        }

        /// <summary>Hogtie her: in the placed device whose "F Hogtie" was pressed (RestraintHub sets Device.UsedAt), else where she
        /// stands (a mission through RestraintHub). Null when it is closing, else why not.</summary>
        [HideFromIl2Cpp]
        internal string PutOn()
        {
            var at = Plugin.Device.UsedAt;
            string result = at != null ? Lock(false, false, at) : Lock(false);
            if (!_locked) return Reason(result);
            GameUi.PlaySe(InGameSoundManager.SeType.CommonEquip);
            return null;
        }

        /// <summary>Whether <see cref="PutOn"/> would find room here (RestraintHub's CanPutOn): null = yes, else why not.</summary>
        internal static string Check()
        {
            string result = Lock(false, true);
            return result == "ok" ? null : Reason(result);
        }

        private static string Reason(string result) => result.StartsWith("refused: ") ? result.Substring(9).Split(" (")[0] : result;

        /// <summary>A save was loaded or a new game started: she starts free (being hogtied is not saved).</summary>
        [HideFromIl2Cpp]
        internal void OnSaveLoaded()
        {
            if (Machine.Worn || _locked) Release();
        }

        private void Start()
        {
            _preCull = DelegateSupport.ConvertDelegate<Camera.CameraCallback>(new Action<Camera>(OnPreCull));
            Camera.onPreCull = Camera.onPreCull == null
                ? _preCull
                : Il2CppSystem.Delegate.Combine(Camera.onPreCull, _preCull).Cast<Camera.CameraCallback>();
        }

        [HideFromIl2Cpp]
        private void OnPreCull(Camera cam)
        {
            try
            {
                if (!_locked) return;
                if (_viewing && cam == Camera.main)
                {
                    // Diagnostics view: the game camera moved to a point in device coordinates, aimed at her hips (changing the
                    // transform in onPreCull still takes effect for this render).
                    Vector3 from = HogtiePlacer.U(_spot.Frame.Point(_viewFrom));
                    Vector3 at = HogtiePlacer.U(_spot.Frame.Point(_layout.Hips + (_layout.Neck - _layout.Hips) * 0.3f));
                    cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from, Vector3.up));
                    if (_viewLight != null) _viewLight.transform.position = from;
                }
                if (_drawnFrame == Time.frameCount) return;
                _drawnFrame = Time.frameCount;
                var anim = PlayerController.Instance?.Pca?.Animator;
                if (anim == null || (Plugin.DebugDisable.Value & 1) != 0) return;
                HogtiePoser.RecordDrawn(anim);
                // Cuffs to her skin: once the pose has settled (drawn lengths known), and again twice (a cuff only grows).
                int since = Time.frameCount - _lockFrame;
                if (since == 40 || since == 120 || since == 300)
                {
                    if (_fitter.Fit(anim, _spot.Frame, Model, _layout, _body, _params.Clearance)) _rig.Place(_spot.Frame, _layout);
                    if ((Plugin.DebugDisable.Value & 8) != 0) _rig.SetVisible(false); // diagnostics: the skin markers alone
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] fit: " + _fitter.Status);
                }
                if (Time.frameCount % 60 == 0) Status = _fit + "; " + HogtiePoser.Misses(anim, _spot.Frame, _layout) + "; " + _fitter.Status;
            }
            catch (Exception e) { Fail("onPreCull", e); }
        }

        /// <summary>Diagnostics: look at her from (sideways = her right, along = towards her feet, up) in device metres, with a lamp.</summary>
        public static string View(float side, float along, float up)
        {
            var c = Instance;
            if (c == null || !c._locked) return "not hogtied";
            c._viewFrom = new System.Numerics.Vector3(side, along, up);
            c._viewing = true;
            if (c._viewLight == null)
            {
                c._viewLight = new GameObject("Hogtie.ViewLight");
                DontDestroyOnLoad(c._viewLight);
                var l = c._viewLight.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 6f;
                l.intensity = 1.5f;
            }
            c._viewLight.SetActive(true);
            return "viewing from " + side + ", " + along + ", " + up;
        }

        public static string ViewOff()
        {
            var c = Instance;
            if (c == null) return "no controller";
            c._viewing = false;
            if (c._viewLight != null) c._viewLight.SetActive(false);
            return "view off";
        }

        private void OnDestroy()
        {
            if (_viewLight != null) Destroy(_viewLight);
            if (_preCull != null && Camera.onPreCull != null)
                Camera.onPreCull = Il2CppSystem.Delegate.Remove(Camera.onPreCull, _preCull)?.Cast<Camera.CameraCallback>();
            _rig?.Destroy();
            foreach (var rig in _placed.Values) rig.Destroy();
            _placed.Clear();
            _fitter.Destroy();
            if (Instance == this) Instance = null;
        }

        // The lock: her body measured, the device fitted and placed; the pose held every LateUpdate until released.
        private bool _locked;
        private HogtieLayout _layout;
        private BodyDims _body;
        private PoseParams _params;
        private string _fit = "";
        private Vector3 _rootAt;
        private Quaternion _rootRot;
        // The third-person camera follows the player's CameraTarget (under her body object, ~1.2 m up when she stands): while
        // she is hogtied it is held just above her back, and put back on release.
        private Transform _camTarget;
        private Vector3 _camTargetLocal;
        private IntPtr _lockedPlayer;

        /// <summary>Hogtie her where she stands (diagnostics; players place the device from the hub's panel).</summary>
        public static string LockHere() => Lock(false);

        /// <summary>Diagnostics: lock even where <see cref="LockHere"/> would refuse the spot (the reason is still reported).</summary>
        public static string LockForced() => Lock(true);

        /// <summary>Hogtie her where she stands, or in the placed device <paramref name="at"/>; <paramref name="check"/>: only find
        /// out whether it would work ("ok").</summary>
        [HideFromIl2Cpp]
        private static string Lock(bool force, bool check = false, Placed at = null)
        {
            try
            {
                var c = Instance;
                var player = PlayerController.Instance;
                if (c == null || player == null || player.Pca == null || player.Pca.Animator == null) return "not in game";
                if (c._locked && !check) Release();
                var anim = player.Pca.Animator;
                if (!HogtiePoser.Measure(anim, out BodyDims body, out string problem)) return "refused: " + problem;
                var prm = Params();
                var layout = HogtiePose.Layout(Model, body, prm);
                if (layout.Problem != null) return "refused: " + layout.Problem;
                // A placed device was checked when it was placed: her body goes where it is.
                var spot = at != null ? new HogtiePlacer.Spot { Ok = true, Frame = FrameAt(at.Position, at.Rotation, layout), Floor = "placed device" }
                                      : HogtiePlacer.Find(player, Model, layout, force);
                c._spot = spot;
                c._fit = "scale " + F(layout.Scale) + ", ankle bar " + F(layout.AnkleBarRaise * layout.Scale) + " m, shins lean "
                         + F(layout.ShinLean * Mathf.Rad2Deg) + " deg (ankle cuffs turned " + F(layout.AnkleTilt * Mathf.Rad2Deg) + "), belly "
                         + F(layout.Belly) + " m above the floor, arm cuffs slid " + F(layout.UpperSlide[1] * layout.Scale) + " / " + F(layout.WristSlide[1] * layout.Scale) + " m, hips " + F(layout.Hips.Z) + " m up"
                         + (layout.Costs.Length > 0 ? ", rules off: " + layout.Costs : "");
                string text = (spot.Ok ? (spot.Reason == null ? "ok" : "FORCED, would refuse: " + spot.Reason) : "refused: " + spot.Reason)
                              + " (floor '" + spot.Floor + "'); body spine " + F(body.Spine) + " thigh " + F(body.Thigh) + " shin " + F(body.Shin)
                              + " upper arm " + F(body.UpperArm) + " forearm " + F(body.Forearm) + " neck " + F(body.NeckLength) + "; " + c._fit;
                Plugin.Logger.LogInfo("[SFM-ACCEPT] " + (check ? "check: " : "lock: ") + text);
                Status = text;
                if (!spot.Ok) return text;
                if (check) return "ok";
                if (c._rig == null || !c._rig.Built)
                {
                    c._rig = new HogtieRig(Model);
                    c._rig.Build(anim.gameObject.layer);
                }
                c._layout = layout;
                c._fitter.Reset();
                c._lockFrame = Time.frameCount;
                c._body = body;
                c._params = prm;
                c._rig.Place(spot.Frame, layout);
                c._rig.SetShade(Plugin.MetalShade.Value);
                c._rig.SetVisible(true);
                c._inPlaced = at;
                if (at != null && c._placed.TryGetValue(at, out var empty)) empty.SetVisible(false); // the fitted rig takes its place
                // Her root stands on the floor under her hips, facing where her head is (the camera and the game follow it).
                var hips = spot.Frame.Point(new System.Numerics.Vector3(0f, layout.Hips.Y, 0f));
                c._rootAt = HogtiePlacer.U(hips);
                c._rootRot = Quaternion.LookRotation(-HogtiePlacer.U(spot.Frame.Back), Vector3.up);
                c._lockedPlayer = player.Pointer;
                c._camTarget = player.CameraTarget;
                if (c._camTarget != null) c._camTargetLocal = c._camTarget.localPosition;
                c._locked = true;
                c.Machine.RequestLock();
                return text;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("lock failed: " + e);
                return "failed: " + e.Message;
            }
        }

        public static string Release()
        {
            var c = Instance;
            if (c == null) return "no controller";
            c._locked = false;
            ViewOff();
            if (c._camTarget != null) c._camTarget.localPosition = c._camTargetLocal;
            c._camTarget = null;
            HogtiePoser.ForgetDrawn();
            c.Machine.Reset();
            c._rig?.SetVisible(false);
            if (c._inPlaced != null && c._placed.TryGetValue(c._inPlaced, out var empty)) empty.SetVisible(true);
            c._inPlaced = null;
            Plugin.Logger.LogInfo("[SFM-ACCEPT] release: free");
            return "released";
        }

        /// <summary>The camera's target over the middle of her back (Update and LateUpdate: whichever the camera reads after).</summary>
        [HideFromIl2Cpp]
        private void HoldCamera()
        {
            if (_camTarget == null || _layout == null) return;
            var at = (_layout.Hips + _layout.Neck) / 2f;
            float top = Math.Max(Math.Max(_layout.Hips.Z, _layout.Chest.Z), _layout.Neck.Z);
            _camTarget.position = HogtiePlacer.U(_spot.Frame.Point(new System.Numerics.Vector3(0f, at.Y, top + Plugin.CameraHeight.Value)));
        }

        // --- the device as a placeable (RestraintHub keeps the placed ones, their map and the save) ---------------------------

        /// <summary>How far in front of her feet a placed device's hips point goes (before 2.0 she lay down where she stood).</summary>
        private const float PlaceAhead = 1.1f;
        private readonly Dictionary<Placed, HogtieRig> _placed = new Dictionary<Placed, HogtieRig>();
        private Placed _inPlaced;

        /// <summary>The panel's "Hogtie": the device on the floor in front of her, its head end away from her. Its pose is where
        /// her root will be (the hips' floor point, facing the way her head goes).</summary>
        internal static PlaceSpot FindSpot()
        {
            var player = PlayerController.Instance;
            if (Instance == null || player == null || player.Pca == null || player.Pca.Animator == null) return PlaceSpot.No("not ready");
            if (!LayoutNow(player.Pca.Animator, out var layout, out string problem)) return PlaceSpot.No(problem);
            var spot = HogtiePlacer.Find(player, Model, layout, false, PlaceAhead);
            Plugin.Logger.LogInfo("[SFM-ACCEPT] place: " + (spot.Ok ? "ok" : "refused: " + spot.Reason) + " (floor '" + spot.Floor + "')");
            if (!spot.Ok) return PlaceSpot.No(spot.Reason);
            var hips = spot.Frame.Point(new System.Numerics.Vector3(0f, layout.Hips.Y, 0f));
            return new PlaceSpot { Position = HogtiePlacer.U(hips), Rotation = Quaternion.LookRotation(-HogtiePlacer.U(spot.Frame.Back), Vector3.up) };
        }

        /// <summary>A placed device shown (placed, or its map loaded): the device, sized for her, empty.</summary>
        internal static void ShowPlaced(Placed placed)
        {
            var c = Instance;
            var anim = PlayerController.Instance?.Pca?.Animator;
            if (c == null || anim == null) return;
            if (!LayoutNow(anim, out var layout, out string problem))
            {
                Plugin.Logger.LogWarning("placed hogtie not shown: " + problem);
                return;
            }
            var rig = new HogtieRig(Model);
            rig.Build(anim.gameObject.layer, placed.Root);
            rig.Place(FrameAt(placed.Position, placed.Rotation, layout), layout);
            rig.SetShade(Plugin.MetalShade.Value);
            rig.SetVisible(true);
            c._placed[placed] = rig;
        }

        internal static void HidePlaced(Placed placed)
        {
            var c = Instance;
            if (c == null || !c._placed.TryGetValue(placed, out var rig)) return;
            rig.Destroy();
            c._placed.Remove(placed);
        }

        /// <summary>The device frame for a root pose (hips' floor point, facing the way her head goes) and her layout.</summary>
        private static DeviceFrame FrameAt(Vector3 rootAt, Quaternion rootRot, HogtieLayout layout)
        {
            var back = System.Numerics.Vector3.Normalize(HogtiePlacer.N(-(rootRot * Vector3.forward)));
            var origin = HogtiePlacer.N(rootAt) - back * layout.Hips.Y;
            return new DeviceFrame(origin, back, System.Numerics.Vector3.UnitY);
        }

        [HideFromIl2Cpp]
        private static bool LayoutNow(Animator anim, out HogtieLayout layout, out string problem)
        {
            layout = null;
            if (!HogtiePoser.Measure(anim, out BodyDims body, out problem)) return false;
            layout = HogtiePose.Layout(Model, body, Params());
            problem = layout.Problem;
            return problem == null;
        }

        [HideFromIl2Cpp]
        private static PoseParams Params()
        {
            var p = PoseParams.Default;
            p.Clearance = Plugin.ClearanceCm.Value / 100f;
            p.AnkleOnShin = Plugin.AnkleOnShin.Value;
            p.ArchAngle = Plugin.ArchDegrees.Value * Mathf.Deg2Rad;
            return p;
        }

        private void LateUpdate()
        {
            if (!_locked) return;
            try
            {
                var player = PlayerController.Instance;
                if (player == null || player.Pointer != _lockedPlayer || player.Pca == null || player.Pca.Animator == null)
                {
                    Release(); // map change or the player was replaced
                    return;
                }
                player.transform.SetPositionAndRotation(_rootAt, _rootRot);
                HoldCamera();
                if ((Plugin.DebugDisable.Value & 1) == 0)
                {
                    HogtiePoser.UseDrawnLengths(player.Pca.Animator, (Plugin.DebugDisable.Value & 2) == 0);
                    var anim = player.Pca.Animator;
                    HogtiePoser.ApplyBody(anim, _spot.Frame, _layout);
                    for (int side = 0; side < 2; side++)
                        HogtiePose.FitArm(Model, _body, _params, _layout, side, HogtiePoser.Shoulder(anim, _spot.Frame, side));
                    HogtiePoser.ApplyArms(anim, _spot.Frame, _layout);
                    HogtiePoser.RestoreAnimatedLengths(anim);
                    _rig.Move(_layout);
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("holding the pose failed, released: " + e);
                Release();
            }
        }

        /// <summary>The player's bone positions relative to her feet and facing (x right, y up, z forward), for sizing the pose.</summary>
        public static string Bones()
        {
            var player = PlayerController.Instance;
            var anim = player == null ? null : player.Pca.Animator;
            if (anim == null) return "not in game";
            Transform pt = player.transform;
            var sb = new StringBuilder("bones (x right, y up, z forward; m):");
            foreach (HumanBodyBones b in new[] {
                HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head,
                HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes })
            {
                var t = anim.GetBoneTransform(b);
                if (t == null) { sb.Append('\n').Append(b).Append(": none"); continue; }
                Vector3 p = pt.InverseTransformPoint(t.position);
                sb.Append('\n').Append(b).Append(": ").Append(F(p.x)).Append(", ").Append(F(p.y)).Append(", ").Append(F(p.z));
            }
            Plugin.Logger.LogInfo(sb.ToString());
            return "logged";
        }

        private static string F(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);
    }
}
