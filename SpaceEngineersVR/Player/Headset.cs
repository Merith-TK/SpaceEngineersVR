﻿using ParallelTasks;
using Sandbox;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SpaceEngineersVR.Player.Components;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Util;
using SpaceEngineersVR.Wrappers;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Valve.VR;
using VRage;
using VRage.Utils;
using VRageMath;
using VRageRender;

// See MyRadialMenuItemFactory for actions

namespace SpaceEngineersVR.Player
{
	public class Headset : TrackedDevice
	{
		private VRBodyComponent bodyComponent = null;

		private readonly FastResourceLock renderBodyComponentLock = new FastResourceLock();
		private VRBodyComponent renderBodyComponent = null;

		private readonly uint pnX;
		private readonly uint pnY;
		private uint height;
		private uint width;

		public Vector2I rawResolution => new Vector2I((int)width, (int)height);
		public Vector2I scaledResolution => new Vector2I((int)(width * Main.Config.resolutionScale.value), (int)(height * Main.Config.resolutionScale.value));

		private readonly float fovH;
		private readonly float fovV;

		private VRTextureBounds_t imageBounds;

		private bool enableNotifications = true;

		public Headset()
			: base(actionName: "")
		{
			Logger.Debug("Creating Headset object");

			deviceId = OpenVR.k_unTrackedDeviceIndex_Hmd;

			OpenVR.ExtendedDisplay.GetEyeOutputViewport(EVREye.Eye_Left, ref pnX, ref pnY, ref width, ref height);

			float left = 0f, right = 0f, top = 0f, bottom = 0f;
			OpenVR.System.GetProjectionRaw(EVREye.Eye_Left, ref left, ref right, ref top, ref bottom);
			fovH = MathHelper.Atan((right - left) / 2) * 2f;
			fovV = MathHelper.Atan((bottom - top) / 2) * 2f;

			imageBounds = new VRTextureBounds_t
			{
				uMax = 1,
				vMax = 1,
			};

			ETrackedPropertyError error = ETrackedPropertyError.TrackedProp_Success;
			int refreshRate = (int)Math.Ceiling(OpenVR.System.GetFloatTrackedDeviceProperty(0, ETrackedDeviceProperty.Prop_DisplayFrequency_Float, ref error));
			if (refreshRate == 121 || refreshRate == 119) refreshRate = 120;
			if (error != ETrackedPropertyError.TrackedProp_Success)
			{
				Logger.Critical("Failed to get HMD refresh rate! defaulting to 80");
				refreshRate = 80;
			}

			MyRenderDeviceSettings x = MyRender11.m_Settings;
			x.RefreshRate = refreshRate;
			//x.BackBufferHeight = (int)height;
			//x.BackBufferWidth = (int)width;
			x.SettingsMandatory = true;
			MySandboxGame.Static.SwitchSettings(x);

			Logger.Info($"Found headset with eye resolution of '{width}x{height}' and refresh rate of {refreshRate}");
			Logger.Info($"Changing eye resolution to : {MyRender11.Resolution.X}x{MyRender11.Resolution.Y}");
			width = (uint)MyRender11.Resolution.X;
			height = (uint)MyRender11.Resolution.Y;

			MySession.AfterLoading += GameLoaded;
		}

		private void GameLoaded()
		{
			Logger.Debug("Headset#GameLoaded() start");
			FetchObjectCameraIsAttachedTo(MySession.Static.CameraController);
			MySession.Static.CameraAttachedToChanged += (oldCamera, newCamera) =>
			{
				FetchObjectCameraIsAttachedTo(newCamera);
			};

			void FetchObjectCameraIsAttachedTo(VRage.Game.ModAPI.Interfaces.IMyCameraController camera)
			{
				VRage.Game.Components.MyEntityComponentContainer components = (VRage.Game.Components.MyEntityComponentContainer)(camera?.Entity?.Components);
				if (components == null)
					return;

				if (components.TryGet(out VRBodyComponent body))
				{
					SetBodyComponent(body);
				}
			}
			Logger.Debug("Headset#GameLoaded() end");
		}

		#region DrawingLogic

		private bool firstUpdate = true;
		private BorrowedRtvTexture texture;

