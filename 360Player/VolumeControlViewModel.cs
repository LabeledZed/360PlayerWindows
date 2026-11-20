using Caliburn.Micro;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Bivrost.Bivrost360Player
{
	public class VolumeControlViewModel : Screen
	{
		private VolumeControlView view;
		private IInputElement _element;
		private bool isDragging = false;
		private DateTime lastMouse;
		private DispatcherTimer visibilityTimer;
		private bool visible = false;
		private bool mute = false;

		public event Action<double> OnVolumeChange = delegate { };

		public VolumeControlViewModel()
		{
			Volume = 1;
			lastMouse = DateTime.Now;
		}

		protected override void OnViewLoaded(object view)
		{
			base.OnViewLoaded(view);
			this.view = view as VolumeControlView;
			if (this.view != null)
			{
				this.view.Opacity = 0;
				this.view.IsHitTestVisible = false;
			}
		}

		protected override void OnDeactivate(bool close)
		{
			visibilityTimer?.Stop();
			base.OnDeactivate(close);
		}

		private double _volume = 0.5;
		public double Volume {
			get
			{
				return mute ? 0 : this._volume;
			}
			set
			{
                this._volume = Math.Max(0, Math.Min(value, 1));
				NotifyOfPropertyChange(() => Volume);
				OnVolumeChange(Volume);
			}
		}

		public bool IsMuted { get { return this.mute; } }

		public void Show()
		{
			Execute.OnUIThread(() =>
			{
				lastMouse = DateTime.Now;

				if (!visible && view != null)
				{
					visible = true;
					view.IsHitTestVisible = true;
					Animate(true);

					if (visibilityTimer == null)
					{
						visibilityTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
						visibilityTimer.Tick += (sender, args) =>
						{
							if ((DateTime.Now - lastMouse).TotalSeconds > 2)
							{
								Hide();
							}
						};
					}
					visibilityTimer.Start();
				}
			});
		}

		public void Hide()
		{
			Execute.OnUIThread(() =>
			{
				visibilityTimer?.Stop();
				if (visible)
				{
					visible = false;
					if (view != null)
					{
						view.IsHitTestVisible = false;
						Animate(false);
					}
				}
			});
		}

		private void Animate(bool visible)
		{
			if (view == null) return;
			Storyboard storyboard = new Storyboard();
			DoubleAnimation opacityAnimation = new DoubleAnimation() { From = view.Opacity, To = visible ? 1 : 0, Duration = TimeSpan.FromSeconds(0.5) };
			Storyboard.SetTarget(opacityAnimation, view);
			Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath("Opacity"));
			storyboard.Children.Add(opacityAnimation);
			storyboard.Begin();
		}

		public void MouseWheel(MouseWheelEventArgs eventArgs)
		{
			double vol = Volume + Math.Sign(eventArgs.Delta) * 0.05f;
			vol = Math.Max(0, Math.Min(1, vol));
			if(!IsMuted)
				Volume = vol;
			lastMouse = DateTime.Now;
		}

		public void MouseDown(object sender, MouseButtonEventArgs eventArgs)
		{
			if (view == null) return;
			PositionToVolume(eventArgs.GetPosition(view).Y);

			_element = eventArgs.Source as IInputElement;
			_element?.CaptureMouse();

			isDragging = true;
			eventArgs.Handled = true;
		}

		public void ToggleMute()
		{
			mute = !mute;
			OnVolumeChange(Volume);
			NotifyOfPropertyChange(() => Volume);
			NotifyOfPropertyChange(() => IsMuted);
		}

		public void AdjustVolume(double delta)
		{
			if (Math.Abs(delta) < 1e-6) return;
			if (mute && delta > 0)
			{
				mute = false;
				NotifyOfPropertyChange(() => IsMuted);
			}
			Volume = Math.Max(0.0, Math.Min(1.0, this._volume + delta));
		}

		private void PositionToVolume(double position)
		{
			if (view == null || view.ActualHeight <= 0) return;
			//54 / 86
			double ratio = view.ActualHeight / 86;
			double margin = (86 - 54) * ratio * 0.5;
			double vol = ((86 * ratio - position - margin) / (54 * ratio));
			vol = Math.Max(0, Math.Min(1, vol));
			Volume = vol;
		}

		public void MouseMove(object sender, MouseEventArgs eventArgs)
		{
			if (isDragging && view != null)
			{
				PositionToVolume(eventArgs.GetPosition(view).Y);
			}
			lastMouse = DateTime.Now;
		}

		public void MouseUp(object sender, MouseButtonEventArgs eventArgs)
		{
			if (isDragging)
			{
				_element?.ReleaseMouseCapture();
				_element = null;
				isDragging = false;
			}
		}

	}
}
