using Bivrost.Log;
using Bivrost.Bivrost360Player.Streaming;
using System.Threading.Tasks;
using System.Windows;

namespace Bivrost.Bivrost360Player
{
	/// <summary>
	/// Interaction logic for OpenUrlView.xaml
	/// </summary>
	public partial class ServiceResultResolver : Window
	{
		private static Logger log = new Logger("ServiceResultResolver");


		protected ServiceResultResolver(Window owner)
		{
			Owner = owner;
			InitializeComponent();
		}

		public static ServiceResult DialogProcessURIBlocking(string uri, Window owner)
		{
			var window = new ServiceResultResolver(owner);

			object sync = new object();
			ServiceResult result = null;
			bool closed = false;
			bool completed = false;

			var task = Task.Run(() =>
			{
				var resolved = ProcessURI(uri, () =>
				{
					lock (sync) { return closed; }
				});

				bool shouldClose = false;
				lock (sync)
				{
					if (!closed)
					{
						result = resolved;
						completed = true;
						shouldClose = true;
					}
				}

				if (!shouldClose)
					log.Info($"Streaming result resolving was cancelled before completion. Uri={uri}.");
				else
					window.Dispatcher.InvokeAsync(() => window.Close());
			});

			window.ShowDialog();
			lock (sync)
			{
				closed = true;
				return completed ? result : null;
			}
		}

		private void Cancel_Click(object sender, RoutedEventArgs e)
		{
			Close();
		}

		/// <summary>
		/// Processes an URI and returns a ServiceResult or null on failure
		/// Displays notifications on errors.
		/// </summary>
		/// <param name="uri"></param>
		/// <returns>ServiceResult with parsed streaming information or null</returns>
		protected static ServiceResult ProcessURI(string uri, System.Func<bool> isCancelled = null)
		{
			void NotifyIfActive(string msg)
			{
				if (isCancelled == null || !isCancelled())
					Logic.Notify(msg);
			}

			try
			{
				var sr = Streaming.StreamingFactory.Instance.GetStreamingInfo(uri);

				// no error, but nothing found (probably couldn't parse)
				if (sr == null)
					NotifyIfActive("Url is not valid video or recognised streaming service address.");

				return sr;
			}
			catch (Streaming.StreamNotSupported exc)
			{
				log.Error(exc, "Streaming: video not supported: " + uri);
				NotifyIfActive("Video not yet supported.");
			}
			catch (Streaming.StreamParsingFailed exc)
			{
				log.Error(exc, "Streaming: Parsing failed. Unable to open the video: " + uri);
				NotifyIfActive("Parsing failed. Unable to open the video.");
			}
			catch (Streaming.StreamNetworkFailure exc)
			{
				log.Error(exc, "Streaming: Network/file failure. Unable to open the video: " + uri);
				NotifyIfActive("This file is currently unavailable.");
			}
			catch (System.Exception exc)
			{
				log.Error(exc, "Streaming: media not supported: " + uri);
				NotifyIfActive("Media not supported.");
			}

			return null;
		}
	}
}
