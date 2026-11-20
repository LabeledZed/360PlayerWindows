using Bivrost.AnalyticsForVR;
using SharpDX.Direct3D11;
using System;
using System.Threading;
using SharpDX;
using Bivrost.Bivrost360Player.Streaming;
using Bivrost.Log;
using Bivrost.Bivrost360Player.Tools;
using System.Drawing;
using System.Drawing.Imaging;

namespace Bivrost.Bivrost360Player
{

	public class HeadsetError : Exception
	{
		public HeadsetError(string msg) : base(msg) { }
		public HeadsetError(Exception inner, string msg) : base(msg, inner) { }
	}


	public abstract class Headset : ILookProvider, IContentUpdatableFromMediaEngine
	{

		private static ServiceResult nothingIsPlaying = new ServiceResult(null, "(none)", "nothing")
		{
			description = "",
			stereoscopy = VideoMode.Autodetect,
			projection = ProjectionMode.Sphere,
			title = "",
			contentType = ServiceResult.ContentType.none
		};

		private ServiceResult _media;
		public ServiceResult Media
		{
			get => _media ?? nothingIsPlaying;
			set
			{
				pause = false;
				_media = value;
				if (value != null && value.contentType != ServiceResult.ContentType.none)
				{
					FileBrowser.Close();
				}
				vrui?.EnqueueUIRedraw();
				
				//UpdateSceneSettings(Media.projection, Media.stereoscopy);
			}
		}
		//public bool _stereoVideo => Array.IndexOf(new[] { VideoMode.Mono, VideoMode.Autodetect }, Media.stereoscopy) < 0;
		protected float Duration => (float)MediaDecoder.Instance.Duration;

		public readonly VRFileBrowser FileBrowser = new VRFileBrowser();

		protected Logger log;


		private bool _wasBrowserOpen = false;

		public Headset()
		{
			FileBrowser.OnChanged += () =>
			{
				bool isOpen = FileBrowser.IsOpen;
				bool justOpened = isOpen && !_wasBrowserOpen;
				_wasBrowserOpen = isOpen;
				if (justOpened)
					vrui?.ShowSeekBar(recenterIfHidden: false, forceRecenter: true);
				else
					vrui?.EnqueueUIRedraw();
			};
		}


		public void Start()
		{
			abort = false;
			pause = false;
			waitForRendererStop.Reset();
			if (Lock)
				return;

			var thread = new Thread(() =>
			{
				try
				{
                    MediaDecoder.Instance.OnContentChanged += ContentChanged;
                    ContentChanged();

                    Render();
				}
				catch (Exception exc)
				{
					log.Error(exc);
					Logic.Notify("An error was encountered in VR playback. See log for details.");
				}
				finally
				{
					MediaDecoder.Instance.OnContentChanged -= ContentChanged;
					Lock = false;
					CleanupResources();

					abort = false;
					pause = false;
				}
			})
			{
				Name = $"Headset: {DescribeType}",
				IsBackground = true
			};
			thread.Start();
		}


		protected abstract void Render();



		volatile bool _playbackLock = false;
		public bool Lock { get { return _playbackLock; } protected set { this._playbackLock = value; } }

		protected object localCritical = new object();


		protected ManualResetEvent waitForRendererStop = new ManualResetEvent(false);
		protected volatile bool abort = false;
		protected volatile bool pause = false;

		protected float currentTime = 0;

		protected SharpDX.Toolkit.Graphics.Effect customEffectL;
		protected SharpDX.Toolkit.Graphics.Effect customEffectR;