		public void DrawScene()
		{
			if (firstUpdate && renderPose.isTracked)
			{
				//TODO: Listen to Main.Config.resolutionScale.onValueChanged and update rendering stuff

				//MyRender11.Resolution = new Vector2I(scaledResolution.X, scaledResolution.Y);
				MyRender11.CreateScreenResources();
				firstUpdate = false;
				return;
			}

			texture?.Release();
			texture = MyManagers.RwTexturesPool.BorrowRtv("SpaceEngineersVR", scaledResolution.X, scaledResolution.Y, Format.R8G8B8A8_UNorm_SRgb);

			EnvironmentMatrices envMats = MyRender11.Environment_Matrices;

			Matrix deviceToAbsolute = renderPose.deviceToAbsolute.matrix;
			deviceToAbsolute.M42 -= Player.GetBodyCalibration().height;
			Matrix m = deviceToAbsolute * Player.RenderPlayerToAbsolute.inverted;
			MatrixD viewMatrix = envMats.ViewD * MatrixD.Invert(m);


			//TODO: Redo this frustum culling such that it encompasses both eye's projection matrixes
			//theres a thread on unity forums with the math involved, will have to do some searching to find it again.
			//I think someone posted a link to it in the discord
			BoundingFrustumD viewFrustum = envMats.ViewFrustumClippedD;
			MyUtils.Init(ref viewFrustum);
			viewFrustum.Matrix = viewMatrix * envMats.OriginalProjection;
			envMats.ViewFrustumClippedD = viewFrustum;

			BoundingFrustumD viewFrustumFar = envMats.ViewFrustumClippedFarD;
			MyUtils.Init(ref viewFrustumFar);
			viewFrustumFar.Matrix = viewMatrix * envMats.OriginalProjectionFar;
			envMats.ViewFrustumClippedFarD = viewFrustumFar;


			envMats.FovH = fovH;
			envMats.FovV = fovV;

			LoadEnviromentMatrices(EVREye.Eye_Left, viewMatrix, ref envMats);
			DrawScene(EVREye.Eye_Left);

			LoadEnviromentMatrices(EVREye.Eye_Right, viewMatrix, ref envMats);
			DrawScene(EVREye.Eye_Right);
		}

		private void DrawScene(EVREye eye)
		{
			BorrowedRtvTexture debugAmbientOcclusion;
			MyRender11.DrawGameScene(texture, out debugAmbientOcclusion);
			MyRender11.DrawDebugScene(debugAmbientOcclusion);

			Texture2D texture2D = texture.resource;
			Texture_t input = new Texture_t
			{
				eColorSpace = EColorSpace.Auto,
				eType = ETextureType.DirectX,
				handle = texture2D.NativePointer
			};
			OpenVR.Compositor.Submit(eye, ref input, ref imageBounds, EVRSubmitFlags.Submit_Default);
			
		}

		private void LoadEnviromentMatrices(EVREye eye, MatrixD viewMatrix, ref EnvironmentMatrices envMats)
		{
			Matrix eyeToHead = OpenVR.System.GetEyeToHeadTransform(eye).ToMatrix();
			viewMatrix *= Matrix.Invert(eyeToHead);

			MatrixD worldMat = MatrixD.Invert(viewMatrix);
			Vector3D cameraPosition = worldMat.Translation;

			envMats.CameraPosition = cameraPosition;
			envMats.ViewD = viewMatrix;
			envMats.InvViewD = worldMat;

			MatrixD viewAt0 = viewMatrix;
			viewAt0.M14 = 0.0;
			viewAt0.M24 = 0.0;
			viewAt0.M34 = 0.0;
			viewAt0.M41 = 0.0;
			viewAt0.M42 = 0.0;
			viewAt0.M43 = 0.0;
			viewAt0.M44 = 1.0;
			envMats.ViewAt0 = viewAt0;
			envMats.InvViewAt0 = MatrixD.Invert(viewAt0);

			MatrixD projection = GetPerspectiveFovRhInfiniteComplementary(eye, envMats.NearClipping);
			envMats.Projection = projection;
			envMats.ProjectionForSkybox = projection;
			envMats.InvProjection = MatrixD.Invert(projection);

			envMats.ViewProjectionD = viewMatrix * projection;
			envMats.InvViewProjectionD = MatrixD.Invert(envMats.ViewProjectionD);

			MatrixD viewProjectionAt0 = viewAt0 * projection;
			envMats.ViewProjectionAt0 = viewProjectionAt0;
			envMats.InvViewProjectionAt0 = MatrixD.Invert(viewProjectionAt0);
		}

