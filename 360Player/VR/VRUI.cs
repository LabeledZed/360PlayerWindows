using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System;
using Device = SharpDX.Direct3D11.Device;
using DX2D = SharpDX.Direct2D1;
using Bivrost.Bivrost360Player.Tools;
using Bivrost.Bivrost360Player.Streaming;

namespace Bivrost.Bivrost360Player
{
	public class VRUI : IDisposable
	{
		Texture2D uiTexture;
		SharpDX.Toolkit.Graphics.Texture2D uiToolkitTexture;
		public SharpDX.Toolkit.Graphics.BasicEffect uiEffect;

		DX2D.RenderTarget target2d;
		SharpDX.Toolkit.Graphics.GeometricPrimitive uiPrimitive;

		SharpDX.DirectWrite.TextFormat textFormat;
		SharpDX.DirectWrite.TextFormat textFormatSmall;
		SharpDX.DirectWrite.TextFormat textFormatList;
		DX2D.SolidColorBrush textBrush;
		DX2D.SolidColorBrush dimTextBrush;
		DX2D.SolidColorBrush blueBrush;
		DX2D.SolidColorBrush highlightBrush;
		DX2D.SolidColorBrush starBrush;
		DX2D.SolidColorBrush hudBgBrush;

		bool uiInitialized = false;
		bool redraw = false;
		bool recenterRequested = true;
		bool lastDrawnSeekBarOnly = false;
		bool lastDrawnVolumeBarOnly = false;
		bool lastDrawnStatusHudOnly = false;

		const float uiDistanceStart = 1.5f;
		const float uiDistanceFade = 0.5f;
		const float uiDistanceDisappear = 0.25f;