		private static SharpDX.Toolkit.Graphics.EffectCompilerResult gammaShader = null;
		private static SharpDX.Toolkit.Graphics.EffectCompilerResult GammaShader
		{
			get
			{
				if (gammaShader == null)
				{
					string shaderSource = Properties.Resources.GammaShader;
					SharpDX.Toolkit.Graphics.EffectCompiler compiler = new SharpDX.Toolkit.Graphics.EffectCompiler();
					var shaderCode = compiler.Compile(shaderSource, "gamma shader", SharpDX.Toolkit.Graphics.EffectCompilerFlags.Debug | SharpDX.Toolkit.Graphics.EffectCompilerFlags.EnableBackwardsCompatibility | SharpDX.Toolkit.Graphics.EffectCompilerFlags.SkipOptimization);

					if (shaderCode.HasErrors)
						throw new HeadsetError("Shader compile error:\n" + string.Join("\n", shaderCode.Logger.Messages));
					gammaShader = shaderCode;
				}
				return gammaShader;
			}
		}


		public static SharpDX.Toolkit.Graphics.Effect GetCustomEffect(SharpDX.Toolkit.Graphics.GraphicsDevice gd)
		{
			var ce = new SharpDX.Toolkit.Graphics.Effect(gd, GammaShader.EffectData);
			ce.CurrentTechnique = ce.Techniques["ColorTechnique"];
			ce.CurrentTechnique.Passes[0].Apply();
			return ce;
		}


		protected VRUI vrui;
		public const double SeekBarDisplayDurationSeconds = 1.0;
		public const double VolumeHudDisplayDurationSeconds = 1.5;
		private long _seekBarVisibleUntilUtcTicks = 0;
		private long _volumeHudVisibleUntilUtcTicks = 0;
		private long _statusHudVisibleUntilUtcTicks = 0;
		protected float currentVolume = 0.5f;
		protected bool currentVolumeMuted = false;
		protected string statusHudText = null;
		protected float statusHudProgress = -1f;

		public bool IsSeekBarActive =>
			DateTime.UtcNow.Ticks < Interlocked.Read(ref _seekBarVisibleUntilUtcTicks);

		public bool IsVolumeHudActive =>
			DateTime.UtcNow.Ticks < Interlocked.Read(ref _volumeHudVisibleUntilUtcTicks);

		public bool IsStatusHudActive =>
			DateTime.UtcNow.Ticks < Interlocked.Read(ref _statusHudVisibleUntilUtcTicks);

		protected bool IsSeekBarOnlyMode =>
			!pause && !FileBrowser.IsOpen && Media.contentType == ServiceResult.ContentType.video && IsSeekBarActive;

		protected bool IsVolumeBarOnlyMode =>
			!pause && !FileBrowser.IsOpen && Media.contentType == ServiceResult.ContentType.video && IsVolumeHudActive;

		protected bool IsStatusHudOnlyMode =>
			!pause && !FileBrowser.IsOpen && Media.contentType == ServiceResult.ContentType.video && IsStatusHudActive;

		protected bool ShouldShowVRUI
		{
			get
			{
				if (FileBrowser.IsOpen)
					return true;
				if (Media.contentType == ServiceResult.ContentType.none)
					return true;
				if (Media.contentType != ServiceResult.ContentType.video)
					return false;
				return pause || IsSeekBarActive || IsVolumeHudActive || IsStatusHudActive;
			}
		}

		public void Pause()
		{
			vrui?.EnqueueUIRedraw();
			pause = true;
		}

		public void UnPause()
		{
			pause = false;
			Interlocked.Exchange(ref _seekBarVisibleUntilUtcTicks, 0);
			Interlocked.Exchange(ref _volumeHudVisibleUntilUtcTicks, 0);
			Interlocked.Exchange(ref _statusHudVisibleUntilUtcTicks, 0);
			vrui?.EnqueueUIRedraw();
		}

		public void Stop()
		{
			Media = null;
			pause = true;
			Interlocked.Exchange(ref _seekBarVisibleUntilUtcTicks, 0);
			Interlocked.Exchange(ref _volumeHudVisibleUntilUtcTicks, 0);
			Interlocked.Exchange(ref _statusHudVisibleUntilUtcTicks, 0);
			vrui?.EnqueueUIRedraw();
		}

		public void UpdateTime(float time)
		{
			vrui?.EnqueueUIRedraw();
			currentTime = time;
		}

