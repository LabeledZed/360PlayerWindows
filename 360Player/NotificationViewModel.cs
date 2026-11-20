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
	public class NotificationViewModel : Screen
	{
		public event Action<NotificationViewModel> OnClose = delegate { };
		private float _timeout = 5f;
		private float _currentTime = 5f;
		private DispatcherTimer _closeTimer;
		private NotificationView _view;
		private bool _animatedClosing = false;
		private System.Action _userAction = null;
		private bool _isAction = false;

		public NotificationViewModel(string message, string url = "", float timeout = 5f, string linkLabel = "read more")
		{
			Message = message;
			Url = url;
			ActionLabel = linkLabel;
			this._timeout = timeout;
			this._currentTime = _timeout;
		}

		public NotificationViewModel(string message, System.Action userAction, string actionLabel = "", float timeout = 10f)
		{
			_isAction = true;
			Message = message;
			Url = "";
			ActionLabel = actionLabel;
			this._timeout = timeout;
			this._currentTime = _timeout;
			this._userAction = userAction;
		}

		protected override void OnViewLoaded(object view)
		{
			base.OnViewLoaded(view);
			_view = view as NotificationView;
			StartCloseTimer();
		}

		private void StartCloseTimer()
		{
			if (_closeTimer != null) return;
			_closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
			_closeTimer.Tick += (s, e) =>
			{
				_currentTime -= 0.1f;
				if (_currentTime <= 0)
				{
					_closeTimer.Stop();
					AnimateClose();
				}
			};
			_closeTimer.Start();
		}

		public string Message { get; set; }
		public string Url { get; set; }
		public string ActionLabel { get; set; }

		public Visibility MoreVisible { get {
				if (_isAction)
				{
					if (!string.IsNullOrWhiteSpace(ActionLabel)) return Visibility.Visible;
				}
				
				return string.IsNullOrWhiteSpace(Url) ? Visibility.Collapsed : Visibility.Visible;
			} }

		public void OpenUrl(RoutedEventArgs e)
		{
			e.Handled = true;
			if(!string.IsNullOrWhiteSpace(Url))
			{
				try
				{
					System.Diagnostics.Process.Start(Url);
				}
				catch (Exception) { }
			}
			if (_userAction != null)
			{	
				_userAction();
				Close();
			}
		}

		public void Close()
		{
			if (_animatedClosing) return;
			_animatedClosing = true;
			_closeTimer?.Stop();
			OnClose(this);
		}

		public void AnimateClose()
		{
			Execute.OnUIThread(() =>
			{
				if (_animatedClosing) return;
				_animatedClosing = true;
				_closeTimer?.Stop();
				if (_view == null)
				{
					OnClose(this);
					return;
				}
				Storyboard storyboard = new Storyboard();
				DoubleAnimation opacityAnimation = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromSeconds(0.5f)));
				opacityAnimation.EasingFunction = new QuadraticEase() { EasingMode = EasingMode.EaseInOut };
				Storyboard.SetTarget(opacityAnimation, _view);
				Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath("Opacity"));
				storyboard.Children.Add(opacityAnimation);
				storyboard.Completed += (s, e) => Execute.OnUIThread(() => OnClose(this));
				storyboard.Begin();
			});
		}

		public void ResetTimeout(MouseEventArgs e)
		{
			_currentTime = _timeout;
			e.Handled = true;
		}


	}
}
