using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace Bivrost.Bivrost360Player.WPF
{
	internal class GridLengthAnimation : AnimationTimeline
	{
		static GridLengthAnimation()
		{
			FromProperty = DependencyProperty.Register("From", typeof(GridLength),
				typeof(GridLengthAnimation));

			ToProperty = DependencyProperty.Register("To", typeof(GridLength),
				typeof(GridLengthAnimation));

			EasingFunctionProperty = DependencyProperty.Register("EasingFunction", typeof(EasingFunctionBase),
				typeof(GridLengthAnimation));
		}

		public override Type TargetPropertyType
		{
			get
			{
				return typeof(GridLength);
			}
		}

		protected override System.Windows.Freezable CreateInstanceCore()
		{
			return new GridLengthAnimation();
		}

		public static readonly DependencyProperty FromProperty;
		public GridLength From
		{
			get
			{
				return (GridLength)GetValue(GridLengthAnimation.FromProperty);
			}
			set
			{
				SetValue(GridLengthAnimation.FromProperty, value);
			}
		}

		public static readonly DependencyProperty ToProperty;
		public GridLength To
		{
			get
			{
				return (GridLength)GetValue(GridLengthAnimation.ToProperty);
			}
			set
			{
				SetValue(GridLengthAnimation.ToProperty, value);
			}
		}

		public static readonly DependencyProperty EasingFunctionProperty;
		public EasingFunctionBase EasingFunction
		{
			get
			{
				return (EasingFunctionBase)GetValue(GridLengthAnimation.EasingFunctionProperty);
			}
			set
			{
				SetValue(GridLengthAnimation.EasingFunctionProperty, value);
			}
		}


		public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
		{
			if (animationClock == null || !animationClock.CurrentProgress.HasValue)
				return defaultOriginValue;

			GridLength fromGridLength = (GridLength)GetValue(GridLengthAnimation.FromProperty);
			GridLength toGridLength = (GridLength)GetValue(GridLengthAnimation.ToProperty);

			double fromVal = fromGridLength.Value;
			double toVal = toGridLength.Value;
			double progress = animationClock.CurrentProgress.Value;
			if (EasingFunction != null)
				progress = EasingFunction.Ease(progress);

			GridUnitType unitType = toGridLength.IsStar ? GridUnitType.Star : GridUnitType.Pixel;

			double currentVal = fromVal > toVal
				? (1 - progress) * (fromVal - toVal) + toVal
				: progress * (toVal - fromVal) + fromVal;

			return new GridLength(Math.Max(0.0, currentVal), unitType);
		}
	}
}
