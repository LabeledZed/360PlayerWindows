using Caliburn.Micro;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Bivrost.Bivrost360Player
{
	public abstract class FadeMenuBase : Screen
	{
		private Control view;
		private DateTime lastMouse;
		private DispatcherTimer visibilityTimer;
		private bool visible = false;

		protected override void OnViewLoaded(object view)
		{
			base.OnViewLoaded(view);
			this.view = view as Control;
			if (this.view != null)
			{
				this.view.Opacity = 0;
				this.view.IsHitTestVisible = false;
				this.view.MouseMove += MouseMove;
			}
		}

		protected override void OnDeactivate(bool close)
		{
			visibilityTimer?.Stop();
			if (this.view != null)
				this.view.MouseMove -= MouseMove;
			base.OnDeactivate(close);
		}

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
			Hide(0.5f);
		}

		public void Hide(float duration)
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
						Animate(false, duration);
					}
				}
			});
		}

		public void ToggleVisibility()
		{
			if (visible) Hide();
			else Show();
		}

		private void Animate(bool visible, float duration = 0.5f)
		{
			if (view == null) return;
			Storyboard storyboard = new Storyboard();
			DoubleAnimation opacityAnimation = new DoubleAnimation() { From = view.Opacity, To = visible ? 1 : 0, Duration = TimeSpan.FromSeconds(duration) };
			Storyboard.SetTarget(opacityAnimation, view);
			Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath("Opacity"));
			storyboard.Children.Add(opacityAnimation);
			storyboard.Begin();
		}

		public void MouseMove(object sender, MouseEventArgs eventArgs)
		{
			lastMouse = DateTime.Now;
		}

	}
}
