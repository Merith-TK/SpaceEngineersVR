﻿using HarmonyLib;
using ParallelTasks;
using SharpDX.Mathematics.Interop;
using SpaceEngineersVR.GUI;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Wrappers;
using System;
using System.Diagnostics;
using System.Text;
using Valve.VR;
using VRage.Profiler;
using VRageMath;
using VRageRender;

namespace SpaceEngineersVR.Patches
{
	[Util.InitialiseOnStart]
	public static class FrameInjections
	{
		public static bool DisablePresent = false;

		static FrameInjections()
		{
			Logger.Info("Starting applying harmony frame injections.");
			Type t = AccessTools.TypeByName("VRageRender.MyRender11");

			Common.Harmony.Patch(AccessTools.Method(t, "Present"), prefix: new HarmonyMethod(typeof(FrameInjections), nameof(Prefix_Present)));

			Common.Harmony.Patch(AccessTools.Method(t, "Draw"), prefix: new HarmonyMethod(typeof(FrameInjections), nameof(Prefix_Draw)));

			Common.Harmony.Patch(AccessTools.Method(t, "DrawScene"), prefix: new HarmonyMethod(typeof(FrameInjections), nameof(Prefix_DrawScene)));


			Common.Harmony.Patch(AccessTools.Method(t, "RenderMainSprites", new Type[0]),
				prefix: new HarmonyMethod(typeof(FrameInjections), nameof(Prefix_RenderMainSprites)),
				postfix: new HarmonyMethod(typeof(FrameInjections), nameof(Postfix_RenderMainSprites)));

			Logger.Info("Applied harmony game injections for renderer.");
		}

		private static bool Prefix_Present()
		{
			return !DisablePresent;
		}

		private static bool Prefix_Draw()
		{
			Player.Player.RenderUpdate();
			StringBuilder builder = new StringBuilder();
			builder.AppendLine("MyRenderer11 state: ");
			builder.AppendLine("\t Backbuffer: " + MyRender11.Backbuffer.ToString());
			builder.AppendLine("\t DebugOverrides: " + MyRender11.DebugOverrides.ToString());
			builder.AppendLine("\t Environment_Matrices: " + MyRender11.Environment_Matrices.ToString());
			builder.AppendLine("\t MainSpritesTask: " + MyRender11.MainSpritesTask.ToString());
			builder.AppendLine("\t m_DrawScene: " + MyRender11.m_DrawScene.ToString());
			builder.AppendLine("\t m_Settings: " + MyRender11.m_Settings.ToString());
			builder.AppendLine("\t RC: " + MyRender11.RC.ToString());
			builder.AppendLine("\t Resolution: " + MyRender11.Resolution.ToString());
			builder.AppendLine("\t Settings: " + MyRender11.Settings.ToString());
			builder.AppendLine("\t ViewportResolution: " + MyRender11.ViewportResolution.ToString());

			Logger.Debug(builder.ToString());
			return true;
		}

		private static bool Prefix_DrawScene()
		{
			Player.Player.Headset.DrawScene();

			return true;
		}

		private static bool Prefix_RenderMainSprites()
		{
			RawColor4 rawColor = new RawColor4(0, 0, 0, 0);
			MyRender11.RC.ClearRtv(MyRender11.Backbuffer, rawColor);

			if (!MyRender11.Settings.OffscreenSpritesRendering || !MyRender11.m_DrawScene)
			{
				MyViewport myViewport = new MyViewport(MyRender11.ViewportResolution.X, MyRender11.ViewportResolution.Y);
				Vector2 size = MyRender11.ViewportResolution;

				object defaultMessages = MyManagers.SpritesManager.AcquireDrawMessages("DefaultOffscreenTarget");
				object debugMessages = MyManagers.SpritesManager.CloseDebugDrawMessages();

				MyRender11.MainSpritesTask = Parallel.Start(delegate {
					MyRender11.RenderMainSpritesWorker(MyRender11.Backbuffer.instance, MyRender11.ScaleMainViewport(myViewport), myViewport, size, defaultMessages, debugMessages, null);

				}, Parallel.DefaultOptions.WithDebugInfo(MyProfiler.TaskType.GUI, "RenderMainSprites"), WorkPriority.VeryHigh);
			}

			return false;
		}

		private static void Postfix_RenderMainSprites()
		{
			MyRender11.ConsumeMainSprites();

			VRGUIManager.Draw();
		}
	}
}
