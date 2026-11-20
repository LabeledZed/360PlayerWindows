using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bivrost.Bivrost360Player.Streaming
{

    public class URLParser : ServiceParser
    {
        public override string ServiceName
        {
            get { return "plain URL"; }
        }

        public override bool CanParse(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                return false;
            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        }

        public override ServiceResult Parse(string uri)
        {
			// TODO: check media type

			// TODO: if html, try to parse it?
			// TODO: search for bivrost-360webplayer tags

			var container = GuessContainerFromExtension(uri);
			string title;
			try
			{
				string fileName = Path.GetFileName(ExtractInspectionPath(uri));
				title = string.IsNullOrEmpty(fileName) ? uri : Uri.UnescapeDataString(fileName);
			}
			catch (Exception)
			{
				title = uri;
			}
			var projection = GuessProjectionFromFileName(uri);
			if (projection == ProjectionMode.Autodetect)
				projection = ProjectionMode.Dome;

			var stereoscopy = GuessStereoscopyFromFileName(uri);
			if (stereoscopy == VideoMode.Autodetect)
				stereoscopy = VideoMode.SideBySide;

			return new ServiceResult(uri, ServiceName, URIToMediaId(uri))
            {
                projection = projection,
				stereoscopy = stereoscopy,
                videoStreams = new List<VideoStream>()
                {
                    new VideoStream()
                    {
                        url = uri,
                        hasAudio = true, // TODO, currently guessed
                        container = container
                    }
                },
				contentType = GuessContentTypeFromContainer(container),
				title = title
			};
        }
    }
}
