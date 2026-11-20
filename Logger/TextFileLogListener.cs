using System;
using System.IO;
using System.Text;

namespace Bivrost.Log
{
	/// <summary>
	/// Writing to a text file
	/// </summary>
	public class TextFileLogListener : LogListener, IDisposable
	{
		private FileStream fp;
		private UTF8Encoding encoding;
		private readonly object writeLock = new object();
		private bool disposed = false;

		public string LogFile { get; protected set; }


		public TextFileLogListener(string logDirectory, string logPrefix = "log", string version = null)
		{
			string now = DateTime.Now.ToString("yyyy-MM-ddTHHmmss");
			if (version == null)
#if DEBUG
				version = "DEBUG";
#else
			{
				var asm = System.Reflection.Assembly.GetEntryAssembly() ?? System.Reflection.Assembly.GetExecutingAssembly();
				version = "v" + asm.GetName().Version.ToString();
			}
#endif

			if (!string.IsNullOrWhiteSpace(logDirectory) && !Directory.Exists(logDirectory))
				Directory.CreateDirectory(logDirectory);

			LogFile = Path.Combine(logDirectory ?? "", string.Format("{2}-{0}-{1}.txt", version, now, logPrefix));

			LoggerManager.log.Info("Log file: " + LogFile);
			fp = new FileStream(LogFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
			encoding = new UTF8Encoding(false);
		}


		public void Write(LoggerManager.LogElement entry)
		{
			string message = (entry.Message ?? "").Trim().Replace("\n", "\n\t");
			byte[] buf = encoding.GetBytes(
				$"[{entry.Type} {entry.Tag}] {entry.Time.ToString("yyyy-MM-dd HH:mm:ss")} at {entry.Path}\r\n" +
				$"\t{message}\r\n\r\n"
			);
			lock (writeLock)
			{
				if (disposed || fp == null) return;
				fp.Write(buf, 0, buf.Length);
				fp.Flush();
			}
		}

		public void Dispose()
		{
			lock (writeLock)
			{
				if (disposed) return;
				disposed = true;
				fp?.Dispose();
				fp = null;
			}
		}

	}
}