		public VRUI(Device device, SharpDX.Toolkit.Graphics.GraphicsDevice gd)
		{
			Texture2DDescription uiTextureDescription = new Texture2DDescription()
			{
				Width = 1024,
				Height = 512,
				MipLevels = 1,
				ArraySize = 1,
				Format = Format.B8G8R8A8_UNorm,
				Usage = ResourceUsage.Default,
				SampleDescription = new SampleDescription(1, 0),
				BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
				CpuAccessFlags = CpuAccessFlags.None,
				OptionFlags = ResourceOptionFlags.Shared
			};

			uiTexture = new SharpDX.Direct3D11.Texture2D(device, uiTextureDescription);

			using (DX2D.Factory factory2d = new DX2D.Factory(SharpDX.Direct2D1.FactoryType.SingleThreaded, DX2D.DebugLevel.Information))
			{
				DX2D.RenderTargetProperties renderTargetProperties = new DX2D.RenderTargetProperties()
				{
					DpiX = 96,
					DpiY = 96,
					PixelFormat = new DX2D.PixelFormat(Format.B8G8R8A8_UNorm, DX2D.AlphaMode.Premultiplied),
					Type = DX2D.RenderTargetType.Hardware,
					MinLevel = DX2D.FeatureLevel.Level_10,
					Usage = DX2D.RenderTargetUsage.None
				};
				using (var uiSurface = uiTexture.QueryInterface<Surface>())
					target2d = new DX2D.RenderTarget(factory2d, uiSurface, renderTargetProperties)
					{
						AntialiasMode = DX2D.AntialiasMode.PerPrimitive
					};
			}


			// 2D materials
			uiToolkitTexture = SharpDX.Toolkit.Graphics.Texture2D.New(gd, uiTexture);
			uiEffect = new SharpDX.Toolkit.Graphics.BasicEffect(gd)
			{
				PreferPerPixelLighting = false,
				Texture = uiToolkitTexture,
				TextureEnabled = true,
				LightingEnabled = false
			};


			BlendStateDescription blendStateDescription = new BlendStateDescription()
			{
				AlphaToCoverageEnable = false
			};
			blendStateDescription.RenderTarget[0].IsBlendEnabled = true;
			blendStateDescription.RenderTarget[0].SourceBlend = BlendOption.SourceAlpha;
			blendStateDescription.RenderTarget[0].DestinationBlend = BlendOption.InverseSourceAlpha;
			blendStateDescription.RenderTarget[0].BlendOperation = BlendOperation.Add;
			blendStateDescription.RenderTarget[0].SourceAlphaBlend = BlendOption.Zero;
			blendStateDescription.RenderTarget[0].DestinationAlphaBlend = BlendOption.Zero;
			blendStateDescription.RenderTarget[0].AlphaBlendOperation = BlendOperation.Add;
			blendStateDescription.RenderTarget[0].RenderTargetWriteMask = ColorWriteMaskFlags.All;

			using (var blendState = SharpDX.Toolkit.Graphics.BlendState.New(gd, blendStateDescription))
				gd.SetBlendState(blendState);

			uiPrimitive = SharpDX.Toolkit.Graphics.GeometricPrimitive.Plane.New(gd, 2, 1);

			using (SharpDX.DirectWrite.Factory factoryDW = new SharpDX.DirectWrite.Factory())
			{
				textFormat = new SharpDX.DirectWrite.TextFormat(factoryDW, "Segoe UI Light", 34f)
				{
					TextAlignment = SharpDX.DirectWrite.TextAlignment.Center,
					ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center
				};

				textFormatSmall = new SharpDX.DirectWrite.TextFormat(factoryDW, "Segoe UI Light", 20f)
				{
					TextAlignment = SharpDX.DirectWrite.TextAlignment.Center,
					ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center
				};

				textFormatList = new SharpDX.DirectWrite.TextFormat(factoryDW, "Segoe UI", 21f)
				{
					TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading,
					ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center,
					WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap
				};
			}

			textBrush = new SharpDX.Direct2D1.SolidColorBrush(target2d, new Color(1f, 1f, 1f, 1f));
			dimTextBrush = new SharpDX.Direct2D1.SolidColorBrush(target2d, new Color(0.75f, 0.75f, 0.75f, 1f));
			blueBrush = new SharpDX.Direct2D1.SolidColorBrush(target2d, new Color(0, 167, 245, 255));
			highlightBrush = new SharpDX.Direct2D1.SolidColorBrush(target2d, new Color4(0f, 167f / 255f, 245f / 255f, 0.35f));
			starBrush = new SharpDX.Direct2D1.SolidColorBrush(target2d, new Color4(1.0f, 0.84f, 0.15f, 1.0f));
			hudBgBrush = new SharpDX.Direct2D1.SolidColorBrush(target2d, new Color4(0f, 0f, 0f, 0.72f));

			uiInitialized = true;
		}


		public void EnqueueUIRedraw(bool recenter = false)
		{
			redraw = true;
			if (recenter)
				recenterRequested = true;
		}

		public void ShowSeekBar(bool recenterIfHidden = true, bool forceRecenter = false)
		{
			if (forceRecenter || (recenterIfHidden && isUIHidden))
				recenterRequested = true;
			showAlpha = 1f;
			isUIHidden = false;
			redraw = true;
		}

		public bool ConsumeRecenterRequest()
		{
			if (!recenterRequested)
				return false;
			recenterRequested = false;
			return true;
		}


		private static string TruncateMiddleOrLeft(string text, int maxChars)
		{
			if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
				return text ?? string.Empty;
			return "..." + text.Substring(text.Length - (maxChars - 3));
		}


