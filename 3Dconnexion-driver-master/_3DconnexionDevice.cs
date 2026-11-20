using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using _3Dconnexion;
using System.Threading;
using System.Runtime.InteropServices;

namespace _3DconnexionDriver
{
    public class _3DconnexionDevice : IDisposable
    {
        IntPtr _deviceHandle = IntPtr.Zero;
        int _leds = -1;
		UInt32 _uiMode = SiApp.SI_UI_NO_CONTROLS;
		
        /// <summary>
        /// Event when Device is at 0 Point
        /// </summary>
        public event EventHandler ZeroPoint;
        
        /// <summary>
        /// Event when Device is moved
        /// </summary>
        public event EventHandler<MotionEventArgs> Motion;

		/// <summary>
		/// Event when button is pressed
		/// </summary>
		public event EventHandler<ButtonEventArgs> ButtonPressed;

		/// <summary>
		/// Event when button is released
		/// </summary>
		public event EventHandler<ButtonEventArgs> ButtonReleased;

		/// <summary>
		/// Event when Device changes. Doesn't work yet
		/// </summary>
		//public event EventHandler<DeviceChangeEventArgs> DeviceChange;

		/// <summary>
		/// Dispatching Thread
		/// </summary>
		private readonly System.Threading.Thread eventThread;
        
        /// <summary>
        /// Buffer for Events
        /// </summary>
        private readonly Dictionary<SiApp.SiEventType, EventArgs> eventBuffer = new Dictionary<SiApp.SiEventType, EventArgs>();

        public _3DconnexionDevice(string appName)
        {
            this.AppName = appName;
            eventThread = new System.Threading.Thread(EventThreadLoop);
            eventThread.IsBackground = true;
            eventThread.Name = "3Dconnexion-Event-Dispatcher";
            eventThread.Start();
        }

        ~_3DconnexionDevice()
        {
            Dispose();
        }

        /// <summary>
        /// Device is available (initialized)
        /// </summary>
        public bool IsAvailable
        {
            get { return _deviceHandle != IntPtr.Zero; }
        }

        /// <summary>
        /// Device is disposed
        /// </summary>
        public bool IsDisposed
        {
            get { return _isDisposed; }
            private set { _isDisposed = value; }
        }
        private volatile bool _isDisposed;

        /// <summary>
        /// The name of the app using this device
        /// </summary>
        public string AppName
        {
            get; 
            private set;
        }

        /// <summary>
        /// The Name of the Device (e.g. Space Pilot)
        /// </summary>
        public string DeviceName
        {
            get;
            private set;
        }

        /// <summary>
        /// The DeviceID of the device
        /// </summary>
        public int DeviceID
        {
            get;
            private set;
        }

        /// <summary>
        /// The Firmware version of the device
        /// </summary>
        public string FirmwareVersion
        {
            get;
            private set;
        }

		public int NumButtons
		{
			get;
			private set;
		}

        /// <summary>
        /// Bitwise Flags for the LEDs of the Device
        /// </summary>
        public int LEDs
        {
            get { return _leds; }
            set
            {
                _leds = value;
                if (IsAvailable)
                {
                    SiApp.SiSetLEDs(_deviceHandle, _leds);
                }
            }
        }

		public bool UiMode
		{
			get
			{
				return _uiMode == SiApp.SI_UI_ALL_CONTROLS ? true : false;
			}
			set
			{
				_uiMode = value ? SiApp.SI_UI_ALL_CONTROLS : SiApp.SI_UI_NO_CONTROLS; 
				if (IsAvailable)
					SiApp.SiSetUiMode(_deviceHandle, _uiMode);
			}
		}

        /// <summary>
        /// Path to an icon for that device, provided by the 3Dx Ware Driver
        /// </summary>
        public string IconPath
        {
            get
            {
                if (IsAvailable)
                {
                    string path;
                    var ret = SiApp.SiGetDeviceImageFileName(_deviceHandle, out path);
                    return path;
                }
                return null;
            }
        }

        /// <summary>
        /// Initialize the Device
        /// </summary>
        /// <param name="windowHandle">Handle to window using the device</param>
        public void InitDevice(IntPtr windowHandle)
        {
            if (this.IsDisposed)
                throw new ObjectDisposedException("");
            if (IsAvailable)
                return; //Init already done.

            var v = SiApp.SiInitialize();
            if (v != SiApp.SpwRetVal.SPW_NO_ERROR)
                throw new _3DxException("Unable to initialize SiApp DLL: " + v);

            SiApp.SiOpenData o = default(SiApp.SiOpenData);
            SiApp.SiOpenWinInit(ref o, windowHandle);

            _deviceHandle = SiApp.SiOpen(this.AppName, SiApp.SI_ANY_DEVICE, IntPtr.Zero, SiApp.SI_EVENT, ref o);
            if (_deviceHandle == IntPtr.Zero)
            {
                SiApp.SiTerminate();
                throw new _3DxException("Unable to open device");
            }
            
            string devName;
            SiApp.SiGetDeviceName(_deviceHandle, out devName);

            this.DeviceName = devName;

            this.DeviceID = SiApp.SiGetDeviceID(_deviceHandle);

            SiApp.SiDevInfo info = default(SiApp.SiDevInfo);
            SiApp.SiGetDeviceInfo(_deviceHandle, ref info);

            this.FirmwareVersion = info.firmware;
			this.NumButtons = info.numButtons;
        }


