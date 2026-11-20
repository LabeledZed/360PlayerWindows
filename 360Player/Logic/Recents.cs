using Bivrost.Log;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Bivrost.Bivrost360Player
{
	public class Recents
	{
		private static Logger log = new Logger("Recents");


		public class RecentsFormat1 : List<string> { }

		public class RecentsFormat2 : List<RecentsFormat2.RecentElement>
		{
			public class RecentElement
			{
				public string title;
				public string uri;
			}


			public static implicit operator RecentsFormat2(RecentsFormat1 rf1)
			{
				RecentsFormat2 rf2 = new RecentsFormat2();
				if (rf1 == null) return rf2;
				rf2.AddRange(rf1.ConvertAll(uri => 
				{
					log.Info("Upgrading recents from v1");
					string title = null;
					try
					{
						string path = uri;
						if (Uri.TryCreate(uri, UriKind.Absolute, out Uri parsed) && !parsed.IsFile)
							path = parsed.AbsolutePath;
						title = Path.GetFileName(path);
					}
					catch (ArgumentException) { }
					if (string.IsNullOrWhiteSpace(title))
						title = uri;
					return new RecentElement() { uri = uri, title = title };
				}));
				return rf2;
			}
		}


		static readonly object recentsLock = new object();
		static RecentsFormat2 recentFiles = null;


		public static RecentsFormat2 RecentFiles
		{
			get
			{
				lock (recentsLock)
				{
					if (recentFiles == null)
						Load();
					return recentFiles;
				}
			}
		}

		static void Save()
		{
			try
			{
				string dataFoler = Logic.LocalDataDirectory;
				if (string.IsNullOrWhiteSpace(dataFoler)) return;
				if (!Directory.Exists(dataFoler))
					Directory.CreateDirectory(dataFoler);
				string recentConfig = Path.Combine(dataFoler, "recents");
				File.WriteAllText(recentConfig, JsonConvert.SerializeObject(recentFiles), Encoding.UTF8);
			}
			catch (Exception exc) {
				log.Error("Recents save: " + exc.Message);
			}
		}

		static void Load()
		{
			try
			{
				string dataFoler = Logic.LocalDataDirectory;
				if (string.IsNullOrWhiteSpace(dataFoler))
				{
					recentFiles = new RecentsFormat2();
					return;
				}
				if (!Directory.Exists(dataFoler))
					Directory.CreateDirectory(dataFoler);
				string recentConfig = Path.Combine(dataFoler, "recents");

				if (File.Exists(recentConfig))
				{
					string recentsSerialized = File.ReadAllText(recentConfig, Encoding.UTF8);

					try
					{
						recentFiles = JsonConvert.DeserializeObject<RecentsFormat2>(recentsSerialized);
					}
					// this probably isn't in the newest format, try the older one
					catch(JsonSerializationException)
					{
						recentFiles = JsonConvert.DeserializeObject<RecentsFormat1>(recentsSerialized);
						Save();
					}
				}
				else
				{
					log.Info("Creating new recents file");
					recentFiles = new RecentsFormat2();
				}
			}
			catch (Exception exc) {
				log.Error(exc, "Recents error");
			}
			finally
			{
				if (recentFiles == null)
					recentFiles = new RecentsFormat2();
			}
		}

		public static void AddRecent(Streaming.ServiceResult result)
		{
			if (result == null || string.IsNullOrWhiteSpace(result.originalURL))
				return;

			lock (recentsLock)
			{
				var list = RecentFiles;

				// remove duplicate
				list.RemoveAll(f => f.uri == result.originalURL);

				// trim oldest entries so at most 9 remain (10 after insert)
				while (list.Count > 9)
					list.RemoveAt(list.Count - 1);

				string title = result.title;
				list.Insert(0, new RecentsFormat2.RecentElement() { title = title, uri = result.originalURL });
				Save();
			}
		}

	}
}