		private static string FormatProjectionAndStereoBadge()
		{
			var decoder = MediaDecoder.Instance;
			if (decoder == null)
				return string.Empty;

			ProjectionMode proj = decoder.EffectiveProjection;
			string projLabel;
			switch (proj)
			{
				case ProjectionMode.Dome:
					projLabel = "180° Equirect (_LR)";
					break;
				case ProjectionMode.Fisheye180:
					projLabel = "180° Fisheye / Dual-Fisheye";
					break;
				case ProjectionMode.Mesh180:
					projLabel = "180° Mesh-Projected";
					break;
				case ProjectionMode.CubeFacebook:
					projLabel = "360° Cubemap (FB)";
					break;
				case ProjectionMode.Sphere:
				default:
					projLabel = "360° Equirectangular";
					break;
			}
			if (decoder.Projection == ProjectionMode.Autodetect)
				projLabel += " (Auto)";

			VideoMode stereo = decoder.CurrentMode;
			string stereoLabel;
			switch (stereo)
			{
				case VideoMode.SideBySide:
					stereoLabel = "Side-by-Side (_LR / SBS)";
					break;
				case VideoMode.SideBySideReversed:
					stereoLabel = "SBS Reversed (_RL)";
					break;
				case VideoMode.TopBottom:
					stereoLabel = "Top-Bottom (_TB / OU)";
					break;
				case VideoMode.TopBottomReversed:
					stereoLabel = "TB Reversed (_BT)";
					break;
				case VideoMode.Mono:
				default:
					stereoLabel = "Mono (2D)";
					break;
			}
			if (decoder.StereoMode == VideoMode.Autodetect)
				stereoLabel += " (Auto)";

			return $"Projection: {projLabel}   |   Stereo: {stereoLabel}";
		}

		private void DrawTimeBar(float currentTime, float duration)
		{
			float safeDuration = (float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0f) ? 0f : duration;
			float safeCurrentTime = (float.IsNaN(currentTime) || float.IsInfinity(currentTime) || currentTime < 0f) ? 0f : currentTime;
			if (safeDuration > 0f && safeCurrentTime > safeDuration)
				safeCurrentTime = safeDuration;

			var barLength = 1024 - 256;
			float progress = safeDuration > 0f ? MathUtil.Clamp(safeCurrentTime / safeDuration, 0f, 1f) : 0f;
			var currentLength = barLength * progress;

			target2d.DrawLine(new Vector2(128, 384), new Vector2(1024 - 128, 384), textBrush, 6);
			target2d.DrawLine(new Vector2(128, 384), new Vector2(128 + currentLength, 384), blueBrush, 6);
			var ellipse = new DX2D.Ellipse(new Vector2(128 + currentLength + 3.5f, 384), 7, 7);
			target2d.FillEllipse(ellipse, blueBrush);

			textFormatSmall.TextAlignment = SharpDX.DirectWrite.TextAlignment.Trailing;
			target2d.DrawText(TimeSpan.FromSeconds(Math.Floor(safeDuration)).ToString(), textFormatSmall, new Rectangle(1024 - 128 - 150, 384, 150, 50), textBrush);

			int positionx = (int)(128 + currentLength - 100 / 2);
			positionx = MathUtil.Clamp(positionx, 128, 1024 - 128 - 100);
			textFormatSmall.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
			target2d.DrawText(TimeSpan.FromSeconds(Math.Floor(safeCurrentTime)).ToString(), textFormatSmall, new Rectangle(positionx, 340, 100, 32), textBrush);
		}

		private void DrawVolumeBar(float volume, bool isMuted)
		{
			float clamped = MathUtil.Clamp(isMuted ? 0f : volume, 0f, 1f);
			int percent = (int)Math.Round(clamped * 100f);

			var panelRect = new RectangleF(272f, 166f, 480f, 94f);
			target2d.FillRoundedRectangle(new DX2D.RoundedRectangle { Rect = panelRect, RadiusX = 12f, RadiusY = 12f }, hudBgBrush);
			target2d.DrawRoundedRectangle(new DX2D.RoundedRectangle { Rect = panelRect, RadiusX = 12f, RadiusY = 12f }, blueBrush, 1.5f);

			string label = isMuted ? "Volume: Muted" : $"Volume: {percent}%";
			textFormatSmall.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
			target2d.DrawText(label, textFormatSmall, new RectangleF(272f, 174f, 480f, 34f), textBrush);

			const float barX = 312f;
			const float barY = 218f;
			const float barW = 400f;
			const float barH = 16f;

			target2d.FillRectangle(new RectangleF(barX, barY, barW * clamped, barH), blueBrush);
			target2d.DrawRectangle(new RectangleF(barX, barY, barW, barH), textBrush, 1.5f);
		}

