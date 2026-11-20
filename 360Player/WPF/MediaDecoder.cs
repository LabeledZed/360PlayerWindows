using Bivrost.Bivrost360Player.Streaming;
using Bivrost.Log;
using RestSharp;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.MediaFoundation;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace Bivrost.Bivrost360Player
{

	public enum VideoMode
	{
		Autodetect,
		Mono,
		SideBySide,
		TopBottom,
		SideBySideReversed,
		TopBottomReversed
	}


	public enum ProjectionMode
	{
		Autodetect,
		Sphere,       // Equirectangular 360x180
		CubeFacebook, // Facebook 3x2 Cubemap
		Dome,         // Equirectangular 180x180 (VR180 / 180 SBS / _LR / _TB)
		Fisheye180,   // Fisheye / Dual-Fisheye 180° (SBS / TB / Mono)
		Mesh180       // Mesh-Projected 180° SBS (VR180 Equi-Angular / Mesh Projection)
	}


	public class MediaDecoder
	{

		private Logger log = new Logger("MediaDecoder");

		public class Error
		{
			public long major;
			public int minor;
		}

		public Error LastError;

		private MediaEngine _mediaEngine;
		private MediaEngineEx _mediaEngineEx;
		private readonly object criticalSection = new object();
		private volatile bool waitForFormatChange = false;
		private volatile bool formatChangePending = false;
		private bool mediaManagerStarted = false;

		private long ts;
		private volatile bool manualRender = false;

		private Texture2D _textureL;
		private Texture2D _textureR;
		protected Texture2D TextureL
		{
			get { return _textureL; }
			private set { _textureL = value; }
		}
		protected Texture2D TextureR
		{
			get { return _textureR; }
			private set { _textureR = value; }
		}

		public bool IsPlaying => isPlaying || IsDisplayingStaticContent;
		private volatile bool isPlaying;
		public bool IsPaused {
            get
            {
				if (IsDisplayingStaticContent) return true;

                if (!_initialized) return false;

                if (_mediaEngineEx == null) return false;

                try
                {
                    return _mediaEngineEx.IsPaused;
                }
                catch(NullReferenceException)
                {
                    return false;
                }
            }
        }


		/// <summary>
		/// True if a panorama is displayed, false if a video or nothing at all is being played.
		/// </summary>
		public bool IsDisplayingStaticContent => bitmap != null;


		/// <summary>
		/// Used instead of mediaEngine to show non-animated content.
		/// Should contain the decoded png or jpeg (stored, so the textures can be recreated with other stereoscopy values)
		/// </summary>
		private Bitmap bitmap = null;


		private bool _loop = false;
		public bool Loop
		{
			get { return _loop; }
			set {
				_loop = value;
				lock (criticalSection)
				{
					if (_initialized && _mediaEngineEx != null)
						_mediaEngineEx.Loop = _loop;
				}
			}
		}

		public bool IsEnded
		{
			get
			{
				if (!_initialized) return false;
				var engineEx = _mediaEngineEx;
				if (engineEx == null) return false;
				try
				{
					return (bool)engineEx.IsEnded;
				}
				catch (Exception)
				{
					return false;
				}
			}
		}

		//public int BufferLevel
		//{
		//    get
		//    {
		//        return _mediaEngineEx.
		//    }
		//}

		public int formatCounter = 0;

		public bool Ready { get; private set; }

		public double Duration { get; protected set; } = -1;  //  _mediaEngineEx.Duration; 

		public double CurrentPosition { get; protected set; } = 0; // _mediaEngineEx.CurrentTime; 
		public bool Initialized { get { return _initialized; } }
		private VideoMode LoadedStereoMode { get; set; } = VideoMode.SideBySide;
		private int _lastMediaWidth = 0;
		private int _lastMediaHeight = 0;
		private long _pendingSeekTargetBits = BitConverter.DoubleToInt64Bits(-1.0);
		private long _lastSeekUtcTicks = 0;
		private volatile bool _hasPendingSeek = false;
		private volatile string _audioEndpointId = null;

		public VideoMode StereoMode
		{
			get { return LoadedStereoMode; }
			set
			{
				if (LoadedStereoMode == value) return;
				LoadedStereoMode = value;
				ApplyStereoModeChange();
			}
		}

		private VideoMode _currentMode = VideoMode.SideBySide;
		public VideoMode CurrentMode
		{
			get { return _currentMode; }
			private set
			{
				if (_currentMode == value) return;

				_currentMode = value;
				log.Info($"Stereoscopy = {value}");
			}
		}


		private ProjectionMode _projection = ProjectionMode.Dome;
		public ProjectionMode Projection
		{
			get => _projection;
			set
			{
				if (_projection == value) return;
				_projection = value;
				if (LoadedStereoMode == VideoMode.Autodetect)
				{
					ApplyStereoModeChange();
				}
				OnContentChanged?.Invoke(); // no need for a critical section
			}
		}

		public ProjectionMode EffectiveProjection =>
			ResolveProjection(Projection, CurrentMode, _lastMediaWidth, _lastMediaHeight);

		public ProjectionMode ResolveProjection(ProjectionMode requestedProjection, VideoMode stereoMode, float w, float h)
		{
			if (requestedProjection != ProjectionMode.Autodetect)
				return requestedProjection;

			if (CurrentServiceResult != null && CurrentServiceResult.projection != ProjectionMode.Autodetect)
				return CurrentServiceResult.projection;

			// 360° Side-by-Side has a ~4:1 full-frame aspect ratio (two 2:1 spheres side by side)
			if ((stereoMode == VideoMode.SideBySide || stereoMode == VideoMode.SideBySideReversed) && w > 0 && h > 0)
			{
				float aspect = w / h;
				if (aspect >= 3.2f)
					return ProjectionMode.Sphere;
			}

			// 360° Top-Bottom has a ~1:1 full-frame aspect ratio (two 2:1 spheres stacked vertically)
			if ((stereoMode == VideoMode.TopBottom || stereoMode == VideoMode.TopBottomReversed) && w > 0 && h > 0)
			{
				float aspect = w / h;
				if (aspect >= 0.85f && aspect <= 1.3f)
					return ProjectionMode.Sphere;
			}

			// Default to 180° Equirectangular (_LR / Dome)
			return ProjectionMode.Dome;
		}

		private void ApplyStereoModeChange()
		{
			lock (criticalSection)
			{
				if (IsDisplayingStaticContent && bitmap != null)
				{
					var w = bitmap.Width;
					var h = bitmap.Height;
					_lastMediaWidth = w;
					_lastMediaHeight = h;
					CurrentMode = ParseStereoMode(LoadedStereoMode, w, h);
					ClipCoords texL, texR;
					GetTextureClipCoordinates(CurrentMode, out texL, out texR);
					ContentChanged(renderer =>
					{
						if (bitmap != null)
							renderer.ReceiveBitmap(bitmap, texL, texR);
					});
				}
				else if (_initialized && _mediaEngineEx != null)
				{
					formatChangePending = true;
					manualRender = true;
				}
				else
				{
					CurrentMode = ParseStereoMode(LoadedStereoMode, _lastMediaWidth, _lastMediaHeight);
					OnContentChanged?.Invoke();
				}
			}
		}


		public ServiceResult CurrentServiceResult
		{
			get;
			protected set;
		}


		private volatile bool _initialized = false;
		private volatile bool _rendering = false;
		private ManualResetEvent waitForRenderingEnd = new ManualResetEvent(false);

		private static MediaDecoder _instance = null;
		public static MediaDecoder Instance { get { return MediaDecoder._instance; } }
		public static event Action<MediaDecoder> OnInstantiated;

		private SharpDX.Direct3D11.Device _device;
		private Factory _factory;
		private FeatureLevel[] _levels = new FeatureLevel[] { FeatureLevel.Level_10_0 };
		private DXGIDeviceManager _dxgiManager;

		public event Action OnPlay = delegate { };
		public event Action<double> OnReady = delegate { };
		public event Action OnStop = delegate { };
		public event Action OnEnded = delegate { };
		public event Action<Error> OnError = delegate { };
		public event Action OnAbort = delegate { };
		public event Action<double> OnTimeUpdate = delegate { };
		public event Action<double> OnSeek = delegate { };
		public event Action OnBufferingStarted = delegate { };
		public event Action OnBufferingEnded = delegate { };
		public event Action<double> OnProgress = delegate { };

		[Obsolete]
		public event Action<Texture2D, Texture2D> OnFormatChanged = delegate { };


		#region new file being played reporting

		/// <summary>
		/// Delegate for the OnNewFileIsPlaying event
		/// </summary>
		/// <param name="service">ServiceResult is of the media file playe.d</param>
		/// <param name="concreteFile">The file name (or URI) directly used - the service result might contain many alternatives.</param>
		/// <param name="duration">The media length. Unreliable for some streams, -1 for images</param>
		public delegate void NewFileIsPlayingDelegate(ServiceResult service, string concreteFile, double duration);

		/// <summary>
		/// Event played once after a video or image has been loaded and will be immediately displayed.
		/// The opposite of this is the OnStop event.
		/// </summary>
		public event NewFileIsPlayingDelegate OnNewFileIsPlaying;


		/// <summary>
		/// Flag used to mark that this streamingResult was already reported
		/// </summary>
		private bool _newFileIsPlayingReported = false; 


		protected void NewFileIsPlaying(ServiceResult sr, string concreteFile, double duration)
		{
			if (_newFileIsPlayingReported) return;
			_newFileIsPlayingReported = true;
			OnNewFileIsPlaying?.Invoke(sr, concreteFile, duration);
		}
		#endregion


		private volatile bool textureReleased = true;
		public bool TextureReleased { get { return textureReleased; } }


		public MediaDecoder()
		{
			isPlaying = false;
			Ready = false;
			MediaDecoder._instance = this;
			if (MediaDecoder.OnInstantiated != null)
				MediaDecoder.OnInstantiated(this);
			_initialized = false;

			_factory = new SharpDX.DXGI.Factory();
			_device = new SharpDX.Direct3D11.Device(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport, _levels);

			using (DeviceMultithread mt = _device.QueryInterface<DeviceMultithread>())
			{
				mt.SetMultithreadProtected(true);
			}

			using (SharpDX.DXGI.Device1 dxgiDevice = _device.QueryInterface<SharpDX.DXGI.Device1>())
			{
				dxgiDevice.MaximumFrameLatency = 1;
			}

			_dxgiManager = new DXGIDeviceManager();
		}


		public static string[] SupportedVideoFileExtensions => new string[] { "mp4", "m4v", "mov", "avi", "wmv" };
		public static string[] SupportedImageFileExtensions => new string[] { "png", "jpg", "jpeg" };
		public static string[] SupportedFileExtensions => SupportedVideoFileExtensions.Concat(SupportedImageFileExtensions).ToArray();


		public static bool CheckExtension(string extension)
		{
			if (string.IsNullOrWhiteSpace(extension))
				return false;
			string e = extension.TrimStart('.').ToLowerInvariant();
			return SupportedVideoFileExtensions.Contains(e) || SupportedImageFileExtensions.Contains(e);
		}

		public static string ExtensionsFilter()
		{
			var allExtensions = string.Join("; ", SupportedFileExtensions.ToList().ConvertAll(ext => $"*.{ext}"));
			var videoExtensions = string.Join("; ", SupportedVideoFileExtensions.ToList().ConvertAll(ext => $"*.{ext}"));
			var imageExtensions = string.Join("; ", SupportedImageFileExtensions.ToList().ConvertAll(ext => $"*.{ext}"));

			return $"All supported formats|{allExtensions}|Video ({videoExtensions})|{videoExtensions}|Panorama ({imageExtensions})|{imageExtensions}";
		}

		public void Init()
		{
			LastError = null;
			lock(criticalSection)
			{
				if (_initialized) return;

				if (!mediaManagerStarted)
				{
					MediaManager.Startup();
					mediaManagerStarted = true;
				}
				using (var mediaEngineFactory = new MediaEngineClassFactory())
				using (MediaEngineAttributes attr = new MediaEngineAttributes())
				{
					_dxgiManager.ResetDevice(_device);

					attr.VideoOutputFormat = (int)SharpDX.DXGI.Format.B8G8R8A8_UNorm;
					attr.DxgiManager = _dxgiManager;

					_mediaEngine = new MediaEngine(mediaEngineFactory, attr, MediaEngineCreateFlags.None);
				}

				if (!string.IsNullOrWhiteSpace(_audioEndpointId))
				{
					Tools.AudioDeviceManager.ApplyEndpointToMediaEngine(_mediaEngine.NativePointer, _audioEndpointId);
				}

				_mediaEngine.PlaybackEvent += (playEvent, param1, param2) =>
				{
					switch (playEvent)
					{
						case MediaEngineEvent.CanPlay:
							Console.WriteLine(string.Format("CAN PLAY {0}, {1}", param1, param2));
							Ready = true;
							Duration = _mediaEngineEx.Duration;
							OnReady(Duration);
							if (CurrentServiceResult == null)
								log.Error("Media playback aborted just after play?");
							else
								NewFileIsPlaying(CurrentServiceResult, CurrentServiceResult.BestSupportedStream, Duration);
							break;

						case MediaEngineEvent.TimeUpdate:
							var engineForTime = _mediaEngineEx;
							if (engineForTime == null || !_initialized || !isPlaying)
								break;

							double engineTime;
							try
							{
								engineTime = engineForTime.CurrentTime;
							}
							catch (Exception)
							{
								break;
							}

							if (_hasPendingSeek)
							{
								long elapsedTicks = DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastSeekUtcTicks);
								double pendingTarget = BitConverter.Int64BitsToDouble(Interlocked.Read(ref _pendingSeekTargetBits));
								bool recentlySought = elapsedTicks < TimeSpan.FromMilliseconds(1000).Ticks;
								if (recentlySought && pendingTarget >= 0.0 && Math.Abs(engineTime - pendingTarget) > 1.5)
								{
									// Ignore stale pre-seek or intermediate TimeUpdate while a rapid multi-seek is catching up
									break;
								}
								_hasPendingSeek = false;
							}

							CurrentPosition = engineTime;
							OnTimeUpdate(engineTime);
							break;

						case MediaEngineEvent.Seeked:
							var engineForSeek = _mediaEngineEx;
							if (engineForSeek != null && _initialized && isPlaying)
							{
								try
								{
									double currentEngineTime = engineForSeek.CurrentTime;
									double pendingTarget = BitConverter.Int64BitsToDouble(Interlocked.Read(ref _pendingSeekTargetBits));
									long elapsedTicks = DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastSeekUtcTicks);
									if (_hasPendingSeek && pendingTarget >= 0.0 &&
										elapsedTicks < TimeSpan.FromSeconds(2.0).Ticks &&
										Math.Abs(currentEngineTime - pendingTarget) > 1.0)
									{
										engineForSeek.CurrentTime = pendingTarget;
									}
									else
									{
										_hasPendingSeek = false;
									}
								}
								catch (Exception) { }
								manualRender = true;
							}
							break;

						case MediaEngineEvent.Error:
							Console.WriteLine(string.Format("ERROR {0}, {1}", param1, param2));
							Console.WriteLine(((MediaEngineErr)param1).ToString());
							LastError = new Error() { major = param1, minor = param2 };

							Stop(true);
							OnError(LastError);
							break;

						case MediaEngineEvent.Abort:
							Console.WriteLine(string.Format("ABORT {0}, {1}", param1, param2));
							OnAbort();
							Stop();
							break;

						case MediaEngineEvent.Ended:
                            Console.WriteLine(string.Format("ENDED {0}, {1}", param1, param2));
							OnEnded();
							break;
                        case MediaEngineEvent.BufferingStarted:
                            OnBufferingStarted();
                            break;
                        case MediaEngineEvent.BufferingEnded:
                            OnBufferingEnded();
                            break;
                        case MediaEngineEvent.FormatChange:
							Console.WriteLine("[!!!] FormatChange " + formatCounter++);
							formatChangePending = true;
							break;

                        case MediaEngineEvent.Playing:
                            OnPlay?.Invoke();
                            break;

                    }
                };

				_mediaEngineEx = _mediaEngine.QueryInterface<MediaEngineEx>();
				_mediaEngineEx.EnableWindowlessSwapchainMode(true);
				_mediaEngineEx.Loop = _loop;

				_initialized = true;
			}
		}

		//private MediaSource CreateMediaSource(string sURL)
		//{
		//	SourceResolver sourceResolver = new SourceResolver();
		//	ComObject comObject;
		//	comObject = sourceResolver.CreateObjectFromURL(sURL, SourceResolverFlags.MediaSource | SourceResolverFlags.ContentDoesNotHaveToMatchExtensionOrMimeType);
		//	return comObject.QueryInterface<MediaSource>();
		//}

		//public static Guid ToGuid(long value)
		//{
		//	byte[] guidData = new byte[16];
		//	Array.Copy(BitConverter.GetBytes(value), guidData, 8);
		//	return new Guid(guidData);
		//}


		public Texture2D CreateTexture(SharpDX.Direct3D11.Device _device, int width, int height)
		{
			Texture2DDescription frameTextureDescription = new Texture2DDescription()
			{
				Width = width,
				Height = height,
				MipLevels = 1,
				ArraySize = 1,
				Format = Format.B8G8R8X8_UNorm,
				Usage = ResourceUsage.Default,
				SampleDescription = new SampleDescription(1, 0),
				BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
				CpuAccessFlags = CpuAccessFlags.None,
				OptionFlags = ResourceOptionFlags.Shared
			};

			var tex = new Texture2D(_device, frameTextureDescription);

			//tex.Disposing += (s, e) => log.Info("Disposing");
			//tex.Disposed += (s, e) => log.Info("Disposed"); ;

			return tex;
		}

		public Texture2D CreateTexture(SharpDX.Direct3D11.Device _device, int width, int height, DataRectangle dataRect)
		{
			Texture2DDescription frameTextureDescription = new Texture2DDescription()
			{
				Width = width,
				Height = height,
				MipLevels = 1,
				ArraySize = 1,
				Format = Format.B8G8R8X8_UNorm,
				Usage = ResourceUsage.Default,
				SampleDescription = new SampleDescription(1, 0),
				BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
				CpuAccessFlags = CpuAccessFlags.None,
				OptionFlags = ResourceOptionFlags.Shared
			};
			var tex = new Texture2D(_device, frameTextureDescription, dataRect);

			//tex.Disposing += (s, e) => log.Info("Disposing");
			//tex.Disposed += (s, e) => log.Info("Disposed");;

			return tex;
		}


		public class ClipCoords {

			public float w;
			public float h;
			public float l;
			public float t;

			public VideoNormalizedRect NormalizedSrcRect
			{
				get
				{
					return new VideoNormalizedRect()
					{
						Left = l,
						Top = t,
						Bottom = t + h,
						Right = l + w
					};
				}
			}

			internal SharpDX.Rectangle DstRect(int pxw, int pxh)
			{
				return new SharpDX.Rectangle(0, 0, Width(pxw), Height(pxh));
			}

			internal int Width(int pxw)
			{
				return (int)(w * pxw);
			}

			internal int Height(int pxh)
			{
				return (int)(h * pxh);
			}

			internal SharpDX.Rectangle SrcRect(int pxw, int pxh)
			{
				return new SharpDX.Rectangle((int)(l * pxw), (int)(t * pxh), (int)(w * pxw), (int)(h * pxh));
			}


			internal System.Drawing.Rectangle SrcRectSystemDrawing(int pxw, int pxh)
			{
				return new System.Drawing.Rectangle((int)(l * pxw), (int)(t * pxh), (int)(w * pxw), (int)(h * pxh));
			}

		}
		private static void GetTextureClipCoordinates(VideoMode mode, out ClipCoords texL, out ClipCoords texR)
		{
			switch (mode)
			{
				case VideoMode.Autodetect:
					throw new ArgumentException("Autodetect should not reach here");
				case VideoMode.Mono:
					texL = new ClipCoords() { w = 1, h = 1f, l = 0, t = 0 };
					texR = null;
					break;
				case VideoMode.SideBySide:
					texL = new ClipCoords() { w = 0.5f, h = 1, l = 0, t = 0 };
					texR = new ClipCoords() { w = 0.5f, h = 1, l = 0.5f, t = 0 };
					break;
				case VideoMode.SideBySideReversed:
					texL = new ClipCoords() { w = 0.5f, h = 1, l = 0.5f, t = 0 };
					texR = new ClipCoords() { w = 0.5f, h = 1, l = 0, t = 0 };
					break;
				case VideoMode.TopBottom:
					texL = new ClipCoords() { w = 1f, h = 0.5f, l = 0, t = 0 };
					texR = new ClipCoords() { w = 1f, h = 0.5f, l = 0, t = 0.5f };
					break;
				case VideoMode.TopBottomReversed:
					texL = new ClipCoords() { w = 1f, h = 0.5f, l = 0, t = 0.5f };
					texR = new ClipCoords() { w = 1f, h = 0.5f, l = 0, t = 0 };
					break;
				default:
					throw new Exception();
			}
		}


		public void Play()
		{
			lock(criticalSection)
			{
				if (isPlaying) return;
				if (IsDisplayingStaticContent) return;


				if (!_initialized) return;
				if (!Ready) return;

				var hasVideo = _mediaEngine.HasVideo();
				var hasAudio = _mediaEngine.HasAudio();

				if (hasVideo)
				{
					//SharpDX.Win32.Variant variant;
					//_mediaEngineEx.GetStreamAttribute(1, MediaTypeAttributeKeys.FrameSize.Guid, out variant);

					int w, h;
					_mediaEngineEx.GetNativeVideoSize(out w, out h);

					int cx, cy;
					_mediaEngineEx.GetVideoAspectRatio(out cx, out cy);
					var s3d = _mediaEngineEx.IsStereo3D;
					var sns = _mediaEngineEx.NumberOfStreams;
				}

				_mediaEngineEx.Play();
                ShellViewModel.SendEvent("moviePlaybackStarted");
                //_mediaEngineEx.Volume = 0.2;
                isPlaying = true;
			}

			Task t = Task.Factory.StartNew(() =>
			{
				_rendering = true;
				int w = -1, h = -1;
				while (_mediaEngine != null && isPlaying)
				{
					bool shouldSleep = false;
					lock (criticalSection)
					{
						if (_mediaEngine == null || _mediaEngineEx == null || !isPlaying)
							break;

						if(w <= 0 || h <= 0 || formatChangePending)
						{
							_mediaEngineEx.GetNativeVideoSize(out w, out h);
							if (w > 0 && h > 0)
							{
								_lastMediaWidth = w;
								_lastMediaHeight = h;
							}
						}

						if(formatChangePending && w > 0 && h > 0)
						{
							formatChangePending = false;
							CurrentMode = ParseStereoMode(LoadedStereoMode, w, h);
							ChangeFormat(CurrentMode, w, h);
						}

						if(!textureReleased && w > 0 && h > 0)
						{
							if (!_mediaEngine.IsPaused || manualRender)
							{
								if(formatCounter == 0)
									Console.WriteLine("[!!!] Render " + formatCounter++);
								long lastTs = ts;
								bool forceRender = manualRender;
								bool result = _mediaEngine.OnVideoStreamTick(out ts);
								if ((!result || ts < 0) && !forceRender)
								{
									shouldSleep = true;
								}
								else if (ts != lastTs || forceRender)
								{
									manualRender = false;
									try {
										ClipCoords texL;
										ClipCoords texR;
										GetTextureClipCoordinates(CurrentMode, out texL, out texR);

										_mediaEngine.TransferVideoFrame(TextureL, texL.NormalizedSrcRect, texL.DstRect(w,h), null);
										if(texR != null)
											_mediaEngine.TransferVideoFrame(TextureR, texR.NormalizedSrcRect, texR.DstRect(w,h), null);
									}
									catch (Exception exc)
									{
										log.Error(exc, "Playback exception");
									}
								}
								else
								{
									shouldSleep = true;
								}
							}
							else
							{
								shouldSleep = true;
							}
						}
						else
						{
							shouldSleep = true;
						}
					}

					if (shouldSleep)
						Thread.Sleep(1);
				}

				waitForRenderingEnd.Set();
				_rendering = false;
			});
			
		}

		private VideoMode ParseStereoMode(VideoMode setStereoMode, float w, float h)
		{
			if (setStereoMode == VideoMode.Autodetect)
			{
				if (w <= 0 || h <= 0)
					return VideoMode.SideBySide;

				float videoAspect = w / h;
				VideoMode mode = (videoAspect < 1.3f) ? VideoMode.TopBottom : VideoMode.SideBySide;
				log.Info($"Autodetected stereoscopy={mode} ({w}x{h}, aspect={videoAspect})");
				return mode;
			}
			else
			{
				return setStereoMode;
			}
		}


		/// <summary>
		/// Called by a IContentUpdatableFromMediaEngine when it wants
		/// to have their content updated
		/// </summary>
		/// <param name="obj"></param>
		/// <returns>true if content request was successful, false if media pipeline was not yet initialized.</returns>
		public bool ContentRequested(IContentUpdatableFromMediaEngine obj)
		{
			lock (criticalSection)
			{
				if(contentChangeDelegate == null)
				{
					//log.Error("ContentRequested without a delegate?");
					return false;
				}

				contentChangeDelegate(obj);
				var proj = ResolveProjection(Projection, CurrentMode, _lastMediaWidth, _lastMediaHeight);
				obj.SetProjection(proj);
			}

			return true;
		}

		/// <summary>
		/// When content changes (media, format, stereoscopy etc. changed), this event is called.
		/// Objects implementing IContentUpdatableFromMediaEngine should react and call ContentRequested.
		/// </summary>
		public event Action OnContentChanged = delegate { };


		Action<IContentUpdatableFromMediaEngine> contentChangeDelegate = null;


		/// <summary>
		/// Mark content as changed and notify whoever listens.
		/// </summary>
		/// <param name="contentChangeDelegate"></param>
		private void ContentChanged(Action<IContentUpdatableFromMediaEngine> contentChangeDelegate)
		{
			lock (criticalSection)
			{
				this.contentChangeDelegate = contentChangeDelegate;
				OnContentChanged?.Invoke();
			}
		}


		/// <summary>
		/// Should be executed in locked context
		/// </summary>
		/// <param name="w"></param>
		/// <param name="h"></param>
		private void ChangeFormat(VideoMode mode, int w, int h)
		{
			Texture2D tempL = TextureL;
			Texture2D tempR = TextureR;

			textureReleased = true;

			ClipCoords texL, texR;
			GetTextureClipCoordinates(mode, out texL, out texR);
			TextureL = CreateTexture(_device, texL.Width(w), texL.Height(h));
			TextureR = (texR != null) ? CreateTexture(_device, texR.Width(w), texR.Height(h)) : null;

			textureReleased = false;

			//OnReleaseTexture();
			OnFormatChanged(TextureL, TextureR);
			if (waitForFormatChange) waitForFormatChange = false;


			ContentChanged(r => r.ReceiveTextures(TextureL, TextureR));

			tempL?.Dispose();
			tempR?.Dispose();
		}


		public void Pause()
		{
			if (IsDisplayingStaticContent) return;
			if (isPlaying)
			{
				var engine = _mediaEngine;
				if (engine != null)
				{
					try
					{
						if (!engine.IsPaused)
							engine.Pause();
					}
					catch (Exception) { }
				}
			}
		}

		public void Unpause()
		{
			if (IsDisplayingStaticContent) return;
			if (isPlaying)
			{
				var engine = _mediaEngine;
				if (engine != null)
				{
					try
					{
						if (engine.IsPaused)
							engine.Play();
					}
					catch (Exception) { }
				}
			}
		}

		public void TogglePause()
		{
			if (IsDisplayingStaticContent) return;
			if (isPlaying)
			{
				if (IsPaused) Unpause();
				else Pause();
			}
		}

		public void SetAudioOutputEndpoint(string endpointId)
		{
			_audioEndpointId = string.IsNullOrWhiteSpace(endpointId) ? null : endpointId;
			var engine = _mediaEngine;
			if (engine != null && !engine.IsDisposed)
			{
				Tools.AudioDeviceManager.ApplyEndpointToMediaEngine(engine.NativePointer, _audioEndpointId);
			}
		}

		public void SetVolume(double volume)
		{
            //if(IsPlaying)
            //{
            if (_mediaEngine != null)
            {
                try
                {
                    if(!_mediaEngine.IsDisposed)
                        _mediaEngine.Volume = volume;
                }
                catch (Exception) { }
            }
			//}
		}

		public static double ClampSeekTime(double time, double duration)
		{
			if (double.IsNaN(time) || double.IsInfinity(time))
				return 0.0;
			if (time < 0.0)
				time = 0.0;
			if (!double.IsNaN(duration) && !double.IsInfinity(duration) && duration > 0.0 && time > duration)
				time = duration;
			return time;
		}

		public double SeekRelative(double deltaSeconds)
		{
			if (IsDisplayingStaticContent || double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds))
				return CurrentPosition;

			double pendingTarget = BitConverter.Int64BitsToDouble(Interlocked.Read(ref _pendingSeekTargetBits));
			double baseTime = (_hasPendingSeek && pendingTarget >= 0.0) ? pendingTarget : CurrentPosition;
			double target = ClampSeekTime(baseTime + deltaSeconds, Duration);
			Seek(target);
			return CurrentPosition;
		}

		public void Seek(double time)
		{
			if (IsDisplayingStaticContent) return;
			if (double.IsNaN(time) || double.IsInfinity(time)) return;
			if (isPlaying)
			{
				var engineEx = _mediaEngineEx;
				if (engineEx == null || !_initialized)
					return;

				double clampedTime = ClampSeekTime(time, Duration);
				CurrentPosition = clampedTime;
				Interlocked.Exchange(ref _pendingSeekTargetBits, BitConverter.DoubleToInt64Bits(clampedTime));
				Interlocked.Exchange(ref _lastSeekUtcTicks, DateTime.UtcNow.Ticks);
				_hasPendingSeek = true;
				manualRender = true;

				try
				{
					// Seek immediately without locking criticalSection or waiting for IsSeeking / buffering
					engineEx.CurrentTime = clampedTime;
				}
				catch (Exception)
				{
				}

				ShellViewModel.SendEvent("movieSeek", clampedTime);
				OnSeek?.Invoke(clampedTime);
				OnTimeUpdate?.Invoke(clampedTime);
			}
		}
		
		public void Stop(bool force = false)
		{
			textureReleased = true;

			if (!force && !IsDisplayingStaticContent)
			{
				if (!_initialized) return;
				if (!isPlaying) return;
			}

			waitForRenderingEnd.Reset();
			isPlaying = false;
			
			if (_rendering) {
				waitForRenderingEnd.WaitOne(1000);
			}

			Ready = false;

			bool lockTaken = false;
			try
			{
				Monitor.TryEnter(criticalSection, force ? 500 : Timeout.Infinite, ref lockTaken);
				var engineEx = _mediaEngineEx;
				var engine = _mediaEngine;
				_mediaEngineEx = null;
				_mediaEngine = null;

				if (engineEx != null)
				{
					try
					{
						engineEx.Shutdown();
						engineEx.Dispose();
						engine?.Dispose();
					}
					catch (Exception)
					{
					}
				}

				bitmap?.Dispose();
				bitmap = null;

				TextureL?.Dispose();
				TextureR?.Dispose();

				TextureL = null;
				TextureR = null;

				_newFileIsPlayingReported = false;

				if (lockTaken)
				{
					ContentChanged(r => r.ClearContent());
				}

				_initialized = false;
			}
			finally
			{
				if (lockTaken)
					Monitor.Exit(criticalSection);
			}

            OnStop?.Invoke();
        }

		//private string _fileName;
  //      public string FileName { get { return _fileName; } }

		public void LoadMedia(ServiceResult serviceResult)
		{
			CurrentServiceResult = serviceResult;
			_lastMediaWidth = 0;
			_lastMediaHeight = 0;
			Interlocked.Exchange(ref _pendingSeekTargetBits, BitConverter.DoubleToInt64Bits(-1.0));
			Interlocked.Exchange(ref _lastSeekUtcTicks, 0);
			_hasPendingSeek = false;
			LoadedStereoMode = serviceResult.stereoscopy;
			Projection = serviceResult.projection;

			string fileName = serviceResult.BestSupportedStream;

			Console.WriteLine("Load media: " + fileName);

			textureReleased = true;
			waitForFormatChange = true;
			Stop();
			while (_initialized == true)
			{
				Console.WriteLine("Cannot init when initialized");
				Stop(true);
				Thread.Sleep(5);
			}

			bitmap?.Dispose();
			bitmap = null;

			// reset the event
			_newFileIsPlayingReported = false;

			switch (serviceResult.contentType)
			{
				case ServiceResult.ContentType.video:
					Init();
					
					formatCounter = 0;
					textureReleased = true;
					waitForFormatChange = true;
					_mediaEngineEx.Source = fileName;
					_mediaEngineEx.Preload = MediaEnginePreload.Automatic;
					_mediaEngineEx.Load();
					break;

				case ServiceResult.ContentType.image:
					Task.Factory.StartNew(() => 
					{
						byte[] contentSource;

						if (fileName.StartsWith("http://", StringComparison.InvariantCultureIgnoreCase) || fileName.StartsWith("https://", StringComparison.InvariantCultureIgnoreCase))
						{
							RestClient client = new RestClient(fileName);
							IRestRequest request = new RestRequest(Method.GET);
							IRestResponse response = client.Execute(request);

							log.Info("Loading remote image file via HTTP: " + fileName);

							if (response.ErrorException != null)
							{
								log.Error(response.ErrorException, "Error while downloading");
								OnError(new Error() { major = (long)MediaEngineErr.Network });
								return;
							}

							if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 400) {
								log.Error($"Bad HTTP status: {response.StatusCode} ({(int)response.StatusCode})");
								OnError(new Error() { major = (long)MediaEngineErr.Network });
								return;
							}

							contentSource = response.RawBytes;
							if (contentSource == null || contentSource.Length == 0)
							{
								log.Error("Downloaded image content is empty");
								OnError(new Error() { major = (long)MediaEngineErr.Network });
								return;
							}
						}
						else
						{
							log.Info("Loading local image file: " + fileName);
							try
							{
								contentSource = File.ReadAllBytes(fileName);
							}
							catch(Exception e)
							{
								log.Error(e, "Error while loading from disk");
								OnError(new Error() { major = (long)MediaEngineErr.SourceNotSupported });
								return;
							}
						}

						try
						{
							lock (criticalSection)
							{
								bitmap?.Dispose();
								using (var stream = new MemoryStream(contentSource))
								using (var decoded = new Bitmap(stream))
								{
									// Clone so the bitmap does not depend on the disposed MemoryStream
									bitmap = new Bitmap(decoded);
								}
							}
						}
						catch(Exception e)
						{
							log.Error(e, "Error while decoding");
							OnError(new Error() { major = (long)MediaEngineErr.SourceNotSupported });
							return;
						}

						ContentChanged(renderer =>
						{
							if(bitmap == null)
							{
								renderer.ClearContent();
								log.Error("Content changed while loading, aborted.");
								return;
							}

							var w = bitmap.Width;
							var h = bitmap.Height;
							_lastMediaWidth = w;
							_lastMediaHeight = h;
							log.Info($"Loaded image of size {w}x{h} from {fileName}");
							CurrentMode = ParseStereoMode(LoadedStereoMode, w, h);

							ClipCoords texL, texR;
							GetTextureClipCoordinates(CurrentMode, out texL, out texR);

							renderer.ReceiveBitmap(bitmap, texL, texR);
						});

						OnReady?.Invoke(-1);

						NewFileIsPlaying(serviceResult, fileName, -1);
					});


					break;
			}
		}


		//public MediaEngine Engine { get { return this._mediaEngine; } }


		public void Shutdown()
		{
			Stop(true);
			if (mediaManagerStarted)
			{
				MediaManager.Shutdown();
				mediaManagerStarted = false;
			}
			_dxgiManager?.Dispose();
			_dxgiManager = null;
			_factory?.Dispose();
			_factory = null;
			_device?.Dispose();
			_device = null;
		}

	}
}

