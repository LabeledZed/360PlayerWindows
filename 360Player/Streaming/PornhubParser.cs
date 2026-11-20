using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System;
using System.Dynamic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Bivrost.Bivrost360Player.Streaming
{
	public class PornhubParser : ServiceParser
	{

        public override string ServiceName
        {
            get { return "PornHub"; }
        }

        public override bool CanParse(string uri)
		{
			if (uri == null) return false;
			return Regex.IsMatch(uri, @"^(https?://)?(www\.)?pornhub\.com/view_video\.php\?.+", RegexOptions.IgnoreCase);
		}

		internal ProjectionMode ParseProjection(int projection)
		{
			switch (projection)
			{
				case 1: throw new StreamNotSupported("DomeEquidistant projection not supported yet: " + projection);
				case 2: return ProjectionMode.Sphere;
				case 3: return ProjectionMode.Dome;
				default: throw new StreamParsingFailed("Projection unknown: " + projection);
			}
		}

		internal VideoMode ParseStereo(int stereo)
		{
			switch (stereo)
			{
				case 1: return VideoMode.SideBySide;
				case 2: return VideoMode.TopBottom;
				case 3: return VideoMode.SideBySideReversed;
				case 4: return VideoMode.TopBottomReversed;
				default:
					throw new StreamParsingFailed("Stereoscopy unknown: " + stereo);
			}
		}


		public override ServiceResult Parse(string uri)
		{
			var result = new ServiceResult(uri, ServiceName, URIToMediaId(uri));
			string html = HTTPGetString(uri);

			var match = Regex.Match(html, @"^\s*vrProps\s*=\s*(.+),\s*$", RegexOptions.Multiline);



			if (match.Captures.Count == 0)
				throw new StreamNotSupported("This movie is not VR enabled: " + uri);

			string vrPropsString=match.Groups[1].Captures[0].Value;
			JObject vrProps;
			try
			{
				vrProps = JObject.Parse(vrPropsString);
			}
			catch (Exception ex)
			{
				throw new StreamParsingFailed("Failed to parse VR properties.", ex);
			}

			if (!((bool?)vrProps["enabled"] ?? false))
				throw new StreamNotSupported("This movie is not VR enabled: " + uri);

			result.projection = ParseProjection((int)(vrProps["projection"] ?? 0));
			result.stereoscopy = ((bool?)vrProps["stereoSrc"] ?? false) ? ParseStereo((int)(vrProps["stereoType"] ?? 0)) : VideoMode.Mono;

			var titleMatch = Regex.Match(html, @"<title>(.+?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
			if (titleMatch.Success)
				result.title = titleMatch.Groups[1].Value.Trim();

			var flashvars = Regex.Match(html, @"^\s*var\s+flashvars_\d+\s*=\s*(.+);\s*$", RegexOptions.Multiline);
			if (flashvars.Success && flashvars.Captures.Count > 0)
			{
				var json = flashvars.Groups[1].Captures[0].Value;
				try
				{
					JObject message = JObject.Parse(json);
					JArray mediaDefinitions = message["mediaDefinitions"] as JArray;
					if (mediaDefinitions != null)
					{
						foreach (JObject md in mediaDefinitions.OfType<JObject>())
						{
							string videoUrl = (string)md["videoUrl"];
							if (string.IsNullOrWhiteSpace(videoUrl))
								continue;

							var stream = new VideoStream()
							{
								container = Container.mp4,
								url = videoUrl,
								hasAudio = true,
								quality = 0,
								width = 0,
								height = 0
							};
							string quality = md["quality"]?.ToString()?.ToLowerInvariant();
							switch (quality)
							{
								case "720":
									stream.width = 1280;
									stream.height = 720;
									stream.quality = 1;
									break;
								case "1080":
									stream.width = 1920;
									stream.height = 1080;
									stream.quality = 2;
									break;
								case "2k":
									stream.width = 2048;
									stream.height = 1024;
									stream.quality = 3;
									break;
								case "4k":
									stream.width = 3840;
									stream.height = 2160;
									stream.quality = 4;
									break;
							}
							result.videoStreams.Add(stream);
						}
					}
				}
				catch (Exception ex)
				{
					throw new StreamParsingFailed("Failed to parse PornHub flashvars JSON.", ex);
				}
			}


			if (result.videoStreams.Count == 0)
				throw new StreamParsingFailed("No videos found");
			
			return result;
		}
	}
}
