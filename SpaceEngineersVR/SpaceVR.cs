﻿using HarmonyLib;
using Sandbox;
using Sandbox.Game;
using Sandbox.Game.World;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Utils;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Valve.VR;
using VRage;
using VRage.Plugins;

namespace SpaceEngineersVR
{
    public class SpaceVR : IPlugin
    {

        public static Harmony Harmony { get; private set; }
        public static bool IsValid { get; private set; }
        static Headset Headset;
        Logger log;

        public void Init(object gameInstance)
        {
            
            if (!OpenVR.IsRuntimeInstalled())
            {
                MySandboxGame.Log.WriteLine("SpaceEngineersVR: OpenVR not found!");
                IsValid = false;
                return;
            }

            if (!OpenVR.IsHmdPresent())
            {
                MySandboxGame.Log.WriteLine("SpaceEngineersVR: No VR headset found, please plug one in and reboot the game to play");
                IsValid = false;
                return;
            }

            log = new Logger();
            log.Write("Starting Steam OpenVR");
            var error = EVRInitError.None;
            OpenVR.Init(ref error, EVRApplicationType.VRApplication_Scene);
            log.Write($"Booting error = {error}");

            if (error != EVRInitError.None)
            {
                log.Write("Failed to connect to SteamVR!");
                IsValid = false;
                return;
            }

            log.Write("De-Keenifying enviroment");
            Form GameWindow = (Form)AccessTools.Field(MyVRage.Platform.Windows.GetType(), "m_form").GetValue(MyVRage.Platform.Windows);
            var assetFolder = Util.GetAssetFolder();
            GameWindow.Icon = new Icon( Path.Combine(assetFolder, "icon.ico"));
            GameWindow.Text = "SpaceEngineersVR";
            GameWindow.AccessibleName = "SpaceEngineersVR";

            MyPerGameSettings.GameIcon = Path.Combine(assetFolder, "icon.ico");
            MyPerGameSettings.BasicGameInfo.GameName = "SpaceEngineersVR";
            MyPerGameSettings.BasicGameInfo.ApplicationName = "SpaceEngineersVR";
            MyPerGameSettings.BasicGameInfo.SplashScreenImage = Path.Combine(assetFolder, "logo.png");
            MyPerGameSettings.BasicGameInfo.GameAcronym = "SEVR";

            log.Write("Creating VR environment");
            OpenVR.Input.SetActionManifestPath(Path.Combine(assetFolder, "actions.json"));
            Harmony = new Harmony("SpaceEngineersVR");
            Headset = new Headset();
            Headset.CreatePopup("Booted successfully");

            MySession.AfterLoading += AfterLoadedWorld;
            MySession.OnUnloading += UnLoadingWorld;

            IsValid = true;
            log.Write("Cleaning up...");
        }

        public void Update()
        {
            if (!IsValid || OpenVR.System == null)
                return;

            // log.Write("Game update");
        }

        public void AfterLoadedWorld()
        {
            log.Write("Loading SE game");
            Headset.CreatePopup("Loaded Game");
        }

        public void UnLoadingWorld()
        {
            log.Write("Unloading SE game");
            Headset.CreatePopup("UnLoaded Game");
        }

        public void Dispose()
        {
            if (IsValid)
            {
                IsValid = false;
                OpenVR.System?.AcknowledgeQuit_Exiting();
                log.Write("Exiting OpenVR and closing threads");
            }
        }

    }
}