		private void DrawStatusBanner(string text, float progress, float topY)
		{
			if (string.IsNullOrWhiteSpace(text))
				return;

			bool hasProgressBar = progress >= 0f;
			float panelH = hasProgressBar ? 94f : 62f;
			var panelRect = new RectangleF(252f, topY, 520f, panelH);
			target2d.FillRoundedRectangle(new DX2D.RoundedRectangle { Rect = panelRect, RadiusX = 12f, RadiusY = 12f }, hudBgBrush);
			target2d.DrawRoundedRectangle(new DX2D.RoundedRectangle { Rect = panelRect, RadiusX = 12f, RadiusY = 12f }, blueBrush, 1.5f);

			textFormatSmall.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
			if (hasProgressBar)
			{
				target2d.DrawText(text, textFormatSmall, new RectangleF(252f, topY + 8f, 520f, 34f), textBrush);

				float clamped = MathUtil.Clamp(progress, 0f, 1f);
				const float barX = 292f;
				float barY = topY + 52f;
				const float barW = 440f;
				const float barH = 16f;

				target2d.FillRectangle(new RectangleF(barX, barY, barW * clamped, barH), blueBrush);
				target2d.DrawRectangle(new RectangleF(barX, barY, barW, barH), textBrush, 1.5f);
			}
			else
			{
				target2d.DrawText(text, textFormatSmall, new RectangleF(252f, topY + 12f, 520f, 38f), textBrush);
			}
		}

		private void DrawStarIcon(float cx, float cy, float outerRadius, float innerRadius)
		{
			using (var geometry = new DX2D.PathGeometry(target2d.Factory))
			using (var sink = geometry.Open())
			{
				for (int p = 0; p < 10; p++)
				{
					double angle = -Math.PI / 2.0 + p * (Math.PI / 5.0);
					float r = (p % 2 == 0) ? outerRadius : innerRadius;
					var pt = new Vector2(cx + (float)(Math.Cos(angle) * r), cy + (float)(Math.Sin(angle) * r));
					if (p == 0)
						sink.BeginFigure(pt, DX2D.FigureBegin.Filled);
					else
						sink.AddLine(pt);
				}
				sink.EndFigure(DX2D.FigureEnd.Closed);
				sink.Close();
				target2d.FillGeometry(geometry, starBrush);
			}
		}

