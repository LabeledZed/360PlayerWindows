using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.IO;
using System.Globalization;
using System.Web;
using System.Threading.Tasks;

namespace BivrostAnalytics
{
    public class Tracker
    {
		public const string URLEndpoint = @"http://www.google-analytics.com/collect";
		public const string URLEndpointSSL = @"https://ssl.google-analytics.com/collect";

		private List<object> TrackerParametersSections;
		private RestClient apiClient;

		public string TrackingId;
		public string DeviceId;
		public string UserAgentString;

		private bool SessionStarted = false;
		private bool QuitSession = false;
		private string CurrentScreen = "";

		public Tracker()
		{
			apiClient = new RestClient(URLEndpoint);			
		}

		public async Task<IRestResponse> TrackScreen(string screenName)
		{
			CurrentScreen = screenName;

			RestRequest hitRequest = GenerateApiHit();

			AddDefaultData(hitRequest);

			AddDataToRequest(hitRequest, new TrackerParameters.HitParameters()
			{
				HitType = HitType.Screenview
			});

			IRestResponse apiResponse = await apiClient.ExecuteTaskAsync(hitRequest);

			return apiResponse;
		}

		public async Task<IRestResponse> TrackEvent(string category, string action, string label)
		{
			RestRequest hitRequest = GenerateApiHit();

			AddDefaultData(hitRequest);

			AddDataToRequest(hitRequest, new TrackerParameters.HitParameters()
			{
				HitType = HitType.Event
			});

			AddDataToRequest(hitRequest, new TrackerParameters.EventTrackingParameters()
			{
				EventCategory = category,
				EventAction = action,				
				EventLabel = label,
				EventValue = DateTime.Now.ToFileTimeUtc()
			});

			IRestResponse apiResponse = await apiClient.ExecuteTaskAsync(hitRequest);

			return apiResponse;	
		}

		private Tracker AddDefaultData(RestRequest hitRequest)
		{
			if (QuitSession)
			{
				AddDataToRequest(hitRequest, new TrackerParameters.SessionParameters()
				{
					SessionControl = SessionControlCommand.End
				});
			}
			else if (!SessionStarted)
			{
				AddDataToRequest(hitRequest, new TrackerParameters.SessionParameters()
				{
					SessionControl = SessionControlCommand.Start
				});
				SessionStarted = true;
			}

			AddDataToRequest(hitRequest, new TrackerParameters.ContentInformationParameters()
			{
				ScreenName = CurrentScreen
			});

			AddDataToRequest(hitRequest, new TrackerParameters.GeneralParameters()
			{
				ProtocolVersion = "1",
				TrackingID = TrackingId
			});

			AddDataToRequest(hitRequest, new TrackerParameters.AppTrackingParameters()
			{
				ApplicationName = "Bivrost 360Player",
				ApplicationVersion = "1.0"
			});

			AddDataToRequest(hitRequest, new TrackerParameters.UserParameters()
			{
				ClientID = DeviceId,
				UserID = DeviceId
			});

			AddDataToRequest(hitRequest, new TrackerParameters.SystemInfoParameters()
			{
				ScreenColors = "32-bits",
				ScreenResolution = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width + "x" + System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height,
				UserLanguage = CultureInfo.CurrentCulture.ToString().ToLower(),
				ViewportSize = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width + "x" + System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height
			});

			return this;
		}

		public async Task<IRestResponse> TrackQuit()
		{
			QuitSession = true;
			return await TrackEvent("Application Events", "Exit", "App quit");
		}


		private RestRequest GenerateApiHit()
		{
			RestRequest request = new RestRequest(Method.POST);
			apiClient.UserAgent = UserAgentString;
			return request;
		}

		private void AddDataToRequest(RestRequest request, object dataObject)
		{
			dataObject.GetType().GetProperties().Where(pi => pi.IsDefined(typeof(ProtocolVariableAttribute), false)).ToList().ForEach(property =>
			{
				ProtocolVariableAttribute attr = property.GetCustomAttribute<ProtocolVariableAttribute>(false);

				var value = property.GetValue(dataObject);
				if(value == null)
				{
					if (attr.Required) throw new ArgumentNullException(property.Name, $"Required property {property.Name} is null.");
					else return;
				}

				
				Type effectiveType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

				var @switch = new Dictionary<Type, Action> {
					{ typeof(string), () => {
						var stringValue = (string)value;
						if (attr.ByteLimit > 0 && Encoding.UTF8.GetByteCount(stringValue) > attr.ByteLimit)
						{
							byte[] utf8 = Encoding.UTF8.GetBytes(stringValue);
							int len = attr.ByteLimit;
							while (len > 0 && (utf8[len - 1] & 0xC0) == 0x80)
								len--;
							if (len > 0 && (utf8[len - 1] & 0x80) != 0)
								len--;
							stringValue = Encoding.UTF8.GetString(utf8, 0, len);
						}
						request.AddParameter(attr.QueryString, stringValue);
						
					} },

					{ typeof(bool), () => {
						request.AddParameter(attr.QueryString, ((bool)value) ? 1:0);
                    } },

					{ typeof(uint), () => {
						request.AddParameter(attr.QueryString, ((uint)value));
					} },

					{ typeof(long), () => {
						request.AddParameter(attr.QueryString, ((long)value).ToString(CultureInfo.InvariantCulture));
					} },
				};

				if (@switch.ContainsKey(effectiveType))
					@switch[effectiveType]();
				else
				{
					request.AddParameter(attr.QueryString, value.ToString().ToLowerInvariant());
					Console.WriteLine("[WARNING] Type is not strictly supported by @switch");
				}

			});
		}
	}
}
