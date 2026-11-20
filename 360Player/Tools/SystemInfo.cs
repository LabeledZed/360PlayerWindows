using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Management;

namespace Bivrost.Bivrost360Player.Tools
{
	class SystemInfo
	{
		public static void Info()
		{
			GetProperties("Win32_Baseboard");
		}

		public static void GraphicAdapterInfo(ManagementObject MO)
		{
			Console.WriteLine(MO["AdapterRAM"]?.ToString());
			Console.WriteLine(MO["Caption"]?.ToString());
			Console.WriteLine(MO["CurrentHorizontalResolution"]?.ToString());
			Console.WriteLine(MO["CurrentVerticalResolution"]?.ToString());
			Console.WriteLine(MO["CurrentBitsPerPixel"]?.ToString());

			Console.WriteLine(MO["DriverDate"]?.ToString());
			Console.WriteLine(MO["DriverVersion"]?.ToString());
			Console.WriteLine(MO["Name"]?.ToString());
			Console.WriteLine(MO["VideoModeDescription"]?.ToString());
			Console.WriteLine(MO["VideoProcessor"]?.ToString());
			Console.WriteLine(MO["PNPDeviceID"]?.ToString());
		}

		public static void CPUInfo(ManagementObject MO)
		{
			Console.WriteLine(MO["Caption"]?.ToString());
			Console.WriteLine(MO["Description"]?.ToString());
			Console.WriteLine(MO["DeviceID"]?.ToString());
			Console.WriteLine(MO["L2CacheSize"]?.ToString());
			Console.WriteLine(MO["L3CacheSize"]?.ToString());
			Console.WriteLine(MO["Manufacturer"]?.ToString());
			Console.WriteLine(MO["MaxClockSpeed"]?.ToString());
			Console.WriteLine(MO["Name"]?.ToString());
			Console.WriteLine(MO["NumberOfCores"]?.ToString());
			Console.WriteLine(MO["NumberOfLogicalProcessors"]?.ToString());
			Console.WriteLine(MO["Version"]?.ToString());
		}

		public static void MemoryInfo(ManagementObject MO)
		{
			Console.WriteLine(MO["Caption"]?.ToString());
			Console.WriteLine(MO["DataWidth"]?.ToString());
			Console.WriteLine(MO["Speed"]?.ToString());
			Console.WriteLine(MO["Capacity"]?.ToString());
			Console.WriteLine(MO["Manufacturer"]?.ToString());
		}

		public static void GetProperties(string queryString)
		{
			WqlObjectQuery objectQuery = new WqlObjectQuery("select * from " + queryString);
			using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(objectQuery))
			using (ManagementObjectCollection results = searcher.Get())
			{
				foreach (ManagementObject MO in results)
				{
					using (MO)
					{
						foreach (PropertyData pd in MO.Properties)
						{
							Console.WriteLine("Property name:" + pd.Name);
							Console.WriteLine("\t\t value:" + pd.Value);
							Console.WriteLine();
						}
					}
				}
			}
		}
	}
}
