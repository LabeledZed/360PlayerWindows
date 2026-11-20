using Bivrost;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Bivrost.Bivrost360Player.Streaming
{

	public class BivrostProtocolParser : ServiceParser
	{
        public override string ServiceName
        {
            get { return "Bivrost protocol"; }
        }

        public override bool CanParse(string url)
		{
			if (string.IsNullOrWhiteSpace(url))
				return false;
			if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
				return false;
			return uri.Scheme.Equals("bivrost", StringComparison.OrdinalIgnoreCase);
		}

		public override ServiceResult Parse(string uri)
		{
			var protocol = Protocol.Parse(uri);
			VideoMode stereoscopy;
			switch (protocol.stereoscopy)
			{
				default:
				case Protocol.Stereoscopy.autodetect: stereoscopy = VideoMode.Autodetect; break;
				case Protocol.Stereoscopy.mono: stereoscopy = VideoMode.Mono; break;
				case Protocol.Stereoscopy.side_by_side: stereoscopy = VideoMode.SideBySide; break;
				case Protocol.Stereoscopy.side_by_side_reversed: stereoscopy = VideoMode.SideBySideReversed; break;
				case Protocol.Stereoscopy.top_and_bottom: stereoscopy = VideoMode.TopBottom; break;
				case Protocol.Stereoscopy.top_and_bottom_reversed: stereoscopy = VideoMode.TopBottomReversed; break;
			}
			bool Loop = protocol.loop.HasValue ? protocol.loop.Value : false;       // unused
			string videoUrl = protocol.urls.FirstOrDefault((u) =>
			{
				if (string.IsNullOrWhiteSpace(u)) return false;
				string p = ExtractInspectionPath(u);
				return p.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
					|| p.EndsWith(".avi", StringComparison.OrdinalIgnoreCase);
			});
			if (string.IsNullOrWhiteSpace(videoUrl) && protocol.urls.Count > 0)
				videoUrl = protocol.urls[0];

			if (string.IsNullOrWhiteSpace(videoUrl))
				throw new StreamParsingFailed("no video stated in protocol");

			ProjectionMode projection;
			switch (protocol.projection)
			{
				default:
				case Protocol.Projection.equirectangular: projection = ProjectionMode.Sphere; break;
			}

			string title;
			try
			{
				title = System.IO.Path.GetFileName(ExtractInspectionPath(videoUrl));
			}
			catch (ArgumentException)
			{
				title = null;
			}
			if (string.IsNullOrWhiteSpace(title))
				title = uri;

			return new ServiceResult(uri, ServiceName, URIToMediaId(uri))
			{
				projection = projection,
				stereoscopy = stereoscopy,
				title = title,
				videoStreams = new List<VideoStream>()
				{
					new VideoStream()
					{
						url = videoUrl,
						hasAudio = true, // TODO
                        container = GuessContainerFromExtension(videoUrl)
                    }
				}
			};
		}
	}
}
