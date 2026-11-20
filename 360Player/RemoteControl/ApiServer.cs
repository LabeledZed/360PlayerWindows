#if FEATURE_REMOTE_CONTROL

//using Nancy;
using System;
//using Nancy.Hosting.Self;
using System.Diagnostics;
//using Nancy.ModelBinding;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Linq;
using Nancy;
using Nancy.Hosting.Self;
using Nancy.ModelBinding;
using System.Globalization;
using System.Collections.Concurrent;

/// <summary>
/// Wymaga pakietów z NuGet:
///  - Nancy
///  - Nancy.Hosting.Self
///  - JSON.net
/// </summary>
namespace Bivrost.Bivrost360Player
{
    public class ApiServer
		: NancyModule
    {

		public static NancyHost InitNancy(Action<ApiServer> onInit, int port = 8080)
		{
			OnInit += onInit;

			System.Threading.Thread.CurrentThread.CurrentCulture =
			System.Threading.Thread.CurrentThread.CurrentUICulture =
			//System.Globalization.CultureInfo.DefaultThreadCurrentCulture=
			System.Globalization.CultureInfo.InvariantCulture;

			string filePath = Process.GetCurrentProcess().MainModule.FileName;
			//Process.Start("netsh", "advfirewall firewall delete rule name=\"Remote Video Player\"");
			//Process.Start("netsh", "advfirewall firewall delete rule name=\"Open Port " + port + "\"");
			//Process.Start("netsh", "advfirewall firewall add rule name=\"Remote Video Player\" dir=in action=allow program=\"" + filePath + "\" enable=yes");
			//Process.Start("netsh", "advfirewall firewall add rule name=\"Open Port " + port + "\" dir=in action=allow protocol=TCP localport=" + port);

			var host = new NancyHost(
				new System.Uri("http://localhost:" + port),
				new DefaultNancyBootstrapper(),
				new HostConfiguration()
				{
					UrlReservations = new UrlReservations() { CreateAutomatically = true }
				}
			);
			host.Start();

			return host;
		}


		static string Log(params object[] p)
		{
			System.Console.Write("[API] ");
			var str = string.Join(" ", p);
			System.Console.WriteLine(str);
			return str;
		}


		class MovieContainer { public string movie; }

		/// <summary>
		/// w Unity3d jest przerobiona kopia
		/// </summary>
		public class Status
		{
			private readonly object _sync = new object();
			public Dictionary<int, string[]> commands = new Dictionary<int, string[]>();

			public int max_id = 0;

			internal void Ack(int maxId)
			{
				lock (_sync)
				{
					foreach (var k in commands.Keys.ToList())
						if (k <= maxId)
							commands.Remove(k);
				}
			}

			internal void Command(params string[] p)
			{
				lock (_sync)
				{
					commands[++max_id] = p;
				}
			}

			internal string ToJson()
			{
				lock (_sync)
				{
					return JsonConvert.SerializeObject(this);
				}
			}
		}


#region API

		/// <summary>
		/// W unity3d jest kopia
		/// </summary>
		public enum State
		{
			init, stop, play, pause, off
		};

		public static Status status = new Status();
		public static string device_id;
		public static string[] movies;
		public static float t01;
		public static Tuple<float, float, float> euler = new Tuple<float, float, float>(0, 0, 0);
		public static State state;

		public static void CommandPlay(string path) { status.Command("play", path); }
		public static void CommandPause() { status.Command("pause"); }
		public static void CommandUnPause() { status.Command("unpause"); }
		public static void CommandStop() { status.Command("stop"); }
		public static void CommandReset() { status.Command("reset"); }
		public static void CommandSettings(bool repeat, bool resetOnPutOn) { status.Command("settings", repeat ? "true" : "false", resetOnPutOn ? "true" : "false"); }
		public static void CommandMessage(string message) { status.Command("message", message); }
		public static void CommandOffset(float eulerX, float eulerY, float eulerZ) { status.Command("offset", eulerX.ToString("000.0", CultureInfo.InvariantCulture), eulerY.ToString("000.0", CultureInfo.InvariantCulture), eulerZ.ToString("000.0", CultureInfo.InvariantCulture)); }
		public static void CommandSeek01(float t01) { status.Command("seek01", t01.ToString("0.000000", CultureInfo.InvariantCulture)); }
		public static void CommandSeek(float t) { status.Command("seek", t.ToString("00000.00", CultureInfo.InvariantCulture)); }
		public static void CommandReinit() { status.Command("reinit"); }


        // Control API handlers
        public static Func<string[]> CommandMoviesHandler = null;
        public static Func<string, bool, bool> CommandLoadHandler = null;
        public static Func<float, bool> CommandSeekHandler = null;
        public static Func<bool> CommandStopAndResetHandler = null;
        public static Func<bool> CommandPauseHandler = null;
        public static Func<bool> CommandUnpauseHandler = null;
        public static Func<PlayingInfo> CommandPlayingHandler = null;
        

        public static event Action<ApiServer> OnInit;
		public static event Action<State> OnStateChange;
		public static event Action<Tuple<float, float, float>, float> OnPos;
		public static event Action<Tuple<float, float, float, float>, float> OnPosQuaternion;
		public static event Action OnBackPressed;
		public static event Action<string> OnInfo;
		public static event Action<string> OnConfirmPlay;

#endregion

        /// <summary>
        /// Wywoływane automatycznie przez Nancy
        /// </summary>
        public ApiServer() : base("/v1/")
        {
            Get["/"] = _ => "api v1";

            Post["/init"] = p =>
            {
                status = new Status();
                device_id = null;
                t01 = 0;
                euler = new Tuple<float, float, float>(0, 0, 0);
                state = State.init;

                var q = Request.Query;
                device_id = (string)q.device_id;
                var boundMovies = this.Bind<List<MovieContainer>>();
                movies = boundMovies != null ? boundMovies.Where(mc => mc != null).Select(mc => mc.movie).ToArray() : new string[0];

                if (OnInit != null)
                    OnInit(this);

                return status.ToJson();
            };

            Get["/state-change"] = p =>
            {
                string stateStr = (string)Request.Query.state;
                if (string.IsNullOrEmpty(stateStr) || !Enum.TryParse(stateStr, true, out State parsedState))
                    return HttpStatusCode.BadRequest;

                state = parsedState;

                if (OnStateChange != null)
                    OnStateChange(state);

                return status.ToJson();
            };

            Get["/pos"] = p =>
            {
                var q = Request.Query;
                if (!float.TryParse((string)q.euler_x, NumberStyles.Float, CultureInfo.InvariantCulture, out float ex) ||
                    !float.TryParse((string)q.euler_y, NumberStyles.Float, CultureInfo.InvariantCulture, out float ey) ||
                    !float.TryParse((string)q.euler_z, NumberStyles.Float, CultureInfo.InvariantCulture, out float ez) ||
                    !float.TryParse((string)q.t01, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedT01) ||
                    !float.TryParse((string)q.quat_x, NumberStyles.Float, CultureInfo.InvariantCulture, out float qx) ||
                    !float.TryParse((string)q.quat_y, NumberStyles.Float, CultureInfo.InvariantCulture, out float qy) ||
                    !float.TryParse((string)q.quat_z, NumberStyles.Float, CultureInfo.InvariantCulture, out float qz) ||
                    !float.TryParse((string)q.quat_w, NumberStyles.Float, CultureInfo.InvariantCulture, out float qw))
                {
                    return HttpStatusCode.BadRequest;
                }

                euler = new Tuple<float, float, float>(ex, ey, ez);
                t01 = parsedT01;
                var quat = new Tuple<float, float, float, float>(qx, qy, qz, qw);

                if (OnPos != null)
                    OnPos(euler, t01);

                if (OnPosQuaternion != null)
                    OnPosQuaternion(quat, t01);

                return status.ToJson();
            };

            Get["/back"] = _ =>
            {
                if (OnBackPressed != null)
                    OnBackPressed();

                return status.ToJson();
            };

            Get["/ack"] = p =>
            {
                if (!int.TryParse((string)Request.Query.max_id, NumberStyles.Integer, CultureInfo.InvariantCulture, out int max_id))
                    return HttpStatusCode.BadRequest;
                status.Ack(max_id);

                return status.ToJson();
            };

            Post["/info"] = p =>
            {
                string message = (string)Request.Form["message"] ?? "";

                if (OnInfo != null)
                    OnInfo(message);

                return status.ToJson();
            };

            Post["/confirm-play"] = p =>
            {
                string path = (string)Request.Form["path"];
                if (string.IsNullOrWhiteSpace(path))
                    return HttpStatusCode.BadRequest;

                if (OnConfirmPlay != null)
                    OnConfirmPlay(path);

                return status.ToJson();
            };

            /// control API
            Get["/movies"] = p => JsonConvert.SerializeObject(CommandMoviesHandler == null ? new string[] { } : CommandMoviesHandler());

            Get["/load"] = p =>
            {
                string movie = (string)Request.Query.movie;
                if (string.IsNullOrWhiteSpace(movie))
                    return HttpStatusCode.BadRequest;
                bool autoplay = false;
                bool.TryParse((string)Request.Query.autoplay, out autoplay);
                return JsonConvert.SerializeObject(CommandLoadHandler == null ? false : CommandLoadHandler(movie, autoplay));
            };

            Get["/seek"] = p =>
            {
                if (!float.TryParse((string)Request.Query.t, NumberStyles.Float, CultureInfo.InvariantCulture, out float seekTime))
                    return HttpStatusCode.BadRequest;
                return JsonConvert.SerializeObject(CommandSeekHandler == null ? false : CommandSeekHandler(seekTime));
            };

            Get["/stop-and-reset"] = p => JsonConvert.SerializeObject(CommandStopAndResetHandler == null ? false : CommandStopAndResetHandler());

            Get["/pause"] = p => JsonConvert.SerializeObject(CommandPauseHandler == null ? false : CommandPauseHandler());

            Get["/unpause"] = p => JsonConvert.SerializeObject(CommandUnpauseHandler == null ? false : CommandUnpauseHandler());

            Get["/playing"] = p => JsonConvert.SerializeObject(CommandPlayingHandler == null ? new PlayingInfo() { is_playing = false, movie = "", quat_x = 0f, quat_y = 0f, quat_z = 0f, quat_w = 0f, t = 0f, tmax = 0f } : CommandPlayingHandler());

			Get["/events"] = p =>
			{
				var waitTimer = Stopwatch.StartNew();
				TimeSpan totalTimeout = TimeSpan.FromSeconds(10);

				while (waitTimer.Elapsed < totalTimeout)
				{
					TimeSpan remaining = totalTimeout - waitTimer.Elapsed;
					if (remaining <= TimeSpan.Zero)
						break;

					OutgoingEvent ev;
					if (outgoingEvents.TryTake(out ev, remaining))
					{
						var r = new List<OutgoingEvent> { ev };
						while (outgoingEvents.TryTake(out ev, TimeSpan.Zero))
							r.Add(ev);
						// ignore all events older than 5 sec
						r.RemoveAll(e => e.created.Elapsed > TimeSpan.FromMilliseconds(5000));
						if (r.Count > 0)
							return JsonConvert.SerializeObject(r);
					}
					else
					{
						break;
					}
				}

				return "[]";
			};

#if DEBUG
			Get["/__eventtester"] = p =>
			{
				AddOutgoingEvent("ev1", "arg1");
				AddOutgoingEvent("ev2", "arg2");
				AddOutgoingEvent("ev3", "arg3");
				return "\"done\"";
			};
#endif
		}

		private const int MaxOutgoingEvents = 256;
		static BlockingCollection<OutgoingEvent> outgoingEvents = new BlockingCollection<OutgoingEvent>(MaxOutgoingEvents);

		class OutgoingEvent {
			public string name;
			public string arg;

			[JsonIgnore]
			public Stopwatch created=new Stopwatch();
		}

		public static void AddOutgoingEvent(string name, string arg) {
			var og = new OutgoingEvent() { name = name, arg = arg };
			og.created.Start();
			while (!outgoingEvents.TryAdd(og))
			{
				OutgoingEvent discarded;
				if (!outgoingEvents.TryTake(out discarded))
					break;
			}
		}





		public struct PlayingInfo
		{

			/// <summary>
			/// The path to the current movie.
			/// </summary>
			public string movie;

			/// <summary>
			/// The current camera orientation quaternion
			/// </summary>
			public float quat_x;

			/// <summary>
			/// The current camera orientation quaternion
			/// </summary>
			public float quat_y;

			/// <summary>
			/// The current camera orientation quaternion
			/// </summary>
			public float quat_z;

			/// <summary>
			/// The current camera orientation quaternion
			/// </summary>
			public float quat_w;


			/// <summary>
			/// Current playback time
			/// </summary>
			public float t;


			/// <summary>
			/// Movie length
			/// </summary>
			public float tmax;


			/// <summary>
			/// Is the movie is currently playing
			/// </summary>
			public bool is_playing;

		}

	}
}

#endif