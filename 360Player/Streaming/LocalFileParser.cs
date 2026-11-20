using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Bivrost.Bivrost360Player.Streaming
{

	public class LocalFileParser : ServiceParser
	{
		public override bool CanParse(string url)
		{
			if (string.IsNullOrWhiteSpace(url))
				return false;
			try
			{
				// this will throw an ArgumentException if path is incorrect
				string urlFull = Path.GetFullPath(url);

				// if url is not rooted, it is invalid
				return Path.IsPathRooted(url);
			}
			catch (ArgumentException) { return false; }
			catch (NotSupportedException) { return false; }
			catch (PathTooLongException) { return false; }
			catch (SecurityException) { return false; }
		}


		public override ServiceResult Parse(string uri)
		{
			string path = Path.GetFullPath(uri);
			if (!File.Exists(path))
				throw new StreamNetworkFailure("File not found", path);

            FileInfo fileInfo = new FileInfo(path);

            int consideredBytes = 1048576;
            if (fileInfo.Length < consideredBytes)
                consideredBytes = (int)fileInfo.Length;

            byte[] data = new byte[consideredBytes];
            using (FileStream fs = fileInfo.OpenRead())
            {
                int totalRead = 0;
                while (totalRead < consideredBytes)
                {
                    int read = fs.Read(data, totalRead, consideredBytes - totalRead);
                    if (read <= 0)
                        break;
                    totalRead += read;
                }
            }

            byte[] hash;
            using (SHA1 sha = SHA1.Create())
            {
                hash = sha.ComputeHash(data);
            }
            string mediaId = $"sha1+len:{string.Concat(Array.ConvertAll(hash, x => x.ToString("x2")))}+{fileInfo.Length}";

			var container = GuessContainerFromExtension(path);
			var projection = GuessProjectionFromFileName(path);
			var stereoscopy = GuessStereoscopyFromFileName(path);
			VR180MeshData customMesh = null;

			if (container == Container.mp4)
			{
				VR180MeshParser.InspectMp4File(path, data, ref projection, ref stereoscopy, out customMesh);
			}

			// Default mode: _LR 180° Equirectangular Side-by-Side
			if (projection == ProjectionMode.Autodetect)
			{
				projection = ProjectionMode.Dome;
			}
			if (stereoscopy == VideoMode.Autodetect)
			{
				stereoscopy = VideoMode.SideBySide;
			}

			return new ServiceResult(path, ServiceName, mediaId)
			{
				projection = projection,
				stereoscopy = stereoscopy,
				CustomMesh = customMesh,
				videoStreams = new List<VideoStream>()
				{
					new VideoStream()
					{
						url = path,
						hasAudio = true, // TODO
                        container = container
                    }
				},
				title = Path.GetFileName(path),
				contentType = GuessContentTypeFromContainer(container)
			};
		}

		public override string ServiceName
        {
            get { return "Local file"; }
        }
    }
}
