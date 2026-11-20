using Bivrost.Log;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Bivrost.Bivrost360Player.Streaming
{
    public class YoutubeParser : ServiceParser
    {
		private static Logger log = new Logger("YoutubeParser");


        public override string ServiceName
        {
            get { return "YouTube"; }
        }

        public override bool CanParse(string uri)
        {
            if (uri == null)
                return false;
            return
                Regex.IsMatch(uri, @"^(https?://)?(www\.youtube\.com/watch|youtube\.com/watch)/?\?(.+&)?v=[a-zA-Z0-9_-]+($|&.*|#.*)", RegexOptions.IgnoreCase)
                ||
                Regex.IsMatch(uri, @"^(https?://)?youtu\.be/[a-zA-Z0-9_-]+($|\?.*|&.*|#.*)", RegexOptions.IgnoreCase);
        }


        public override ServiceResult Parse(string uri)
        {
            string dataFoler = Logic.LocalDataDirectory;
            string ytdl = Path.Combine(dataFoler, "youtube-dl.exe");

            if (!File.Exists(ytdl) || !IsYoutubeUpToDate())
            {
				if (!YoutubeUpdate()) throw new StreamNotSupported("Youtube-dl update failed or declined.");
			}

            int code;
            string jsonStr = YoutubeDL("--ignore-config --no-playlist --no-call-home --dump-json \"" + uri.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"", out code);
            if (code != 0 || string.IsNullOrWhiteSpace(jsonStr))
                throw new StreamParsingFailed($"youtube-dl failed with exit code {code}");

            JObject json;
            try
            {
                json = JObject.Parse(jsonStr);
            }
            catch (Exception ex)
            {
                throw new StreamParsingFailed("Failed to parse youtube-dl JSON output.", ex);
            }

            List<AudioStream> audioStreams = new List<AudioStream>();
            List<VideoStream> videoStreams = new List<VideoStream>();

            JArray formats = json["formats"] as JArray;
            if (formats == null)
                throw new StreamParsingFailed("youtube-dl output contains no formats.");

            foreach (JObject format in formats.OfType<JObject>())
            {
                try
                {
                    int format_id;
                    int.TryParse((string)format["format_id"], out format_id);
                    int preference = (int?)format["preference"] ?? 1000000;
                    string ext = (string)format["ext"];
                    string vcodec = (string)format["vcodec"];
                    string acodec = (string)format["acodec"];
                    if (acodec == "none")
                        acodec = null;
                    int? abr = (int?)format["abr"];

                    // is video or video+audio
                    if (vcodec != "none")
                    {
                        int? width = (int?)format["width"];
                        int? height = (int?)format["height"];
                        int? tbr = (int?)format["tbr"];
                        int? vbr = (int?)format["vbr"];

                        Container vc;
                        switch (ext?.ToLowerInvariant())
                        {
                            case "mp4":
                                vc = Container.mp4;
                                break;
                            case "webm":
                                vc = Container.webm;
                                break;
                            case "3gp":
                                vc = Container._3gp;
                                break;
                            case "ogv":
                            case "ogg":
                                vc = Container.ogg;
                                break;
                            default:
                                log.Info("Skipping unknown YouTube video extension: " + ext);
                                continue;
                        }

                        int computedQuality = (height ?? 0) * 1000 + (vbr ?? tbr ?? 0);
                        videoStreams.Add(new VideoStream()
                        {
                            audioBitrate = abr,
                            bitrate = vbr.HasValue ? vbr : tbr,
                            container = vc,
                            hasAudio = acodec != null,
                            width = width,
                            height = height,
                            url = (string)format["url"],
                            quality = computedQuality > 0 ? computedQuality : format_id
                        });
                    }
                    // is audio
                    else
                    {
                        if (acodec == null)
                            continue;
                        audioStreams.Add(new AudioStream()
                        {
                            url = (string)format["url"],
                            bitrate = abr,
                            quality = abr ?? format_id
                        });
                    }
                }
                catch(Exception e) when (!(e is StreamException))
                {
					log.Error("Failed to parse some of youtube-dl downloaded metadata:\n"+format.ToString()+"\n\n"+e);
                }

            }

            string originalURL = (string)json["webpage_url"] ?? uri;
            return new ServiceResult(originalURL, ServiceName, URIToMediaId(originalURL))
            {
                description = (string)json["description"],
                audioStreams = audioStreams,
                videoStreams = videoStreams,
                stereoscopy = VideoMode.Autodetect,
                title = (string)json["fulltitle"],
                projection = ProjectionMode.Sphere
            };
        }

        string latestOnlineVersion = null;
        string latestDownloadedVersion = null;
        private bool IsYoutubeUpToDate()
        {
            if (latestOnlineVersion == null)
            {
                RestClient client = new RestClient("https://yt-dl.org/latest/version");
                IRestRequest request = new RestRequest();
                IRestResponse response = client.Execute(request);
                if (response.ErrorException == null && response.StatusCode == System.Net.HttpStatusCode.OK && !string.IsNullOrWhiteSpace(response.Content))
                {
                    latestOnlineVersion = response.Content.Trim();
                }
            }

			logger.Info("Latest online version: " + latestOnlineVersion);

            if (!string.IsNullOrWhiteSpace(latestOnlineVersion) && string.IsNullOrEmpty(latestDownloadedVersion))
            {
                int code;
                string version = YoutubeDL("--version", out code);
				if (code == 0 && !string.IsNullOrEmpty(version))
					latestDownloadedVersion = version;
            }

			logger.Info("Latest downloaded version: " + latestDownloadedVersion);

			return !string.IsNullOrEmpty(latestOnlineVersion) && latestOnlineVersion == latestDownloadedVersion;
        }


		Logger logger = new Logger("youtube-parser");
        private bool YoutubeUpdate()
        {
            var app = System.Windows.Application.Current;
            if (app == null)
            {
                logger.Error("Cannot prompt for youtube-dl download without an active WPF Application.");
                return false;
            }

			var confirm = app.Dispatcher.Invoke(() =>
			{
				var decision = System.Windows.MessageBox.Show(
					app.MainWindow,
					"A third-party application youtube-dl " +
					"needs to be downloaded from the internet " +
					"and run for this feature to work.\n\n" +
					"Do you want to continue?",

					"Youtube-dl update",
					System.Windows.MessageBoxButton.OKCancel,

					System.Windows.MessageBoxImage.Warning,
					System.Windows.MessageBoxResult.Cancel
				);
				return decision == System.Windows.MessageBoxResult.OK;
			});

			if(!confirm)
			{
				logger.Error("Youtube-dl download declined.");
				return false;
			}
			logger.Info("Youtube-dl download approved.");

			try
			{
                string dataFoler = Logic.LocalDataDirectory;
				// disable SSL3, because github/s3 will break
				System.Net.ServicePointManager.SecurityProtocol &= ~System.Net.SecurityProtocolType.Ssl3;

				RestClient client = new RestClient("https://yt-dl.org/latest/youtube-dl.exe") { FollowRedirects = true, MaxRedirects = 10 };
                IRestRequest request = new RestRequest();
                IRestResponse response = client.Execute(request);
                if (response.ErrorException == null && response.StatusCode == System.Net.HttpStatusCode.OK && response.RawBytes != null && response.RawBytes.Length > 0)
                {
                    string ytdl = Path.Combine(dataFoler, "youtube-dl.exe");
                    string tmpYtdl = ytdl + ".tmp";
                    File.WriteAllBytes(tmpYtdl, response.RawBytes);
                    if (File.Exists(ytdl))
                        File.Delete(ytdl);
                    File.Move(tmpYtdl, ytdl);
                    log.Info("youtube-dl update success");
                    latestDownloadedVersion = null;
					return true;
                }
                else
                {
                    log.Error($"youtube-dl update failed: {response.StatusCode} {response}");
                }
            }
            catch (Exception exc)
            {
                log.Error(exc, "youtube-dl update failed");
            }

			return false;
        }


        private static string YoutubeDL(string arguments, out int exitCode)
        {
            try
            {
                log.Info("Executing youtube-dl with arguments: " + arguments);
                string dataFoler = Logic.LocalDataDirectory;
                string ytdl = Path.Combine(dataFoler, "youtube-dl.exe");

                using (Process process = new Process())
                {
                    ProcessStartInfo start = new ProcessStartInfo(ytdl);
                    start.Arguments = arguments;
                    start.CreateNoWindow = true;
                    start.RedirectStandardOutput = true;
                    start.RedirectStandardError = true;
                    start.UseShellExecute = false;

                    process.StartInfo = start;
                    StringBuilder output = new StringBuilder();
                    StringBuilder error = new StringBuilder();
                    process.OutputDataReceived += (sender, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                    process.ErrorDataReceived += (sender, e) => { if (e.Data != null) error.AppendLine(e.Data); };

                    process.Start();

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    process.WaitForExit();
                    exitCode = process.ExitCode;

                    log.Info("Youtube-dl exit code: " + exitCode);

                    if (!string.IsNullOrWhiteSpace(error.ToString()))
                        log.Error("Youtube-dl errors: " + error.ToString());

                    return output.ToString().Trim();
                }

            }
            catch (Exception exc)
            {
                throw new StreamParsingFailed("youtube-dl execution failed", exc);
            }
        }

    }
}
