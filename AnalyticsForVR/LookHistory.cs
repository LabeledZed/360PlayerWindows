using Bivrost.Bivrost360Player.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace Bivrost.AnalyticsForVR
{
	public class LookHistory
	{
		private const string BASE64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=";

		struct HeadPosition
		{

            public static byte Angle01to063(double angle01)
            {
                if (double.IsNaN(angle01) || double.IsInfinity(angle01))
                    return 0;
                double wrapped = angle01 - Math.Floor(angle01);
                int v = (int)Math.Round(64.0 * wrapped) % 64;
                if (v < 0) v += 64;
                return (byte)v;
            }

            public static byte Clamp01to063(double angle01)
            {
                if (double.IsNaN(angle01) || double.IsInfinity(angle01))
                    return 32;
                int v = (int)Math.Round(64.0 * angle01);
                if (v < 0) v = 0;
                if (v > 63) v = 63;
                return (byte)v;
            }

			internal HeadPosition(double yaw01, double pitch01, byte fov = 0)
			{
				yaw = Angle01to063(yaw01);
				pitch = Clamp01to063(pitch01);
				this.fov = fov;
			}

			public bool IsEmpty => yaw == 0 && pitch == 0 && fov == 0;

			readonly internal byte yaw;
			readonly internal byte pitch;
			readonly internal byte fov;

            public double Yaw => ((yaw / 64.0) * 2.0 * Math.PI) - Math.PI;
            public double Pitch => ((pitch / 64.0) * Math.PI) - (0.5 * Math.PI);

		}

        private List<HeadPosition> data;
        private int precision;

        public int SampleRate { get { return precision; } }

		public bool Empty
		{
			get
			{
				lock (data)
				{
					foreach (var point in data)
						if (!point.IsEmpty)
							return false;
					return true;
				}
			}
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="precision">How many times per second should orientation be sampled</param>
		/// <param name="mediaLength">How long is this medium</param>
		public LookHistory(int precision, double mediaLength)
		{
            this.precision = precision > 0 ? precision : 1;
            int count = 0;
            if (!double.IsNaN(mediaLength) && !double.IsInfinity(mediaLength) && mediaLength > 0)
            {
                double rawCount = Math.Ceiling(mediaLength * this.precision);
                if (rawCount > 0 && rawCount < 1024 * 1024)
                    count = (int)rawCount;
                else if (rawCount >= 1024 * 1024)
                    count = 1024 * 1024;
            }
            data = new List<HeadPosition>(count);
        }

        public void TrackData(float t, SharpDX.Quaternion quaternion, byte fov)
		{
            if (float.IsNaN(t) || float.IsInfinity(t) || t < 0f)
                return;

			var v = GraphicTools.QuaternionToYawPitch(quaternion);
            double yaw = ( Math.PI * 9 + v.X ) % (2 * Math.PI) - Math.PI; // => [-PI, +PI]
            double pitch = -v.Y;    // => [-PI/2, PI/2]
            double yaw01 = yaw / (2 * Math.PI) + 0.5;
            double pitch01 = pitch / Math.PI + 0.5;
            TrackData(t, yaw01, pitch01, fov);

            Bivrost.Log.LoggerManager.Publish("history.t", t);
            Bivrost.Log.LoggerManager.Publish("history.yaw", yaw * 180f / Math.PI);
            Bivrost.Log.LoggerManager.Publish("history.pitch", pitch * 180f / Math.PI);
			Bivrost.Log.LoggerManager.Publish("history.yaw01", yaw01);
			Bivrost.Log.LoggerManager.Publish("history.pitch01", pitch01);
		}

        void TrackData(float t, double yaw01, double pitch01, byte fov)
		{
            if (float.IsNaN(t) || float.IsInfinity(t) || t < 0f)
                return;

            double rawIdx = Math.Floor(t * precision);
            if (rawIdx < 0 || rawIdx > int.MaxValue - 1)
                return;

            int idx = (int)rawIdx;
            lock (data)
            {
                // add new element at end (and return)
                if (data.Count == idx)
                {
                    data.Add(new HeadPosition(yaw01, pitch01, fov));
                    return;
                }
                
                // no space for element - need to resize
                else if (idx > data.Count)
                {
					if (idx >= data.Capacity)
						data.Capacity = idx + 1;
					data.AddRange(Enumerable.Repeat<HeadPosition>(new HeadPosition(), (idx + 1) - data.Count));
                }

				data[idx] = new HeadPosition(yaw01, pitch01, fov);
            }
        }

		public string ToBase64()
		{
            int fov = 0;
            StringBuilder sb = new StringBuilder();
            lock (data) data.ForEach(pair =>
            {
                if (pair.IsEmpty)
                    sb.Append("--");
                else
                {
                    if (pair.fov != fov)
                    {
                        fov = pair.fov;
                        sb.Append($"!F{fov}!");
                    }
                    sb.Append(BASE64[pair.yaw]);
                    sb.Append(BASE64[pair.pitch]);
                }
            });
			return sb.ToString();
		}

        //public static LookHistory FromBase64(string serialized)
        //{
        //    throw new NotImplementedException();
        //}

	}
}