		public void NotifySeek(float time)
		{
			currentTime = time;
			long expiryTicks = DateTime.UtcNow.AddSeconds(SeekBarDisplayDurationSeconds).Ticks;
			Interlocked.Exchange(ref _seekBarVisibleUntilUtcTicks, expiryTicks);
			vrui?.ShowSeekBar(recenterIfHidden: true);
		}

		public void NotifyVolumeChanged(float volume, bool isMuted)
		{
			currentVolume = MathUtil.Clamp(volume, 0f, 1f);
			currentVolumeMuted = isMuted;
			long expiryTicks = DateTime.UtcNow.AddSeconds(VolumeHudDisplayDurationSeconds).Ticks;
			Interlocked.Exchange(ref _volumeHudVisibleUntilUtcTicks, expiryTicks);
			vrui?.ShowSeekBar(recenterIfHidden: true);
		}

		public void NotifyHoldProgress(string label, float progress)
		{
			if (progress <= 0f || string.IsNullOrWhiteSpace(label))
			{
				Interlocked.Exchange(ref _statusHudVisibleUntilUtcTicks, 0);
				statusHudText = null;
				statusHudProgress = -1f;
				vrui?.EnqueueUIRedraw();
				return;
			}

			statusHudText = label;
			statusHudProgress = MathUtil.Clamp(progress, 0f, 1f);
			long expiryTicks = DateTime.UtcNow.AddSeconds(0.35).Ticks;
			Interlocked.Exchange(ref _statusHudVisibleUntilUtcTicks, expiryTicks);
			vrui?.ShowSeekBar(recenterIfHidden: true);
		}

		public void NotifyRecenterProgress(float progress)
		{
			NotifyHoldProgress("Hold to Recenter View...", progress);
		}

		public void NotifyRecenterCompleted()
		{
			statusHudText = "View Recentered";
			statusHudProgress = 1f;
			long expiryTicks = DateTime.UtcNow.AddSeconds(1.2).Ticks;
			Interlocked.Exchange(ref _statusHudVisibleUntilUtcTicks, expiryTicks);
			vrui?.ShowSeekBar(recenterIfHidden: false);
			vrui?.EnqueueUIRedraw(recenter: true);
		}

		public void NotifyStatusMessage(string message, double durationSeconds = 1.5)
		{
			if (string.IsNullOrWhiteSpace(message))
				return;
			statusHudText = message;
			statusHudProgress = -1f;
			long expiryTicks = DateTime.UtcNow.AddSeconds(durationSeconds).Ticks;
			Interlocked.Exchange(ref _statusHudVisibleUntilUtcTicks, expiryTicks);
			vrui?.ShowSeekBar(recenterIfHidden: true);
		}

		public virtual void RequestRecenterView()
		{
			NotifyRecenterCompleted();
		}

		public void RefreshUI()
		{
			vrui?.EnqueueUIRedraw();
		}


		public void Abort()
		{
			abort = true;
		}
		



		protected SharpDX.Toolkit.Graphics.GraphicsDevice _gd;
		protected Device _device;

        public abstract event Action<Vector3, Quaternion, float> ProvideLook;

        abstract protected float Gamma { get; }
        public abstract string DescribeType { get; }


		private SharpDX.Toolkit.Graphics.Texture2D _defaultBackgroundTexture = null;
		public SharpDX.Toolkit.Graphics.Texture2D DefaultBackgroundTexture
		{
			get
			{
				if(_defaultBackgroundTexture == null)
				{
					var assembly = GetType().Assembly;
					var fullResourceName = "Bivrost.Bivrost360Player.Resources.default-background-equirectangular.png";
					using (var stream = assembly.GetManifestResourceStream(fullResourceName))
					{
						_defaultBackgroundTexture = SharpDX.Toolkit.Graphics.Texture2D.Load(_gd, stream);
					}

					_defaultBackgroundTexture.Disposing += (s, e) =>
					{
						log.Info("Default background is being disposed.");
					};
				}


				return _defaultBackgroundTexture;
			}
		}

		
		SharpDX.Toolkit.Graphics.Texture2D toolkitTextureL;
		SharpDX.Toolkit.Graphics.Texture2D toolkitTextureR;

