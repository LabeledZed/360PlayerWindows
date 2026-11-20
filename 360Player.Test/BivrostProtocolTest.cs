using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using Bivrost.Bivrost360Player.Streaming;

namespace Bivrost.Bivrost360Player.Test
{
	[TestClass]
	public class BivrostProtocolTest : StreamingTest<BivrostProtocolParser>
	{
		protected override string[] CorrectUris
		{
			get
			{
				return new string[] {
					"bivrost:https%3A%2F%2Fexample.com%2Fvideo.mp4",
					"bivrost:https%3A%2F%2Fexample.com%2Fvideo.mp4?stereoscopy=side-by-side&projection=equirectangular",
					"bivrost:https%3A%2F%2Fexample.com%2Fvideo.webm?alt=https%3A%2F%2Fexample.com%2Fvideo.mp4%3Ftoken%3D123&stereoscopy=side-by-side-reversed"
				};
			}
		}


		protected override string[] IncorrectUris
		{
			get
			{
				return new string[] {
					"http://example.com/video.mp4",
					"https://example.com/video.mp4",
					"C:\\video.mp4",
					"not a uri",
					"",
					null
				};
			}
		}

		[TestMethod]
		public void ParsesStereoscopyAndPrefersMp4Alt()
		{
			var result = parser.Parse("bivrost:https%3A%2F%2Fexample.com%2Fvideo.webm?alt=https%3A%2F%2Fexample.com%2Fvideo.mp4%3Ftoken%3D123&stereoscopy=side-by-side-reversed");
			Assert.AreEqual(VideoMode.SideBySideReversed, result.stereoscopy);
			Assert.AreEqual(ProjectionMode.Sphere, result.projection);
			Assert.AreEqual(1, result.videoStreams.Count);
			Assert.AreEqual("https://example.com/video.mp4?token=123", result.videoStreams[0].url);
			Assert.AreEqual(Container.mp4, result.videoStreams[0].container);
		}
	}
}
