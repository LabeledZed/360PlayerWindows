#if FEATURE_REMOTE_CONTROL

using Bivrost.Log;
using Caliburn.Micro;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bivrost.Bivrost360Player
{
	public partial class ShellViewModel
	{
		private static bool IsRemoteControlEnabled = false;


		Logger remoteLogger = new Logger("remote");


		Nancy.Hosting.Self.NancyHost remoteControl;
		float remoteTime = 0;



		private string ResolveRemoteMoviePath(string requestedPath, out string displayName)
		{
			displayName = "";
			if (string.IsNullOrWhiteSpace(requestedPath))
				return null;

			string baseDir = Logic.Instance.settings.RemoteControlMovieDirectory;
			if (string.IsNullOrWhiteSpace(baseDir) || !Directory.Exists(baseDir))
				return null;

			// Normalize both forward and backward slashes and extract file name only
			string fileName = Path.GetFileName(requestedPath.Replace('/', Path.DirectorySeparatorChar));
			displayName = fileName ?? "";
			if (string.IsNullOrWhiteSpace(fileName) || fileName == "." || fileName == "..")
				return null;

			string fullBase = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			string fullCandidate = Path.GetFullPath(Path.Combine(baseDir, fileName));
			if (!fullCandidate.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
				return null;

			return File.Exists(fullCandidate) ? fullCandidate : null;
		}

		private void EnableRemoteControl()
		{
			if (IsRemoteControlEnabled) return;
			if (!Features.RemoteEnabled) return;

			Task.Factory.StartNew((System.Action)(() =>
			{
				try
				{
					remoteControl = ApiServer.InitNancy(apiServer =>
					{
						remoteLogger.Info("got unity init, device_id=" + ApiServer.device_id + " movies=[\n" + string.Join(",\n", ApiServer.movies ?? new string[0]) + "]");
						remoteLogger.Info("init complete");
					});
				}
				catch (Exception ex)
				{
					remoteLogger.Error(ex, "Failed to start remote control HTTP server");
					return;
				}

#region GearVR slave app

				ApiServer.OnBackPressed += () =>
				{
					remoteLogger.Info("back pressed");
				};

				ApiServer.OnStateChange += (state) =>
				{
					remoteLogger.Info("state changed: " + state);

					switch (state)
					{
						case ApiServer.State.pause:
							Execute.OnUIThreadAsync(() =>
							{
								if (IsPlaying && !IsPaused)
								{
									Pause();
									_mediaDecoder.Seek(remoteTime);
								}
							});
							break;

						case ApiServer.State.play:
							Execute.OnUIThreadAsync((System.Action)(() =>
							{
								if (!_ready)
								{
									if (!string.IsNullOrWhiteSpace(SelectedFileName))
										this.OpenURI(SelectedFileName);
								}
								else if (!IsPlaying)
								{
									Play();
								}
								else if (IsPaused)
								{
									UnPause();
								}
							}));
							break;

						case ApiServer.State.stop:
							Execute.OnUIThreadAsync(() =>
							{
								if (IsPlaying)
									Stop();
							});
							break;
					}
				};

				ApiServer.OnConfirmPlay += (path) =>
				{
					remoteLogger.Info("path = " + path);
					string fullPath = ResolveRemoteMoviePath(path, out string remoteFile);
					if (fullPath != null)
					{
						Execute.OnUIThreadAsync(() =>
						{
							try
							{
								var result = Streaming.StreamingFactory.Instance.GetStreamingInfo(fullPath);
								if (result != null)
								{
									SelectedServiceResult = result;
									IsFileSelected = true;
								}
							}
							catch (Exception ex)
							{
								remoteLogger.Error(ex, "Failed to resolve remote movie");
							}
						});
					}
					else
					{
						Execute.OnUIThreadAsync(() =>
						{
							IsFileSelected = false;
							SelectedServiceResult = null;
							NotificationCenter.PushNotification(new NotificationViewModel("Requested file \"" + remoteFile + "\" not found in video library.",
								() =>
								{
									try { System.Diagnostics.Process.Start(Logic.Instance.settings.RemoteControlMovieDirectory); } catch (Exception) { }
								}, "open folder"));
						});
					}
				};

				ApiServer.OnPos += (euler, t) =>
				{
				};

				ApiServer.OnPosQuaternion += (quat, t) =>
				{
					remoteTime = (float)(t * MaxTime);
					var scene = DXCanvas?.Scene as Scene;
					if (scene != null)
					{
						scene.SetLook(quat);
					}
				};



				ApiServer.OnInfo += (msg) =>
				{
					remoteLogger.Info("[remote] msg = " + msg);
				};

#endregion


#region Remote controll app - control API

				ApiServer.CommandLoadHandler = (movie, autoplay) =>
				{
					remoteLogger.Info("[remote] path = " + movie);
					string fullPath = ResolveRemoteMoviePath(movie, out string remoteFile);
					if (fullPath != null)
					{
						bool loaded = false;
						Execute.OnUIThread(() =>
						{
							try
							{
								var result = Streaming.StreamingFactory.Instance.GetStreamingInfo(fullPath);
								if (result != null)
								{
									ResetPlayback();
									LoadMedia(result, autoplay);
									loaded = true;
								}
							}
							catch (Exception ex)
							{
								remoteLogger.Error(ex, "Failed to load remote movie");
							}
						});
						return loaded;
					}
					else
					{
						Execute.OnUIThreadAsync(() =>
						{
							IsFileSelected = false;
							SelectedServiceResult = null;
							NotificationCenter.PushNotification(new NotificationViewModel("Requested file \"" + remoteFile + "\" not found in video library.",
								() =>
								{
									try { System.Diagnostics.Process.Start(Logic.Instance.settings.RemoteControlMovieDirectory); } catch (Exception) { }
								}, "open folder"));
						});
						return false;
					}
				};

				ApiServer.CommandMoviesHandler = () =>
				{
					List<string> movies = new List<string>();
					var dir = Logic.Instance.settings.RemoteControlMovieDirectory;
					if (Directory.Exists(dir))
					{
						try
						{
							var files = Directory.GetFiles(dir);
							foreach (string file in files)
							{
								if (MediaDecoder.CheckExtension(Path.GetExtension(file)))
									movies.Add(Path.GetFileName(file));
							}
						}
						catch (Exception exc)
						{
							remoteLogger.Error(exc, "Failed to enumerate remote movies");
						}
					}
					return movies.ToArray();
				};

				ApiServer.CommandPauseHandler = () =>
				{
					bool result = false;
					Execute.OnUIThread(() =>
					{
						if (IsPlaying && !IsPaused)
						{
							Pause();
							result = true;
						}
					});
					return result;
				};

				ApiServer.CommandPlayingHandler = () =>
				{
					var info = new ApiServer.PlayingInfo()
					{
						is_playing = false,
						movie = "",
						quat_x = 0f,
						quat_y = 0f,
						quat_z = 0f,
						quat_w = 0f,
						t = 0,
						tmax = 0
					};

					if (IsFileSelected) info.movie = SelectedFileName ?? "";
					if (IsPlaying)
					{
						info.is_playing = !IsPaused;
						var scene = this.DXCanvas?.Scene as Scene;
						if (scene != null)
						{
							var q = scene.GetCurrentLook();
							info.quat_x = q.X;
							info.quat_y = q.Y;
							info.quat_z = q.Z;
							info.quat_w = q.W;
						}
						info.t = (float)TimeValue;
						info.tmax = (float)MaxTime;
					}

					return info;
				};

				ApiServer.CommandSeekHandler = (time) =>
				{
					bool result = false;
					Execute.OnUIThread(() =>
					{
						if (IsPlaying)
						{
							TimeValue = time;
							result = true;
						}
					});
					return result;
				};

				ApiServer.CommandStopAndResetHandler = () =>
				{
					bool result = false;
					Execute.OnUIThread(() =>
					{
						if (IsPlaying)
						{
							Stop();
							result = true;
						}
					});
					return result;
				};

				ApiServer.CommandUnpauseHandler = () =>
				{
					bool result = false;
					Execute.OnUIThread(() =>
					{
						if (IsPlaying && IsPaused)
						{
							UnPause();
							result = true;
						}
						else if (!IsPlaying && IsFileSelected)
						{
							PlayPause();
							result = true;
						}
					});
					return result;
				};


#endregion

				IsRemoteControlEnabled = true;

			}));

		}



		private static void RemoteControlSendEvent(string name, object eventParameter = null)
		{
			if (IsRemoteControlEnabled)
			{
				string arg = JsonConvert.SerializeObject(eventParameter);
				ApiServer.AddOutgoingEvent(name, arg);
			}
		}

		//Player events:
		//- movie loaded
		//- movie playback started
		//- movie paused
		//- movie stopped
		//- movie ended
		//- movie seek
		//- playback error
		//- volume changed
		//- full screen state changed
		//- headset connected
		//- headset error
		//- zoom changed
		//- projection changed (gnomic / stereographic)
		//- notification pushed
		//- quit


	}
}

#endif