		void TextureCleanup()
		{
			toolkitTextureL?.Dispose();
			toolkitTextureR?.Dispose();
			toolkitTextureL = null;
			toolkitTextureR = null;

			localBitmapTextureL?.Dispose();
			localBitmapTextureR?.Dispose();
			localBitmapTextureL = null;
			localBitmapTextureR = null;
		}

		protected void CleanupResources()
		{
			lock (localCritical)
			{
				TextureCleanup();

				primitive?.Dispose();
				primitive = null;

				_defaultBackgroundTexture?.Dispose();
				_defaultBackgroundTexture = null;
			}
		}


		public void SetDefaultScene()
		{
            log.Info("Showing default scene");

			lock (localCritical)
			{
				TextureCleanup();

				customEffectL.Parameters["UserTex"].SetResource(DefaultBackgroundTexture);
				customEffectL.Parameters["gammaFactor"].SetValue(Gamma);
				customEffectL.CurrentTechnique = customEffectL.Techniques["ColorTechnique"];
				customEffectL.CurrentTechnique.Passes[0].Apply();

				customEffectR.Parameters["UserTex"].SetResource(DefaultBackgroundTexture);
				customEffectR.Parameters["gammaFactor"].SetValue(Gamma);
				customEffectR.CurrentTechnique = customEffectR.Techniques["ColorTechnique"];
				customEffectR.CurrentTechnique.Passes[0].Apply();

				primitive?.Dispose();
				primitive = GraphicTools.CreateGeometry(ProjectionMode.Sphere, _gd, false);
			}

			vrui?.EnqueueUIRedraw();
		}


		private volatile bool contentUpdateRequested = true;
		protected void UpdateContentIfRequested()
		{
			if (contentUpdateRequested)
			{
				lock (localCritical)
				{
                    if (MediaDecoder.Instance.ContentRequested(this))
                    {
                        log.Info("Content updated");
                        contentUpdateRequested = false;
                    }
				}
			}
		}
		private void ContentChanged()
		{
			contentUpdateRequested = true;
		}


		abstract public bool IsPresent();


		protected SharpDX.Toolkit.Graphics.GeometricPrimitive primitive;

		void IContentUpdatableFromMediaEngine.ReceiveTextures(Texture2D textureL, Texture2D textureR)
		{
			if (MediaDecoder.Instance.TextureReleased)
			{
				log.Error("MediaDecoder texture released");
				return;
			}

			lock (localCritical)
			{
				TextureCleanup();

				using (var resourceL = textureL.QueryInterface<SharpDX.DXGI.Resource>())
				using (var sharedTexL = _device.OpenSharedResource<Texture2D>(resourceL.SharedHandle))
				{
					toolkitTextureL = SharpDX.Toolkit.Graphics.Texture2D.New(_gd, sharedTexL);
					customEffectL.Parameters["UserTex"].SetResource(toolkitTextureL);
					customEffectL.Parameters["gammaFactor"].SetValue(Gamma);
					customEffectL.CurrentTechnique = customEffectL.Techniques["ColorTechnique"];
					customEffectL.CurrentTechnique.Passes[0].Apply();
				}

				using (var resourceR = (textureR ?? textureL).QueryInterface<SharpDX.DXGI.Resource>())
				using (var sharedTexR = _device.OpenSharedResource<Texture2D>(resourceR.SharedHandle))
				{
					toolkitTextureR = SharpDX.Toolkit.Graphics.Texture2D.New(_gd, sharedTexR);
					customEffectR.Parameters["UserTex"].SetResource(toolkitTextureR);
					customEffectR.Parameters["gammaFactor"].SetValue(Gamma);
					customEffectR.CurrentTechnique = customEffectR.Techniques["ColorTechnique"];
					customEffectR.CurrentTechnique.Passes[0].Apply();
				}

				//_device.ImmediateContext.Flush();
			}

			vrui?.EnqueueUIRedraw();
		}


