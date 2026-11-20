using System;
using System.Diagnostics;

namespace Bivrost.Bivrost360Player.Tools
{
    public class TimeoutBool
    {
        private readonly object _sync = new object();
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private bool _value = false;

        public bool Value
        {
            get
            {
                lock (_sync)
                {
                    if (_value && _stopwatch.Elapsed.TotalMilliseconds > Timeout)
                    {
                        _value = false;
                        _stopwatch.Reset();
                    }
                    return _value;
                }
            }
            set
            {
                lock (_sync)
                {
                    _value = value;
                    if (value)
                        _stopwatch.Restart();
                    else
                        _stopwatch.Reset();
                }
            }
        }

        public double Timeout { get; set; } = 6000;

        public TimeoutBool() { }

        public TimeoutBool(bool initialValue)
        {
            Timeout = 6000;
            Value = initialValue;
        }

        public TimeoutBool(bool initialValue, double timeout)
        {
            Timeout = timeout;
            Value = initialValue;
        }

        public static implicit operator bool(TimeoutBool tb)
        {
            return tb != null && tb.Value;
        }

        public static implicit operator TimeoutBool(bool b)
        {
            return new TimeoutBool(b);
        }
    }
}
