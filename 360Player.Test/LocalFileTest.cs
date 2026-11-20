using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using Bivrost.Bivrost360Player.Streaming;

namespace Bivrost.Bivrost360Player.Test
{
	[TestClass]
	public class LocalFileTest : StreamingTest<LocalFileParser>
	{
		private string _tempDir;
		private string _tempVideoAuto;
		private string _tempVideoMono;
		private string _tempVideoSbs;

		[TestInitialize]
		public void SetupTempFiles()
		{
			_tempDir = Path.Combine(Path.GetTempPath(), "Bivrost360Test_" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_tempDir);
			_tempVideoAuto = Path.Combine(_tempDir, "EHF2016.mp4");
			_tempVideoMono = Path.Combine(_tempDir, "sample_mono.mp4");
			_tempVideoSbs = Path.Combine(_tempDir, "sample_sbs.mp4");

			File.WriteAllBytes(_tempVideoAuto, new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 });
			File.WriteAllBytes(_tempVideoMono, new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 });
			File.WriteAllBytes(_tempVideoSbs, new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 });
		}

		[TestCleanup]
		public void CleanupTempFiles()
		{
			if (!string.IsNullOrEmpty(_tempDir) && Directory.Exists(_tempDir))
			{
				try { Directory.Delete(_tempDir, true); } catch (Exception) { }
			}
		}

		protected override string[] CorrectUris
		{
			get
			{
				return new string[] {
					_tempVideoAuto,
					_tempVideoAuto.Replace('\\', '/'),
					_tempVideoMono,
					_tempVideoSbs
				};
			}
		}

		protected override string[] IncorrectUris
		{
			get
			{
				return new string[] {
					"file:///c/film.mp4",
					"local-file.mp4",
					"../relative-file.mp4",
					"../dir/relative-file-in-dir.mp4",
					"http://url.example.com/sdf",
					"0:\\film-z-dysku-numerycznego.mp4",
					"",
					null
				};
			}
		}

		[TestMethod]
		public void CanParseSyntaxForNonExistentRootedPaths()
		{
			Assert.IsTrue(parser.CanParse("D:\\filmy360\\tego-filmu-nie-ma.mp4"));
			Assert.IsTrue(parser.CanParse("D:/filmy360/tego-filmu-nie-ma.mp4"));
			Assert.IsTrue(parser.CanParse("D:\\filmy360\\tutaj\\są spacje i polskie\\znaki.mp4"));
			Assert.IsTrue(parser.CanParse("\\\\Network\\shares\\work-too.mp4"));
			Assert.IsTrue(parser.CanParse("C:\\film.mp4"));
			Assert.IsTrue(parser.CanParse("C:just-drive.mp4"));
		}

		[TestMethod]
		public void CanParseStream()
		{
			var result = parser.Parse(_tempVideoAuto);
			Assert.IsTrue(result.videoStreams.Count > 0, "no video streams");
			Assert.IsFalse(result.videoStreams.Exists(vs => vs.url == null), "some url contains nulls");
			Assert.AreEqual(ProjectionMode.Dome, result.projection);
			Assert.AreEqual(VideoMode.SideBySide, result.stereoscopy);

			var monoResult = parser.Parse(_tempVideoMono);
			Assert.AreEqual(VideoMode.Mono, monoResult.stereoscopy);

			var sbsResult = parser.Parse(_tempVideoSbs);
			Assert.AreEqual(VideoMode.SideBySide, sbsResult.stereoscopy);
		}
	}
}
