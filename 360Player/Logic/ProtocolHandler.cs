using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Bivrost.Bivrost360Player
{
	public class ProtocolHandler
	{
		private const string PROTOCOL_PREFIX = "bivrost";

		public static void RegisterProtocol()
		{
			Task.Factory.StartNew(() => {
				CreateClickOnceShortcut();
			});
		}

		public static void CreateClickOnceShortcut()
		{
			try
			{
				byte[] token = System.Reflection.Assembly.GetExecutingAssembly().GetName().GetPublicKeyToken();
				if (token == null || token.Length == 0)
				{
					byte[] key = System.Reflection.Assembly.GetExecutingAssembly().GetName().GetPublicKey();
					using (var sha1 = SHA1.Create())
					{
						token = sha1.ComputeHash(key ?? new byte[0]).Take(8).ToArray();
					}
				}
				if (!string.IsNullOrWhiteSpace(Logic.LocalDataDirectory))
				{
					if (!Directory.Exists(Logic.LocalDataDirectory))
						Directory.CreateDirectory(Logic.LocalDataDirectory);
					File.WriteAllText(Path.Combine(Logic.LocalDataDirectory, "publictoken"), ByteArrayToString(token));
				}
			}
			catch (Exception) { }
		}

		public static string ByteArrayToString(byte[] ba)
		{
			StringBuilder hex = new StringBuilder(ba.Length * 2);
			foreach (byte b in ba)
				hex.AppendFormat("{0:x2}", b);
			return hex.ToString();
		}
	}
}