		Texture2D localBitmapTextureL;
		Texture2D localBitmapTextureR;



		private Texture2D BitmapAndCoordsToTexture2D(Bitmap bitmap, MediaDecoder.ClipCoords coords)
		{
			var rect = coords.SrcRectSystemDrawing(bitmap.Width, bitmap.Height);
			var data = bitmap.LockBits(
				rect,
				ImageLockMode.ReadOnly,
				PixelFormat.Format32bppRgb
			);
			try
			{
				DataRectangle dataRect = new DataRectangle(data.Scan0, data.Stride);
				return new Texture2D(
					_device,
					new Texture2DDescription()
					{
						Width = rect.Width,
						Height = rect.Height,
						MipLevels = 1,
						ArraySize = 1,
						Format = SharpDX.DXGI.Format.B8G8R8X8_UNorm,
						Usage = ResourceUsage.Default,
						SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
						BindFlags = /*BindFlags.RenderTarget |*/ BindFlags.ShaderResource,
						CpuAccessFlags = CpuAccessFlags.None,
						OptionFlags = ResourceOptionFlags.Shared
					},
					dataRect
				);
			}
			finally
			{
				bitmap.UnlockBits(data);
			}
		}

		void IContentUpdatableFromMediaEngine.ReceiveBitmap(Bitmap bitmap, MediaDecoder.ClipCoords texL, MediaDecoder.ClipCoords texR)
		{
			log.Info($"Received image of size {bitmap.Width}x{bitmap.Height} from stream");


			lock (localCritical)
			{
				TextureCleanup();

				localBitmapTextureL = BitmapAndCoordsToTexture2D(bitmap, texL);
				localBitmapTextureR = (texR != null) ? BitmapAndCoordsToTexture2D(bitmap, texR) : null;

				using (var resourceL = localBitmapTextureL.QueryInterface<SharpDX.DXGI.Resource>())
				using (var sharedTexL = _device.OpenSharedResource<Texture2D>(resourceL.SharedHandle))
				{
					toolkitTextureL = SharpDX.Toolkit.Graphics.Texture2D.New(_gd, sharedTexL);
					customEffectL.Parameters["UserTex"].SetResource(toolkitTextureL);
					customEffectL.Parameters["gammaFactor"].SetValue(Gamma);
					customEffectL.CurrentTechnique = customEffectL.Techniques["ColorTechnique"];
					customEffectL.CurrentTechnique.Passes[0].Apply();
				}

				using (var resourceR = (localBitmapTextureR ?? localBitmapTextureL).QueryInterface<SharpDX.DXGI.Resource>())
				using (var sharedTexR = _device.OpenSharedResource<Texture2D>(resourceR.SharedHandle))
				{
					toolkitTextureR = SharpDX.Toolkit.Graphics.Texture2D.New(_gd, sharedTexR);
					customEffectR.Parameters["UserTex"].SetResource(toolkitTextureR);
					customEffectR.Parameters["gammaFactor"].SetValue(Gamma);
					customEffectR.CurrentTechnique = customEffectR.Techniques["ColorTechnique"];
					customEffectR.CurrentTechnique.Passes[0].Apply();
				}
			}

			log.Info($"Changed texture");
		}


		void IContentUpdatableFromMediaEngine.ClearContent()
		{
			SetDefaultScene();
		}

		void IContentUpdatableFromMediaEngine.SetProjection(ProjectionMode projection)
		{
			lock (localCritical)
			{
				primitive?.Dispose();
				if (primitive != null)
					System.Diagnostics.Debug.WriteLine(primitive.GetHashCode().ToString());
				primitive = GraphicTools.CreateGeometry(projection, _gd, false);
				if (primitive != null)
					System.Diagnostics.Debug.WriteLine(primitive.GetHashCode().ToString());
				;
			}
		}
	}
}