		public void ResetAllButtonBindings()
		{
			if (this.IsDisposed)
				throw new ObjectDisposedException("");
			if (!IsAvailable)
				return;

			SiApp.SiSyncSendQuery(_deviceHandle);
			for(uint it = 1; it<=NumButtons; it++)
				SiApp.SiSyncSetButtonAssignment(_deviceHandle, it, 0);
		}

		public void Beep(string tone)
		{
			if (!IsAvailable)
				return;
			var retVal = SiApp.SiBeep(_deviceHandle, tone);
		}

        /// <summary>
        /// Close the Device
        /// </summary>
        public void CloseDevice()
        {
            if (_deviceHandle != IntPtr.Zero)
            {
                SiApp.SiClose(_deviceHandle);
                SiApp.SiTerminate();
                _deviceHandle = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Called in WndPrc to process the Window Messages
        /// </summary>
        /// <param name="msg">Message Number</param>
        /// <param name="wParam"></param>
        /// <param name="lParam"></param>
        public void ProcessWindowMessage(int msg, IntPtr wParam, IntPtr lParam)
        {
            if (this.IsDisposed || !IsAvailable) //We don't throw an Exception in the Message Loop, just return
                return;

            SiApp.SiGetEventData evd = default(SiApp.SiGetEventData);
            SiApp.SiGetEventWinInit(ref evd, msg, wParam, lParam);
            SiApp.SiSpwEvent_SpwData edata = default(SiApp.SiSpwEvent_SpwData);
            var t = SiApp.SiGetEvent(_deviceHandle, SiApp.SI_AVERAGE_EVENTS, ref evd, ref edata);
            if (t == SiApp.SpwRetVal.SI_IS_EVENT)
            {
                switch (edata.type)
                {
                    case SiApp.SiEventType.SI_ZERO_EVENT:
                        lock (eventBuffer)
                        {
                            eventBuffer[SiApp.SiEventType.SI_ZERO_EVENT] = EventArgs.Empty;
                            Monitor.Pulse(eventBuffer);
                        }
                        break;
                    case SiApp.SiEventType.SI_MOTION_EVENT:
                        lock (eventBuffer)
                        {
                            eventBuffer[SiApp.SiEventType.SI_MOTION_EVENT] = MotionEventArgs.FromEventArray(edata.spwData.mData);
                            Monitor.Pulse(eventBuffer);
                        }
                        break;
					case SiApp.SiEventType.SI_BUTTON_PRESS_EVENT:
						lock (eventBuffer)
						{
							eventBuffer[SiApp.SiEventType.SI_BUTTON_PRESS_EVENT] = ButtonEventArgs.FromKeyCode((int)edata.spwData.bData.last, true);
							Monitor.Pulse(eventBuffer);
						}
						break;
					case SiApp.SiEventType.SI_BUTTON_RELEASE_EVENT:
						lock (eventBuffer)
						{
							eventBuffer[SiApp.SiEventType.SI_BUTTON_RELEASE_EVENT] = ButtonEventArgs.FromKeyCode((int)edata.spwData.bData.last, false);
							Monitor.Pulse(eventBuffer);
						}
						break;
					default:
						break;
                }
            }
        }

        protected virtual void OnZeroPoint()
        {
            var handler = ZeroPoint;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        protected virtual void OnMotion(MotionEventArgs args)
        {
            var handler = Motion;
            if (handler != null)
                handler(this, args);
        }

		protected virtual void OnButtonPress(ButtonEventArgs args)
		{
			var handler = ButtonPressed;
			if (handler != null)
				handler(this, args);
		}

		protected virtual void OnButtonRelease(ButtonEventArgs args)
		{
			var handler = ButtonReleased;
			if (handler != null)
				handler(this, args);
		}

		//protected virtual void OnDeviceChange(DeviceChangeEventArgs args)
		//{
		//    if (DeviceChange != null)
		//        DeviceChange(this, args);
		//}

		#region IDisposable Member

		public void Dispose()
        {
            if (!this.IsDisposed)
            {
                this.IsDisposed = true;
                CloseDevice();
                ZeroPoint = null;
                Motion = null;
                ButtonPressed = null;
                ButtonReleased = null;
                lock (eventBuffer)
                {
                    Monitor.PulseAll(eventBuffer);
                }
                GC.SuppressFinalize(this);
            }
        }

        #endregion

        private void EventThreadLoop()
        {
            while (!this.IsDisposed)
            {
                KeyValuePair<SiApp.SiEventType, EventArgs>[] pending;
                lock (eventBuffer)
                {
                    while (eventBuffer.Count == 0 && !this.IsDisposed)
                        Monitor.Wait(eventBuffer);
                    if (this.IsDisposed)
                        break;
                    pending = eventBuffer.ToArray();
                    eventBuffer.Clear();
                }

                foreach (var c in pending)
                {
                    switch (c.Key)
                    {
                        case SiApp.SiEventType.SI_MOTION_EVENT:
                            OnMotion(c.Value as MotionEventArgs); break;
						case SiApp.SiEventType.SI_BUTTON_PRESS_EVENT:
							OnButtonPress(c.Value as ButtonEventArgs); break;
						case SiApp.SiEventType.SI_BUTTON_RELEASE_EVENT:
							OnButtonRelease(c.Value as ButtonEventArgs); break;
						case SiApp.SiEventType.SI_ZERO_EVENT:
                            OnZeroPoint(); break;
                    }
                }
                Thread.Sleep(50);
            }
        }
    }
}
