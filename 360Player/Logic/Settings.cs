using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.IO;
using Bivrost.Bivrost360Player.ConfigUI;
using Bivrost.Log;

namespace Bivrost.Bivrost360Player
{


 

    public partial class Settings
	{

		static Logger log = new Logger("Settings");


#region settings management
		private readonly object _fileLock = new object();
		private string _settingsFile = "";
		[JsonIgnore]
		public string SettingsFile
		{
			get { return _settingsFile; }
			private set { _settingsFile = value; }
		}

		public Settings(string configFile = "")
		{
			if (string.IsNullOrWhiteSpace(configFile))
			{
				configFile = Path.Combine(Logic.LocalDataDirectory, "settings.conf");
			}
			SettingsFile = configFile;
			Load();

			if (InstallId == Guid.Empty)
			{
				InstallId = Guid.NewGuid();
				log.Info("Regenerated InstallId");
				Save();
			}
			log.Info("InstallId == " + InstallId);

		}


		public void Load()
		{
			lock (_fileLock)
			{
				if (File.Exists(SettingsFile))
				{
					try
					{
						JsonConvert.PopulateObject(File.ReadAllText(SettingsFile), this);
					}
					catch (Exception ex)
					{
						log.Error(ex, "Failed to load settings file, using defaults");
					}
				}
				else
				{
					Save();
				}
			}
		}

		public void Save()
		{
			lock (_fileLock)
			{
				try
				{
					string dir = Path.GetDirectoryName(SettingsFile);
					if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
						Directory.CreateDirectory(dir);
					string serialized = JsonConvert.SerializeObject(this);
					string tmpFile = SettingsFile + ".tmp";
					File.WriteAllText(tmpFile, serialized);
					File.Copy(tmpFile, SettingsFile, true);
					File.Delete(tmpFile);
					Console.WriteLine(serialized);
				}
				catch (Exception ex)
				{
					log.Error(ex, "Failed to save settings file");
				}
			}
		}
#endregion


		public Guid InstallId { get; set; } = Guid.Empty;

		public string LastStoredPlayerVersion { get; set; } = null;


		public string AutoPlayFile { get; set; } = "";
		public bool AutoPlay { get; set; } = true;
		public bool AutoLoad { get; set; } = true;

		[SettingsProperty("Start in fullscreen", ConfigItemType.Bool)]
		public bool StartInFullScreen { get; set; } = false;

		[SettingsProperty("Default VR headset mode", ConfigItemType.Enum)]
		public HeadsetMode HeadsetUsage { get; set; } = HeadsetMode.Disable;

		[SettingsProperty("Audio output device", ConfigItemType.AudioDevice)]
		public string AudioOutputDevice { get; set; } = Tools.AudioDeviceManager.AutoModeId;

		[SettingsProperty("OSVR screen number", ConfigItemType.Enum)]
		public ScreenSelection OSVRScreen { get; set; } = ScreenSelection.Autodetect;

		[SettingsProperty("Face screen in OpenVR", ConfigItemType.Bool)]
		public bool OpenVRReverse { get; set; } = true;

		[JsonIgnore]
		[SettingsAdvancedProperty("Reset Analytics ID", ConfigItemType.Action, Caption = "Reset ID")]
		public System.Action ResetInstallId { get; set; }

		[JsonIgnore]
		[SettingsAdvancedProperty("Reset player configuration", ConfigItemType.Action, Caption = "Reset")]
		public System.Action ResetConfiguration { get; set; }

		[SettingsAdvancedProperty("User headset tracking in window", ConfigItemType.Bool)]
		public bool UserHeadsetTracking { get; set; } = false;


#if FEATURE_GHOSTVR
		//GhostVR and locally stored sessions
		[SettingsAdvancedProperty("Enable GhostVR analytics", ConfigItemType.Bool, requiredFeatures = FeaturesEnum.ghostVR)]
		public bool GhostVREnabled { get; set; } = true;


		[SettingsAdvancedProperty("GhostVR license token", ConfigItemType.String, requiredFeatures = FeaturesEnum.ghostVR)]
		public string GhostVRLicenseToken { get; set; }


		[SettingsAdvancedProperty("GhostVR API endpoint override", ConfigItemType.String, requiredFeatures = FeaturesEnum.ghostVR | FeaturesEnum.isDebugOrCanary)]
		public string GhostVREndpointOverride { get; set; } = "http://dev.ghostvr.io/api/v1/";
#endif


		[SettingsAdvancedProperty("Local video analytics sessions enabled", ConfigItemType.Bool, requiredFeatures = FeaturesEnum.locallyStoredSessions)]
		public bool LocallyStoredSessions { get; set; } = false;

		[SettingsAdvancedProperty("Local video analytics sessions directory", ConfigItemType.Path, requiredFeatures = FeaturesEnum.locallyStoredSessions)]
		public string LocallyStoredSessionsDirectory { get; set; } = null;


#if FEATURE_LICENSE_NINJA
		//License settings
		public string LicenseCode = "";
#endif


		// Debug features
		[SettingsAdvancedProperty("Use black background in player window", ConfigItemType.Bool, requiredFeatures = FeaturesEnum.isDebug)]
		public bool UseBlackBackground { get; set; } = false;

		[SettingsAdvancedProperty("Do not exit fullscreen on movie stop", ConfigItemType.Bool, requiredFeatures = FeaturesEnum.isDebug)]
		public bool DoNotExitFullscreenOnStop { get; set; } = false;

		[SettingsAdvancedProperty("Disable UI", ConfigItemType.Bool, requiredFeatures = FeaturesEnum.isDebug)]
		public bool DisableUI { get; set; } = false;


		// Space navigator
        [SettingsAdvancedProperty("Space Navigator: invert pitch", ConfigItemType.Bool)]
        public bool SpaceNavigatorInvertPitch { get; set; } = false;
        [SettingsAdvancedProperty("Space Navigator: shortcut keys and push to zoom active", ConfigItemType.Bool)]
        public bool SpaceNavigatorKeysAndZoomActive { get; set; } = true;
    }


	[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
	internal abstract class SettingVerificationAttribute : Attribute
	{
		public SettingVerificationAttribute() { }
		public abstract void Normalize();
		public abstract bool Valid { get; }
	}

}