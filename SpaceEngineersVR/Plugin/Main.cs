﻿using HarmonyLib;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.GUI;
using SpaceEngineersVR.Player.Components;
using SpaceEngineersVR.Wrappers;
using System;
using System.IO;
using System.Reflection;
using Valve.VR;
using VRage.FileSystem;
using VRage.Plugins;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.Plugin
{
	// ReSharper disable once UnusedType.Global
	public class Main : IPlugin
	{
		public static Harmony Harmony { get; private set; }
		public static Config.Config Config { get; private set; }

		public static readonly string Name = "SpaceEngineersVR";
		public static readonly string PublicName = "Space Engineers VR";
		public static readonly string ShortName = "SEVR";

		public static readonly Version Version = typeof(Main).Assembly.GetName().Version;

		private static bool Failed;

		private static Vector2I DesktopResolution;

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		public void Init(object gameInstance)
		{
			MyLog.Default.WriteLine("SpaceEngineersVR: starting...");
			string configPath = Path.Combine(MyFileSystem.UserDataPath, Assets.ConfigFileName);
			Config = SpaceEngineersVR.Config.Config.Load(configPath);

			try
			{
				if (!Initialize())
				{
					Failed = true;
				}
			}
			catch (Exception ex)
			{
				MyLog.Default.WriteLine("SpaceEngineersVR: Failed to start!");
				MyLog.Default.WriteLine(ex.Message);
				MyLog.Default.WriteLine(ex.StackTrace);
				return;
			}
		}

		public void Dispose()
		{
			Logger.Debug("Main#Dispose() called.\t Failed: "+Failed);
			if (!Failed)
			{
				try
				{
					OpenVR.System?.AcknowledgeQuit_Exiting();
					Logger.Info("Exiting OpenVR and closing threads");
				}
				catch (Exception ex)
				{
					Logger.Critical(ex, "Dispose failed");
				}
			}
			Logger.Debug("Main#Dispose() end.");
		}

		public void LoadAssets(string folder)
		{
			Assets.SetFolder(folder);
		}

		public void Update()
		{
			if (!Failed)
			{
				try
				{
					CustomUpdate();
				}
				catch (Exception ex)
				{
					Logger.Critical(ex, "Update failed");
					Failed = true;
				}
			}
		}

		private bool Initialize()
		{
			if (!OpenVR.IsRuntimeInstalled())
			{
				MyLog.Default.WriteLine("SpaceEngineersVR: OpenVR not found!");
				return false;
			}

			if (!OpenVR.IsHmdPresent())
			{
				MyLog.Default.WriteLine("SpaceEngineersVR: No VR headset found, please plug one in and reboot the game to play");
				return false;
			}

			Logger.Info("Starting Steam OpenVR");
			EVRInitError error = EVRInitError.None;
			OpenVR.Init(ref error, EVRApplicationType.VRApplication_Scene);
			if (error != EVRInitError.None)
			{
				Logger.Error($"Booting error = {error}");
				Logger.Critical("Failed to connect to SteamVR!");
				return false;
			}

			/*
			Logger.Info("Starting enviroment");
			MyPerGameSettings.GameIcon = Common.IconIcoPath;
			MyPerGameSettings.BasicGameInfo.GameName = Common.PublicName;
			MyPerGameSettings.BasicGameInfo.ApplicationName = Common.PublicName;
			MyPerGameSettings.BasicGameInfo.SplashScreenImage = Common.IconPngPath;
			MyPerGameSettings.BasicGameInfo.GameAcronym = Common.ShortName;
			*/

			Logger.Info("Patching game.");
			Harmony = new Harmony(Name);
			Harmony.PatchAll(Assembly.GetExecutingAssembly());

			Util.InitialiseOnStartAttribute.FindAndInitialise();

			MySession.AfterLoading += AfterLoadedWorld;
			MySession.OnUnloading += UnloadingWorld;
			MySession.OnUnloaded += UnloadedWorld;

			DesktopResolution = MyRender11.Resolution;

			Logger.Info("Finished main initialization.");
			return true;
		}

		private void CustomUpdate()
		{
			Player.Player.MainUpdate();

			if (MySession.Static?.LocalCharacter != null &&
				!MySession.Static.LocalCharacter.Components.Contains(typeof(VRMovementComponent)))
			{
				MySession.Static.LocalCharacter.Components.Add(new VRMovementComponent());
				MySession.Static.LocalCharacter.Components.Add(new VRBodyComponent());
			}
		}

		// ReSharper disable once UnusedMember.Global
		public void OpenConfigDialog()
		{
			MyGuiSandbox.AddScreen(new ConfigDialog());
		}

		public void AfterLoadedWorld()
		{
			Logger.Info("Loading SE game");
			Player.Player.Headset.CreatePopup("Loaded Game");
		}

		public void UnloadingWorld()
		{
			//MyRender11.Resolution = DesktopResolution;
			Logger.Info("Unloading SE game");
			Player.Player.Headset.CreatePopup("Unloaded Game");
		}

		public void UnloadedWorld()
		{
			Logger.Debug("Main#UnloadedWorld start.");
			
/*			OpenVR.Shutdown();
			EVRInitError error = EVRInitError.None;
			OpenVR.Init(ref error, EVRApplicationType.VRApplication_Scene);
			if (error != EVRInitError.None)
			{
				Logger.Error($"Booting error = {error}");
			}
			Harmony = new Harmony(Name);
			Harmony.PatchAll(Assembly.GetExecutingAssembly());*/
			Logger.Debug("Main#UnloadedWorld end.");
		}
	}
}
