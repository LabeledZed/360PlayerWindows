using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interactivity;

public class InputBindingTrigger : TriggerBase<FrameworkElement>, ICommand
{
	public static readonly DependencyProperty InputBindingProperty =
	  DependencyProperty.Register("InputBinding", typeof(InputBinding)
		, typeof(InputBindingTrigger)
		, new UIPropertyMetadata(null));

	public InputBinding InputBinding
	{
		get { return (InputBinding)GetValue(InputBindingProperty); }
		set { SetValue(InputBindingProperty, value); }
	}

	public event EventHandler CanExecuteChanged = delegate { };

	public bool CanExecute(object parameter)
	{
		// action is anyway blocked by Caliburn at the invoke level
		return true;
	}

	public void Execute(object parameter)
	{

		InvokeActions(parameter);
	}

	private Window _attachedWindow;

	protected override void OnAttached()
	{
		if (InputBinding != null)
		{
			InputBinding.Command = this;
			if (AssociatedObject.Focusable)
			{
				if (!AssociatedObject.InputBindings.Contains(InputBinding))
					AssociatedObject.InputBindings.Add(InputBinding);
			}
			else
			{
				AssociatedObject.Loaded += OnAssociatedObjectLoaded;
				AssociatedObject.Unloaded += OnAssociatedObjectUnloaded;
				if (AssociatedObject.IsLoaded)
				{
					AttachToWindow();
				}
			}
		}
		base.OnAttached();
	}

	protected override void OnDetaching()
	{
		if (AssociatedObject != null)
		{
			AssociatedObject.Loaded -= OnAssociatedObjectLoaded;
			AssociatedObject.Unloaded -= OnAssociatedObjectUnloaded;
			if (InputBinding != null)
				AssociatedObject.InputBindings.Remove(InputBinding);
		}
		DetachFromWindow();
		base.OnDetaching();
	}

	private void OnAssociatedObjectLoaded(object sender, RoutedEventArgs e)
	{
		AttachToWindow();
	}

	private void OnAssociatedObjectUnloaded(object sender, RoutedEventArgs e)
	{
		DetachFromWindow();
	}

	private void AttachToWindow()
	{
		if (AssociatedObject == null || InputBinding == null)
			return;
		_attachedWindow = Window.GetWindow(AssociatedObject);
		if (_attachedWindow != null && !_attachedWindow.InputBindings.Contains(InputBinding))
		{
			_attachedWindow.InputBindings.Add(InputBinding);
		}
	}

	private void DetachFromWindow()
	{
		if (_attachedWindow != null && InputBinding != null)
		{
			_attachedWindow.InputBindings.Remove(InputBinding);
			_attachedWindow = null;
		}
	}
}