		private static MatrixD GetPerspectiveFovRhInfiniteComplementary(EVREye eye, double nearPlane)
		{
			float left = 0f, right = 0f, top = 0f, bottom = 0f;
			OpenVR.System.GetProjectionRaw(eye, ref left, ref right, ref top, ref bottom);

			//Adapted from decompilation of Matrix.CreatePerspectiveFovRhInfiniteComplementary, Matrix.CreatePerspectiveFieldOfView
			//and https://github.com/ValveSoftware/openvr/wiki/IVRSystem::GetProjectionRaw

			double idx = 1d / (right - left);
			double idy = 1d / (bottom - top);
			double sx = right + left;
			double sy = bottom + top;

			return new MatrixD(
				2d * idx, 0d, 0d, 0d,
				0d, 2d * idy, 0d, 0d,
				sx * idx, sy * idy, 0d, -1d,
				0d, 0d, nearPlane, 0d);
		}

		#endregion
		#region Control logic

		protected override void OnConnected()
		{
			base.OnConnected();

			if (MySession.Static == null)
				return;

			if (MySession.Static.IsPausable())
			{
				MySandboxGame.PausePop();
				Logger.Info("Headset reconnected, unpausing game.");
			}
			else
			{
				Logger.Info("Headset reconnected, unable to unpause game as game is already unpaused.");
			}
		}
		protected override void OnDisconnected()
		{
			base.OnDisconnected();

			if (MySession.Static == null)
				return;

			//ShowMessageBoxAsync("Your headset got disconnected, please reconnect it to continue gameplay.", "Headset Disconnected");
			if (MySession.Static.IsPausable())
			{
				MySandboxGame.PausePush();
				Logger.Info("Headset disconnected, pausing game.");
			}
			else
			{
				Logger.Info("Headset disconnected, unable to pause game since it is a multiplayer session.");
			}
		}

		protected override void OnStartTracking()
		{
			base.OnStartTracking();
			Player.ResetPlayerFloor();
		}



		public override void MainUpdate()
		{
			VRage.Game.ModAPI.IMyCharacter character = MyAPIGateway.Session?.Player?.Character;
			if (character != null)
			{
				if (character.Visible && !Main.Config.enableCharacterRendering.value)
				{
					character.Visible = false;
				}
				else if (!character.Visible && Main.Config.enableCharacterRendering.value)
				{
					character.Visible = true;
				}
			}
		}

		public void SetBodyComponent(VRBodyComponent bodyComponent)
		{
			this.bodyComponent = bodyComponent;

			using (renderBodyComponentLock.AcquireExclusiveUsing())
			{
				renderBodyComponent = bodyComponent;
			}
		}

		#endregion

		#region Utils

		public void CreatePopup(string message)
		{
			System.Drawing.Bitmap img = new Bitmap(File.OpenRead(Assets.IconPngPath));
			CreatePopup(EVRNotificationType.Transient, message, ref img);
		}

		public void CreatePopup(EVRNotificationType type, string message, ref System.Drawing.Bitmap bitmap)
		{
			Logger.Debug($"Trying to create pop-up. Message: {message} \t enableNotifications: {enableNotifications}");
			if (!enableNotifications)
				return;

			ulong handle = 0;
			OpenVR.Overlay.CreateOverlay(Guid.NewGuid().ToString(), "SpaceEngineersVR", ref handle);

			System.Drawing.Imaging.BitmapData textureData =
				bitmap.LockBits(
					new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
					System.Drawing.Imaging.ImageLockMode.ReadOnly,
					System.Drawing.Imaging.PixelFormat.Format32bppArgb
				);

			NotificationBitmap_t image = new NotificationBitmap_t()
			{
				m_pImageData = textureData.Scan0,
				m_nWidth = textureData.Width,
				m_nHeight = textureData.Height,
				m_nBytesPerPixel = 4
			};
			// FIXME: Notification on overlay
			uint id = 0;
			OpenVR.Notifications.CreateNotification(handle, 0, type, message, EVRNotificationStyle.Application, ref image, ref id);

			bitmap.UnlockBits(textureData);
			Logger.Debug("Pop-up created with message: " + message);
		}

		/// <summary>
		/// Shows a messagebox async to prevent calling thread from being paused.
		/// </summary>
		/// <param name="msg">The message of the messagebox.</param>
		/// <param name="caption">The caption of the messagebox.</param>
		/// <returns>The button that the user clicked as System.Windows.Forms.DialogResult.</returns>
		public DialogResult ShowMessageBoxAsync(string msg, string caption)
		{
			Parallel.Start(() =>
			{
				Logger.Info($"Messagebox created with the message: {msg}");
				DialogResult result = MessageBox.Show(msg, caption, MessageBoxButtons.OKCancel);
				return result;
			});
			return DialogResult.None;
		}

		#endregion
	}
}
