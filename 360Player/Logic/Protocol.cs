using System;
using System.Collections.Generic;
using System.Web;

namespace Bivrost
{

    public class Protocol
    {
        public enum Stereoscopy
        {
            autodetect, // guess by filename tags and media ratio (see: Media preparation guide)
	        mono, // whole image used
	        side_by_side, // image for left eye is on the left half, and right on the right half of the media
	        side_by_side_reversed, // image for right eye is on the left half, and left on the right half of the media
	        top_and_bottom, // the left eye is the top half of the image, the right one in the bottom half
 	        top_and_bottom_reversed // the left eye is the bottom half of the image, the right one in the top half
        }

        public enum Projection {
            equirectangular
        };

        public List<string> urls = new List<string>();
        public Stereoscopy? stereoscopy;
        public Projection? projection;
        public bool? autoplay;
        public bool? loop;
        public string version = null;

        protected Protocol(string link)
        {

			Uri uri = new Uri(link);
            var query=HttpUtility.ParseQueryString(uri.Query);

            if (!uri.Scheme.Equals("bivrost", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("scheme is not bivrost");

            string mainUrl = uri.AbsolutePath;
            if (!string.IsNullOrWhiteSpace(uri.Host))
                mainUrl = uri.Host + mainUrl;
            mainUrl = HttpUtility.UrlDecode(mainUrl);
            if (!string.IsNullOrWhiteSpace(mainUrl))
                urls.Add(mainUrl);

            foreach (string key in query.AllKeys)
            {
                if (key == null) continue;
                string[] values = query.GetValues(key);
                if (values == null) continue;

                foreach (string val in values)
                {
                    switch (key.ToLowerInvariant())
                    {
                        case "stereoscopy":
                            if (!string.IsNullOrEmpty(val) && Enum.TryParse(val.Replace("-", "_"), true, out Stereoscopy parsedStereo))
                                stereoscopy = parsedStereo;
                            break;

                        case "projection":
                            if (!string.IsNullOrEmpty(val) && Enum.TryParse(val.Replace("-", "_"), true, out Projection parsedProj))
                                projection = parsedProj;
                            break;

                        case "autoplay":
                            if (bool.TryParse(val, out bool parsedAutoplay))
                                autoplay = parsedAutoplay;
                            break;

                        case "loop":
                            if (bool.TryParse(val, out bool parsedLoop))
                                loop = parsedLoop;
                            break;

                        case "version":
                            version = val;
                            break;

                        case "alt":
                        case "alt0":
                        case "alt1":
                        case "alt2":
                        case "alt3":
                        case "alt4":
                        case "alt5":
                        case "alt6":
                        case "alt7":
                        case "alt8":
                        case "alt9":
                            if (!string.IsNullOrWhiteSpace(val))
                                urls.Add(val);
                            break;

                        default:
                            Console.WriteLine("unknown argument in url: " + key + "=" + val);
                            break;
                    }
                }
            }
        }

        public static Protocol Parse(string link)
        {
            return new Protocol(link);
        }


        public override string ToString()
        {
            return string.Format(
                "[Bivrost.Protocol stereoscopy={0} projection={1} autoplay={2} loop={3} version={4}] urls=",
                stereoscopy,
                projection,
                autoplay,
                loop,
                version
           )+string.Join(", ", urls);
        }
    }

}