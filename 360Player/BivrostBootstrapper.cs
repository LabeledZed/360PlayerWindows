using Caliburn.Micro;
using Microsoft.Win32;
using Bivrost.Bivrost360Player.Tools;
using System;
using System.Deployment.Application;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Bivrost.Log;

namespace Bivrost.Bivrost360Player
{
	public class BivrostBootstrapper : BootstrapperBase
	{
		// APP GUID 150A3700-F729-4FE2-B61B-C3C716530C58
		// Single instance lock
		static Mutex mutex = new Mutex(false, "{41F397A7-D196-4C6F-B75A-616069D45DAD}");
		static bool ownMutex = false;
		private static Logger logger;

		internal static void ReleaseSingleInstanceMutex()
		{
			if (!ownMutex) return;
			try
			{
				mutex.ReleaseMutex();
				ownMutex = false;
			}
			catch (Exception exc)
			{
				System.Diagnostics.Debug.WriteLine("Releasing mutex: " + exc.Message);
			}
		}

		[DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)]
		private static extern IntPtr LoadLibrary([MarshalAs(UnmanagedType.LPStr)]string lpFileName);


		private bool IsWindowsTooOld()
		{
			var ver = System.Environment.OSVersion;
			if (ver.Platform != PlatformID.Win32NT) return true; // Not windows NT (98/Me/95?)
			if (ver.Version.Major < 6) return true;
			if (ver.Version.Major == 6 && ver.Version.Minor < 3) return true; // Vista, 2008, 7 or 8
			return false;
		}


		public BivrostBootstrapper()
		{
			if (IsWindowsTooOld())
			{
				MessageBox.Show($"Windows Version '{Environment.OSVersion.VersionString}' is not supported by BIVROST® 360Player. Please upgrade.", "Out of date Windows warning", MessageBoxButton.OK, MessageBoxImage.Warning);
			}
			Initialize();
		}



		protected override void OnStartup(object sender, System.Windows.StartupEventArgs e)
		{

			Application.ShutdownMode = ShutdownMode.OnMainWindowClose;
			//AddPathPatch();

			logger = new Logger("bootstrap");

			Application.Current.DispatcherUnhandledException += (_s, _e) => logger.Fatal(_e.Exception, "unhandled application exception");
			AppDomain.CurrentDomain.UnhandledException += (_s, _e) => logger.Fatal(_e.ExceptionObject as Exception, "unhandled application domain exception");


			LoggerManager.RegisterListener(new TraceLogListener(false));

			//LoggerManager.RegisterListener(new WindowsEventLogListener());
			

			System.Windows.Forms.Application.EnableVisualStyles();

			if (!System.Diagnostics.Debugger.IsAttached && ApplicationDeployment.IsNetworkDeployed)
			{
				//logger.Info("Copying appref and associating icons and file extensions");
				Logic.Prepare(ApplicationDeployment.CurrentDeployment.DataDirectory + Path.DirectorySeparatorChar);

				Task.Factory.StartNew(() =>
				{
					CopyApplicationReference();
					SetAddRemoveProgramsIcon();
					AssociateFileExtensions("rundll32.exe dfshim.dll,ShOpenVerbShortcut " + Logic.LocalDataDirectory + "Bivrost360Player.appref-ms" + "|%1");
				});
			}
			else
			{
				//logger.Info("Not copying appref and associating icons and file extensions - but making a stub, because this app is not network deployed");
				{
                    // Check for application identity (UWP)
                    string path;
                    try
                    {
                        path = Windows.Storage.ApplicationData.Current.LocalFolder.Path + Path.DirectorySeparatorChar;
                    }
                    catch (Exception)
                    {
                        // If no application identity, use application directory (desktop/portable app)
                        string assemblyPath = new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath;
                        path = Path.GetDirectoryName(assemblyPath) + Path.DirectorySeparatorChar;
                    }
                    Logic.Prepare(path);
				}
			}

			LoggerManager.RegisterListener(new TextFileLogListener(Logic.LocalDataDirectory));
			logger.Info("Registered all listeners");
			


			string[] args = Environment.GetCommandLineArgs();
			if(args.Length > 0)
				logger.Info("Received command line arguments: " + string.Join(", ", args));

			try
			{
				if (AppDomain.CurrentDomain.SetupInformation.ActivationArguments?.ActivationData != null)
				{
					args = AppDomain.CurrentDomain.SetupInformation.ActivationArguments.ActivationData;
					logger.Info("Forwarding activation arguments as command line: " + string.Join(", ", args));
				}
				
			}
			catch (Exception)
			{
			}

			bool acquiredMutex = false;
			try
			{
				acquiredMutex = mutex.WaitOne(TimeSpan.Zero, false);
			}
			catch (AbandonedMutexException)
			{
				acquiredMutex = true;
			}

			ownMutex = acquiredMutex;

			if (ownMutex || System.Diagnostics.Debugger.IsAttached)
			{
				logger.Info("Initiating player window...");

				if(args != null && args.Length > 0)
				{
					var lastArgument = args[args.Length - 1];
					if (!string.IsNullOrWhiteSpace(lastArgument) && !lastArgument.ToLowerInvariant().EndsWith(".exe") && !lastArgument.ToLowerInvariant().EndsWith(".application"))
					{
						logger.Info("Forwarding argument from command line:");
						ShellViewModel.FileFromArgs = lastArgument;
					}
				}
				
				DisplayRootViewFor<ShellViewModel>();
				
			}
			else
			{
				logger.Info("This is a secondary instance - forwarding the file that should be played (if any) to the main instance.");
				if (args != null && args.Length > 0)
				{
					if (!string.IsNullOrWhiteSpace(args[args.Length - 1]))
					{
						string str = args[args.Length - 1];

						if (!str.ToLowerInvariant().EndsWith(".exe") && !str.ToLowerInvariant().EndsWith(".application"))
						{
							var cds = new NativeMethods.COPYDATASTRUCT
							{
								dwData = new IntPtr(3),
								cbData = (str.Length + 1) * sizeof(char),
								lpData = str
							};

							IntPtr bwin = GetPlayerWindow();
							if (bwin != IntPtr.Zero)
							{
								logger.Info("Sending to player window: " + bwin.ToString());
								NativeMethods.SendMessage(bwin, NativeMethods.WM_COPYDATA, IntPtr.Zero, ref cds);
							}
						}
					}
				}
				Application.Shutdown();
			}
		}