		public void Draw(
			ServiceResult serviceResult,
			float currentTime,
			float duration,
			VRFileBrowser fileBrowser = null,
			bool seekBarOnly = false,
			bool volumeBarOnly = false,
			float volume = 0.5f,
			bool isMuted = false,
			bool statusHudOnly = false,
			string statusHudText = null,
			float statusHudProgress = -1f)
		{
			if (!uiInitialized) return;
			if (seekBarOnly != lastDrawnSeekBarOnly ||
				volumeBarOnly != lastDrawnVolumeBarOnly ||
				statusHudOnly != lastDrawnStatusHudOnly)
			{
				lastDrawnSeekBarOnly = seekBarOnly;
				lastDrawnVolumeBarOnly = volumeBarOnly;
				lastDrawnStatusHudOnly = statusHudOnly;
				redraw = true;
			}
			if (!redraw) return;
			redraw = false;

			target2d.BeginDraw();

			textFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
			textFormatSmall.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;

			bool isBrowserOpen = fileBrowser != null && fileBrowser.IsOpen;
			bool isVideo = serviceResult != null && serviceResult.contentType == ServiceResult.ContentType.video;

			if (!isBrowserOpen && isVideo && (seekBarOnly || volumeBarOnly || statusHudOnly))
			{
				target2d.Clear(new Color4(0f, 0f, 0f, 0f));
				if (seekBarOnly)
					DrawTimeBar(currentTime, duration);
				if (volumeBarOnly)
					DrawVolumeBar(volume, isMuted);
				if (statusHudOnly)
					DrawStatusBanner(statusHudText, statusHudProgress, volumeBarOnly ? 58f : 166f);
				target2d.EndDraw();
				return;
			}

			target2d.Clear(new Color4(0, 0, 0, 0.78f));

			target2d.DrawLine(new Vector2(0, 1), new Vector2(1024, 1), blueBrush, 2);
			target2d.DrawLine(new Vector2(0, 511), new Vector2(1024, 511), blueBrush, 2);

			if (isBrowserOpen)
			{
				DrawFileBrowser(fileBrowser);
			}
			else if (isVideo)
			{
				target2d.DrawText("now playing:", textFormatSmall, new RectangleF(0, 0, 1024, 60), textBrush);

				string volText = isMuted ? "Vol: Muted" : $"Vol: {(int)Math.Round(MathUtil.Clamp(volume, 0f, 1f) * 100f)}%";
				textFormatSmall.TextAlignment = SharpDX.DirectWrite.TextAlignment.Trailing;
				target2d.DrawText(volText, textFormatSmall, new RectangleF(1024 - 220, 12, 180, 36), blueBrush);
				textFormatSmall.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;

				target2d.DrawText(serviceResult.TitleWithFallback, textFormat, new RectangleF(0, 40, 1024, 85), textBrush);
				target2d.DrawText(FormatProjectionAndStereoBadge(), textFormatSmall, new RectangleF(0, 125, 1024, 36), blueBrush);

				DrawTimeBar(currentTime, duration);

				target2d.DrawEllipse(new DX2D.Ellipse(new Vector2(512, 240), 48, 48), textBrush, 2);
				var dist = 8;
				var len = 10;
				target2d.DrawLine(new Vector2(512 - dist, 240 - len), new Vector2(512 - dist, 240 + len), textBrush, 2);
				target2d.DrawLine(new Vector2(512 + dist, 240 - len), new Vector2(512 + dist, 240 + len), textBrush, 2);

				target2d.DrawLine(new Vector2(48, 456), new Vector2(1024 - 48, 456), blueBrush, 1);
				target2d.DrawText("A/X: Play/Pause | B/Y: Files | Stick L/R: -/+10s (+Grip: 1m, +Trig: 5m) | Stick U/D: Vol", textFormatSmall, new RectangleF(0, 458, 1024, 50), dimTextBrush);
			}
			else
			{
				target2d.DrawText("BIVROST® 360Player", textFormat, new RectangleF(0, 140, 1024, 80), textBrush);
				target2d.DrawText("Press B, Y, A, or X on your Oculus controller to select a local file", textFormatSmall, new RectangleF(0, 230, 1024, 60), blueBrush);
			}

			target2d.EndDraw();
		}

		private void DrawFileBrowser(VRFileBrowser fileBrowser)
		{
			string currentDir;
			int selectedIndex;
			int scrollOffset;
			var entries = fileBrowser.GetSnapshot(out currentDir, out selectedIndex, out scrollOffset);

			string subtitleRaw = fileBrowser.HeaderSubtitle;
			string headerSubtitle = string.IsNullOrEmpty(subtitleRaw)
				? "Locations"
				: TruncateMiddleOrLeft(subtitleRaw, 68);

			textFormatList.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
			target2d.DrawText("SELECT LOCAL FILE", textFormatList, new RectangleF(48, 10, 450, 34), blueBrush);

			if (entries.Count > 0)
			{
				textFormatSmall.TextAlignment = SharpDX.DirectWrite.TextAlignment.Trailing;
				target2d.DrawText($"{selectedIndex + 1} / {entries.Count}", textFormatSmall, new RectangleF(1024 - 248, 10, 200, 34), dimTextBrush);
			}

			textFormatSmall.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
			target2d.DrawText(headerSubtitle, textFormatSmall, new RectangleF(48, 44, 928, 30), dimTextBrush);

			target2d.DrawLine(new Vector2(48, 80), new Vector2(1024 - 48, 80), blueBrush, 1.5f);

			if (entries.Count == 0)
			{
				textFormatSmall.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
				target2d.DrawText("No supported media files or folders found.", textFormatSmall, new RectangleF(0, 220, 1024, 50), dimTextBrush);
			}
			else
			{
				int endIndex = Math.Min(entries.Count, scrollOffset + VRFileBrowser.VisibleRowCount);
				for (int i = scrollOffset; i < endIndex; i++)
				{
					int row = i - scrollOffset;
					float rowTop = 88f + row * 45f;
					var rowRect = new RectangleF(48f, rowTop, 1024f - 96f, 41f);

					if (i == selectedIndex)
					{
						target2d.FillRectangle(rowRect, highlightBrush);
						target2d.FillRectangle(new RectangleF(48f, rowTop, 6f, 41f), blueBrush);
					}

					if (entries[i].IsFavorite)
					{
						DrawStarIcon(72f, rowTop + 20.5f, 10f, 4.5f);
					}

					string label = TruncateMiddleOrLeft(entries[i].DisplayName, 62);
					target2d.DrawText(
						label,
						textFormatList,
						new RectangleF(92f, rowTop, 1024f - 156f, 41f),
						i == selectedIndex ? textBrush : dimTextBrush);
				}
			}

			target2d.DrawLine(new Vector2(48, 456), new Vector2(1024 - 48, 456), blueBrush, 1.5f);
			textFormatSmall.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
			target2d.DrawText("Stick U/D: Move | A/X/Trigger: Open | Stick Click: Favorite | Stick L/R: -/+10s | B/Y: Close", textFormatSmall, new RectangleF(0, 458, 1024, 50), dimTextBrush);
		}


