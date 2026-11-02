using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using RestraintHub;
using Hogtie.Logic;

namespace Hogtie
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Hub.Guid, "^1.1.0")] // OnLoad / OnNewGame; PutOn / CanPutOn / TakeOff (missions)
    public sealed class Plugin : BasePlugin
    {
        public const string PluginGuid = "FlanderDev.SFM.Hogtie";
        public const string PluginName = "Hogtie";
        public const string PluginVersion = "0.2.0";
        private const string ModelResource = "Hogtie.assets.hogtie.htm";

        internal static ManualLogSource Logger;
        /// <summary>The hogtie's registration with RestraintHub (ring entry, selection, F).</summary>
        internal static Restraint Entry;
        public static ConfigEntry<float> ClearanceCm, AnkleOnShin, ArchDegrees, MetalShade, AttemptSeconds, SuccessChance, CameraHeight;
        public static ConfigEntry<int> DebugDisable;
        public static ConfigEntry<string> RingLabel;

        public override void Load()
        {
            Logger = Log;
            RingLabel = Config.Bind("General", "RingLabel", "Hogtie", "Name of the ring menu entry.");
            AttemptSeconds = Config.Bind("Unlock", "AttemptSeconds", 4.5f, new ConfigDescription(
                "Seconds of holding the interact key (F) per attempt to struggle free.", new AcceptableValueRange<float>(0.5f, 30f)));
            SuccessChance = Config.Bind("Unlock", "SuccessChance", 1f, new ConfigDescription(
                "Chance that an attempt frees her (1 = the first attempt always does; failures raise it like the game's handcuffs).",
                new AcceptableValueRange<float>(0.01f, 1f)));
            ClearanceCm = Config.Bind("Model", "ClearanceCm", 0.4f, new ConfigDescription(
                "Room between her limbs and the collar / cuffs (cm); the limbs are estimated from her bone lengths. Raise it if they " +
                "show through, lower it (down to -1) for a tighter fit. Applies the next time she is hogtied.", new AcceptableValueRange<float>(-1f, 5f)));
            AnkleOnShin = Config.Bind("Pose", "AnkleOnShin", 0.08f, new ConfigDescription(
                "Where the ankle cuffs sit, in shin lengths above the ankle joint.", new AcceptableValueRange<float>(0.02f, 0.4f)));
            ArchDegrees = Config.Bind("Pose", "ArchDegrees", 35f, new ConfigDescription(
                "How far her back arches (degrees between lower back and chest; 0 = straight). Applies the next time she is hogtied.",
                new AcceptableValueRange<float>(0f, 70f)));
            CameraHeight = Config.Bind("View", "CameraHeight", 0.2f, new ConfigDescription(
                "While she is hogtied the camera circles a point this far (m) above her back (standing: about 1.2 m up).",
                new AcceptableValueRange<float>(0f, 1.5f)));
            MetalShade = Config.Bind("Model", "MetalShade", 0.15f, new ConfigDescription(
                "Brightness of the device (0 = black, 1 = white).", new AcceptableValueRange<float>(0f, 1f)));
            DebugDisable = Config.Bind("Debug", "Disable", 0, "Bit mask for tests: 1 = do not pose the body; 2 = aim with the LateUpdate bone lengths (not the drawn ones); 8 = mark the skin points the cuff fit used (right upper arm and wrist) and hide the device.");

            try
            {
                HogtieController.Model = LoadModel();
            }
            catch (Exception e)
            {
                Logger.LogError("Hogtie model could not be loaded, the mod is disabled: " + e.Message);
                return;
            }
            ClassInjector.RegisterTypeInIl2Cpp<HogtieController>();
            AddComponent<HogtieController>();
            Entry = new Restraint
            {
                Id = "Hogtie",
                RingValue = HogtieController.RingItemValue,
                Label = () => RingLabel.Value,
                ShortcutSaveKey = "Hogtie.Shortcuts",
                OnLoad = _ => HogtieController.Instance?.OnSaveLoaded(),
                OnNewGame = () => HogtieController.Instance?.OnSaveLoaded(),
                Holds = BodyParts.Neck | BodyParts.Arms | BodyParts.Legs | BodyParts.Spot,
                IsWorn = () => HogtieController.Instance != null && HogtieController.Instance.Machine.Worn,
                // Hogtied: no walking, running, crouching, peeing, pose skills or anything that needs her hands.
                Blocks = () => RestraintHub.Blocks.Moving | RestraintHub.Blocks.Running | RestraintHub.Blocks.Crouching | RestraintHub.Blocks.Pee
                               | RestraintHub.Blocks.HandActions | RestraintHub.Blocks.PoseSkills,
                OnChosen = () => HogtieController.Instance?.OnRingChosen(),
                PutOn = () => HogtieController.Instance == null ? "not ready" : HogtieController.Instance.PutOn(),
                CanPutOn = HogtieController.Check,
                TakeOff = () => HogtieController.Release(),
                Attempt = () => HogtieController.Instance == null ? 0f : HogtieController.Instance.Machine.Attempt,
            };
            Hub.Register(Entry);
            Logger.LogInfo("[SFM-ACCEPT] load: " + PluginName + " " + PluginVersion + " ready (" + HogtieController.Model.Parts.Count + " parts)");
        }

        private static HogtieModel LoadModel()
        {
            using Stream s = typeof(Plugin).Assembly.GetManifestResourceStream(ModelResource)
                             ?? throw new FileNotFoundException("embedded resource " + ModelResource + " missing");
            return HogtieModel.Read(s);
        }
    }
}