		protected override void OnExit(object sender, EventArgs e)
		{
			//try
			//{
			//	if (!string.IsNullOrWhiteSpace(Logic.LocalDataDirectory))
			//	{
			//		foreach (string s in Directory.EnumerateFiles(Logic.LocalDataDirectory))
			//		{
			//			//MessageBox.Show(s);
			//			File.Copy(s, Path.GetFileName(s), true);
			//		}
			//	}
			//}
			//catch (Exception exc) { }

			base.OnExit(sender, e);
			ReleaseSingleInstanceMutex();
		}

		private static void SetAddRemoveProgramsIcon()
		{
			//only run if deployed 
			try {
				string markerPath = Path.Combine(System.Windows.Forms.Application.StartupPath, "updated");
				if (File.Exists(markerPath)) return;
				File.WriteAllText(markerPath, "");
			}
			catch (Exception)
			{
				
			}

			if (System.Deployment.Application.ApplicationDeployment.IsNetworkDeployed)
			{
				try
				{
					//the icon is included in this program
					string iconSourcePath = Path.Combine(System.Windows.Forms.Application.StartupPath, "Graphics\\fileassoc.ico");

					if (!File.Exists(iconSourcePath))
						return;

					using (RegistryKey myUninstallKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
					{
						if (myUninstallKey == null)
							return;

						string[] mySubKeyNames = myUninstallKey.GetSubKeyNames();
						for (int i = 0; i < mySubKeyNames.Length; i++)
						{
							using (RegistryKey myKey = myUninstallKey.OpenSubKey(mySubKeyNames[i], true))
							{
								object myValue = myKey?.GetValue("DisplayName");
								if (myValue != null && myValue.ToString().ToLower().Contains("bivrost"))
								{
									myKey.SetValue("DisplayIcon", iconSourcePath);
									break;
								}
							}
						}
					}
				}
				catch (Exception)
				{
					//log an error
				}
			}
		}

		public static void CopyApplicationReference()
		{
			try
			{
				// http://download.bivrost360.com/player-desktop/canary/BivrostPlayer.application#BivrostPlayer.application, Culture=en, PublicKeyToken=8b49056c26c8df4d, processorArchitecture=msil
				if(!File.Exists(Logic.LocalDataDirectory + "Bivrost360Player.appref-ms"))
				{
					string appref = $"http://download.bivrost360.com/player-desktop/canary/BivrostPlayer.application#BivrostPlayer.application, Culture=en, PublicKeyToken={PublishInfo.ApplicationIdentity.PublicKeyToken}, processorArchitecture={PublishInfo.ApplicationIdentity.ProcessorArchitecture.ToString().ToLower()}";
					File.WriteAllText(Logic.LocalDataDirectory + "Bivrost360Player.appref-ms", appref, Encoding.GetEncoding(1200));
                }
			} catch(Exception)
			{

			}
		}

		public static void AssociateFileExtensions(string runArgument)
		{

			//PROTOCOL REGISTRATION
			try {
				using (RegistryKey myClasses = Registry.CurrentUser.OpenSubKey(@"Software\Classes", true) ?? Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Classes", true))
				{
					if (myClasses != null)
					{
						using (RegistryKey commandKey = myClasses.CreateSubKey(@"bivrost\shell\open\command"))
						{
							commandKey?.SetValue("", runArgument);
						}
						using (RegistryKey bivrostProtocolKey = myClasses.OpenSubKey("bivrost", true))
						{
							bivrostProtocolKey?.SetValue("URL Protocol", "");
						}
					}
				}
			} catch(Exception exc) {
				logger.Error(exc, "Exception while associating protocol");
			}


			////VIDEO FILES CONTEXT MENU
			//try
			//{
			//	RegistryKey bivrostMenuCommandKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Classes\SystemFileAssociations\.mp4\Shell\Open in 360Player\Command", true);
			//	if (bivrostMenuCommandKey == null)
			//		bivrostMenuCommandKey = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Classes\SystemFileAssociations\.mp4\Shell\Open in 360Player\Command");

			//	RegistryKey bivrostMenuKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Classes\SystemFileAssociations\.mp4\Shell\Open in 360Player", true);				
			//	string iconSourcePath = Path.Combine(System.Windows.Forms.Application.StartupPath, "Graphics\\fileassoc.ico");
			//	bivrostMenuKey.SetValue("Icon", iconSourcePath);

			//	bivrostMenuCommandKey.SetValue("", "rundll32.exe dfshim.dll,ShOpenVerbShortcut " + Logic.LocalDataDirectory + "Bivrost360Player.appref-ms" + "|%1");

			//	bivrostMenuCommandKey.Close();
			//	bivrostMenuKey.Close();
			//}
			//catch (Exception exc) {
			//	//MessageBox.Show(exc.Message + "\n\n" + exc.StackTrace);
			//}
			//AssociateExtension(".mp4");
			//AssociateExtension(".m4v");
			//AssociateExtension(".mov");
			//AssociateExtension(".avi");
			//AssociateExtension(".wmv");


			foreach(var ext in MediaDecoder.SupportedFileExtensions)
				AssociateExtension($".{ext}", runArgument);
		}

		private static void AssociateExtension(string extension, string runArgument)
		{
			if (!extension.StartsWith(".")) extension = "." + extension;

			//VIDEO FILES CONTEXT MENU
			try
			{
				RegistryKey bivrostMenuCommandKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Classes\SystemFileAssociations\" + extension + @"\Shell\Open in 360Player\Command", true);
				if (bivrostMenuCommandKey == null)
					bivrostMenuCommandKey = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Classes\SystemFileAssociations\" + extension + @"\Shell\Open in 360Player\Command");

				RegistryKey bivrostMenuKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Classes\SystemFileAssociations\" + extension + @"\Shell\Open in 360Player", true);
				string iconSourcePath = Path.Combine(System.Windows.Forms.Application.StartupPath, "Graphics\\fileassoc.ico");
				
				bivrostMenuKey.SetValue("Icon", iconSourcePath);

				bivrostMenuCommandKey.SetValue("", runArgument);

				bivrostMenuCommandKey.Close();
				bivrostMenuKey.Close();
			}
			catch (Exception exc)
			{
				//MessageBox.Show(exc.Message + "\n\n" + exc.StackTrace);
			}
		}

		public static string GetPublisher(string application)
		{
			using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
			{
				var appKey = key.GetSubKeyNames().FirstOrDefault(x => GetValue(key, x, "DisplayName") == application);
				if (appKey == null) { return null; }
				return GetValue(key, appKey, "Publisher");
			}
		}

		private static string GetValue(RegistryKey key, string app, string value)
		{
			using (var subKey = key.OpenSubKey(app))
			{
				if (!subKey.GetValueNames().Contains(value)) { return null; }
				return subKey.GetValue(value).ToString();
			}
		}

		private static string GetPublicKeyTokenFromAssembly(Assembly assembly)
		{
			var bytes = assembly.GetName().GetPublicKeyToken();
			if (bytes == null || bytes.Length == 0)
				return "None";
			var publicKeyToken = string.Empty;
			for (int i = 0; i < bytes.GetLength(0); i++)
				publicKeyToken += string.Format("{0:x2}", bytes[i]);
			return publicKeyToken;
		}

		private static IntPtr GetPlayerWindow()
		{
			IntPtr playerPointer = IntPtr.Zero;

			NativeMethods.EnumWindows((in1, in2) =>
			{
				StringBuilder sb = new StringBuilder(256);
				NativeMethods.GetWindowText(in1, sb, 256);
				string text = sb.ToString();
				if (text.StartsWith("BIVROST® 360Player"))
				{
					StringBuilder sbname = new StringBuilder(256);
					NativeMethods.GetClassName(in1, sbname, 256);
					logger.Info("Found Bivrost player window with class: " + sbname);
					playerPointer = in1;
				}
				return true;
			}, IntPtr.Zero);

			return playerPointer;
		}
	}
}