		public bool isUIHidden = true;
		private float showAlpha = 0f;


		public void Update(float deltaTime, bool pause)
		{
			showAlpha = showAlpha.LerpInPlace(pause ? 1 : 0, 8f * deltaTime);
			if (showAlpha < 0.01f)
				showAlpha = 0f;
			else if (showAlpha > 0.99f)
				showAlpha = 1f;

			isUIHidden = showAlpha <= 0;
		}

		public void Render(float deltaTime, Matrix viewMatrix, Matrix projectionMatrix, Vector3 viewPosition, bool pause, bool updateAlpha = true)
		{
			uiEffect.View = viewMatrix;
			uiEffect.Projection = projectionMatrix;

			if (updateAlpha)
			{
				Update(deltaTime, pause);
			}

			// distance ui plane - eye
			// { 0    for d <= uiDistanceDisappear
			// { 0..1 for uiDistanceDisappear < d < uiDistanceFade
			// { 1    for d >= uiDistanceFade
			float dot;
			Vector3.Dot(ref uiPlane.Normal, ref viewPosition, out dot);
			float d = Math.Abs(dot - uiPlane.D);
			float overrideShowAlpha = (d - uiDistanceDisappear) / (uiDistanceFade - uiDistanceDisappear);
			if (overrideShowAlpha < 0) overrideShowAlpha = 0;
			else if (overrideShowAlpha > 1) overrideShowAlpha = 1;

			uiEffect.Alpha = showAlpha * overrideShowAlpha;

			if (uiEffect.Alpha > 0)
				uiPrimitive.Draw(uiEffect);
		}	


		Plane uiPlane = new Plane(1);

		internal void SetWorldPosition(Vector3 forward, Vector3 viewPosition, bool reverseDistance)
		{
			float yaw = (float)(Math.PI - Math.Atan2(forward.X, forward.Z));
			uiEffect.World = Matrix.Identity * Matrix.Scaling(1f) * Matrix.Translation(0, 0, reverseDistance ? -uiDistanceStart : uiDistanceStart) * Matrix.RotationAxis(Vector3.Up, yaw) * Matrix.Translation(viewPosition);
			uiPlane = new Plane(-uiEffect.World.TranslationVector, uiEffect.World.Forward);
		}


		public void Dispose()
		{
			uiInitialized = false;

			uiToolkitTexture?.Dispose();
			uiTexture?.Dispose();
			uiEffect?.Dispose();

			target2d?.Dispose();
			uiPrimitive?.Dispose();

			textFormat?.Dispose();
			textFormatSmall?.Dispose();
			textFormatList?.Dispose();
			textBrush?.Dispose();
			dimTextBrush?.Dispose();
			blueBrush?.Dispose();
			highlightBrush?.Dispose();
			starBrush?.Dispose();
			hudBgBrush?.Dispose();
		}

	}
}
