#if FEATURE_BROWSER_PLUGINS

using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bivrost.Bivrost360Player
{
    public static class BrowserPluginManagement
    {
        public static string chromePath = "";
        public static string firefoxPath = "";

		public static void CheckForBrowsers()
		{
			var settings = Logic.Instance.settings;

			if (string.IsNullOrWhiteSpace(Logic.LocalDataDirectory)) return;

			if (!settings.BrowserPluginQuestionShown)
			{
				Caliburn.Micro.Execute.OnUIThreadAsync(() =>
				{
					var mainWindow = System.Windows.Application.Current?.MainWindow;
					var result = mainWindow != null
						? System.Windows.MessageBox.Show(mainWindow, "Install browser integration extensions?", "Browser addons", System.Windows.MessageBoxButton.YesNoCancel)
						: System.Windows.MessageBox.Show("Install browser integration extensions?", "Browser addons", System.Windows.MessageBoxButton.YesNoCancel);

					if (result == System.Windows.MessageBoxResult.Yes)
					{
						settings.BrowserPluginAccepted = true;
						settings.BrowserPluginQuestionShown = true;
						settings.Save();

						Task.Factory.StartNew(() =>
						{
							try
							{
								if (CheckFirefox())
									InstallFirefoxPlugin();
							}
							catch (Exception exc) { Bivrost.Log.LoggerManager.Error(exc, "Firefox plugin install failed"); }

							try
							{
								if (CheckChrome())
									InstallChromePlugin();
							}
							catch (Exception exc) { Bivrost.Log.LoggerManager.Error(exc, "Chrome plugin install failed"); }
						});
					}
					else if (result == System.Windows.MessageBoxResult.No)
					{
						settings.BrowserPluginAccepted = false;
						settings.BrowserPluginQuestionShown = true;
						settings.Save();
					}
				});
			}
			else
			{
				Task.Factory.StartNew(() =>
				{
					if (settings.BrowserPluginAccepted)
					{
						try
						{
							if (CheckFirefox())
								InstallFirefoxPlugin();
						}
						catch (Exception exc) { Bivrost.Log.LoggerManager.Error(exc, "Firefox plugin check/install failed"); }

						try
						{
							if (CheckChrome())
								InstallChromePlugin();
						}
						catch (Exception exc) { Bivrost.Log.LoggerManager.Error(exc, "Chrome plugin check/install failed"); }
					}
				});
			}
		}

		public static bool CheckFirefox()
        {
            return CheckForApp("firefox.exe", out firefoxPath);
        }

        public static bool CheckChrome()
        {
            return CheckForApp("chrome.exe", out chromePath);
        }

        private static bool CheckForApp(string appName, out string appPath)
        {
            bool appFound = false;
            appPath = "";

            using (RegistryKey myApps = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths"))
            {
                if (myApps != null)
                {
                    using (RegistryKey appKey = myApps.OpenSubKey(appName))
                    {
                        string val = appKey?.GetValue("") as string;
                        if (!string.IsNullOrEmpty(val))
                        {
                            appPath = val;
                            appFound = true;
                        }
                    }
                }
            }

            if (!appFound)
            {
                using (RegistryKey localApps = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths"))
                {
                    if (localApps != null)
                    {
                        using (RegistryKey appKey = localApps.OpenSubKey(appName))
                        {
                            string val = appKey?.GetValue("") as string;
                            if (!string.IsNullOrEmpty(val))
                            {
                                appPath = val;
                                appFound = true;
                            }
                        }
                    }
                }
            }

			return appFound;
		}

        public static void RunBrowser(string appPath)
        {
            ProcessStartInfo proc = new ProcessStartInfo();
            proc.FileName = appPath;

            Process.Start(proc);
        }

        public static void InstallFirefoxPlugin()
        {
			string tempDir = null;
			try
			{
				string ffPlugin = @"https://download.bivrost360.com/ff-plugin/firefox-connector-extension@bivrost360.com.xpi";

				var client = new RestSharp.RestClient(ffPlugin);
				var request = new RestSharp.RestRequest("");
				var response = client.Execute(request);
				if (response.ErrorException == null && response.StatusCode == System.Net.HttpStatusCode.OK && response.RawBytes != null && response.RawBytes.Length > 0)
				{
					string pluginDir = Path.Combine(Logic.LocalDataDirectory, "ffplugin");
					tempDir = Path.Combine(Logic.LocalDataDirectory, "ffplugin_tmp_" + Guid.NewGuid().ToString("N"));
					Directory.CreateDirectory(tempDir);

					var destCopy = Path.Combine(tempDir, "extension.xpi");
					File.WriteAllBytes(destCopy, response.RawBytes);
					ZipFile.ExtractToDirectory(destCopy, tempDir);
					File.Delete(destCopy);

					if (Directory.Exists(pluginDir))
						Directory.Delete(pluginDir, true);
					Directory.Move(tempDir, pluginDir);
					tempDir = null;

					string extensionID = "firefox-connector-extension@bivrost360.com";

					using (RegistryKey firefoxKey = Registry.CurrentUser.OpenSubKey(@"Software\Mozilla\Firefox", true))
					{
						if (firefoxKey != null)
						{
							using (RegistryKey extensionsKey = firefoxKey.OpenSubKey(@"Extensions", true) ?? firefoxKey.CreateSubKey("Extensions"))
							{
								extensionsKey?.SetValue(extensionID, pluginDir);
							}
						}
					}
				}
			}
			catch (Exception exc)
			{
				Bivrost.Log.LoggerManager.Error(exc, "InstallFirefoxPlugin failed");
			}
			finally
			{
				if (tempDir != null && Directory.Exists(tempDir))
				{
					try { Directory.Delete(tempDir, true); } catch (Exception) { }
				}
			}
        }


        public static void InstallChromePlugin()
        {
            try
			{
				using (RegistryKey chromeKey = Registry.CurrentUser.OpenSubKey(@"Software\Google\Chrome", true))
				{
					if (chromeKey != null)
					{
						using (RegistryKey extensionsKey = chromeKey.OpenSubKey(@"Extensions", true) ?? chromeKey.CreateSubKey("Extensions"))
						using (RegistryKey pluginKey = extensionsKey?.CreateSubKey("goloffhdkngdkldehjjijobinpfcjpbj"))
						{
							pluginKey?.SetValue("update_url", "https://clients2.google.com/service/update2/crx");
						}
					}
				}
			}
			catch (Exception exc)
			{
				Bivrost.Log.LoggerManager.Error(exc, "InstallChromePlugin failed");
			}
        }
    }
}

#endif
