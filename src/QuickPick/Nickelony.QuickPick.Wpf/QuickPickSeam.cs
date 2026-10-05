using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Nickelony.QuickPick.Wpf;

/// <summary>Gives the shared quick-pick presenter its WPF calls: the popup, the list and its rows, the subscriptions, and the focus.</summary>
/// <remarks>
/// <para>
/// A WPF popup is a topmost window of its own, so the frame draws a drop shadow inside a transparent margin. The popup stays out of the logical
/// tree of the host, and it learns of the deactivation of the host window only through the window's event. The list is a <see cref="ListBox"/>
/// whose containers get the row visual from the presenter, because the neutral rows have no data template of their own. The two toolkit packages
/// declare the same members, so the shared presenter reads one set of names.
/// </para>
/// <para>
/// A host that no WPF <see cref="Window"/> owns, such as an <c>ElementHost</c>, has no window to report its deactivation. The loss of the keyboard
/// focus still dismisses the window there.
/// </para>
/// </remarks>
internal static class QuickPickSeam
{
	// The margin leaves room inside the transparent popup for the shadow of the frame.
	private const double ShadowMargin = 8.0;

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='CreatePopup']/*"/>
	internal static Popup CreatePopup(UIElement host, Border frame)
	{
		frame.Margin = new Thickness(ShadowMargin);

		frame.Effect = new DropShadowEffect
		{
			BlurRadius = 8,
			ShadowDepth = 2,
			Opacity = 0.3,
		};

		var popup = new Popup
		{
			PlacementTarget = host,
			Placement = PlacementMode.Custom,
			StaysOpen = false,
			AllowsTransparency = true,
			PopupAnimation = PopupAnimation.None,
			Child = frame,
		};

		// The frame is centered on the host. WPF adds the offsets of the popup to the point of a custom placement and reports a popup size without the
		// margin of the frame, so the callback takes that margin off on both axes, which puts the frame itself at the offset.
		popup.CustomPopupPlacementCallback = (popupSize, targetSize, _) =>
			[new CustomPopupPlacement(new Point(((targetSize.Width - popupSize.Width) / 2.0) - ShadowMargin, -ShadowMargin), PopupPrimaryAxis.None)];

		return popup;
	}

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='SetTopOffset']/*"/>
	internal static void SetTopOffset(Popup popup, double offset) => popup.VerticalOffset = offset;

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='ReleasePopup']/*"/>
	/// <remarks>
	/// A popup without a parent registers with its placement target, which then holds it, so clearing the target releases that registration.
	/// </remarks>
	internal static void ReleasePopup(Popup popup) => popup.PlacementTarget = null;

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='CreateList']/*"/>
	internal static ListBox CreateList(Func<QuickPickRow, UIElement> createRow)
	{
		var list = new RowListBox(createRow)
		{
			BorderThickness = new Thickness(0.0),
			Background = Brushes.Transparent,
			Focusable = false,
			SelectionMode = SelectionMode.Single,
		};

		ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);

		return list;
	}

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='SetItemTemplate']/*"/>
	internal static void SetItemTemplate(ListBox list, DataTemplate? template)
	{
		((RowListBox)list).UsesTemplate = template is not null;
		list.ItemTemplate = template;
	}

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='SetAccessibleName']/*"/>
	internal static void SetAccessibleName(UIElement element, string name) => AutomationProperties.SetName(element, name);

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='StyleActiveRow']/*"/>
	/// <remarks>
	/// The container template replaces the system chrome, which draws the selection in colors of its own, so the theme decides how the active
	/// row looks.
	/// </remarks>
	internal static void StyleActiveRow(ListBox list, Brush background, Brush foreground)
	{
		var chrome = new FrameworkElementFactory(typeof(Border), "Chrome");

		chrome.SetValue(Border.BackgroundProperty, Brushes.Transparent);
		chrome.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));

		var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = chrome };
		var selected = new Trigger { Property = Selector.IsSelectedProperty, Value = true };

		selected.Setters.Add(new Setter(Border.BackgroundProperty, background, "Chrome"));
		selected.Setters.Add(new Setter(TextElement.ForegroundProperty, foreground));
		template.Triggers.Add(selected);

		var style = new Style(typeof(ListBoxItem));

		style.Setters.Add(new Setter(Control.TemplateProperty, template));
		style.Setters.Add(new Setter(UIElement.FocusableProperty, false));

		list.ItemContainerStyle = style;
	}

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='SetVisible']/*"/>
	internal static void SetVisible(UIElement element, bool isVisible) => element.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='SetRuns']/*"/>
	internal static void SetRuns(TextBlock textBlock, IReadOnlyList<(string Text, bool IsBold)> runs)
	{
		textBlock.Inlines.Clear();

		foreach ((string text, bool isBold) in runs)
			textBlock.Inlines.Add(isBold ? new Run(text) { FontWeight = FontWeights.Bold } : new Run(text));
	}

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='ScrollIntoView']/*"/>
	internal static void ScrollIntoView(ListBox list, int index)
	{
		if (index >= 0 && index < list.Items.Count)
			list.ScrollIntoView(list.Items[index]);
	}

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='GetWidth']/*"/>
	internal static double GetWidth(UIElement host) => host is FrameworkElement element ? element.ActualWidth : 0.0;

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='FocusInput']/*"/>
	/// <remarks>
	/// The popup window exists once the popup opens, but the keyboard focus can move into it only after its content is laid out, so the focus
	/// is requested at input priority.
	/// </remarks>
	internal static void FocusInput(TextBox input)
	{
		input.Dispatcher.BeginInvoke(
			DispatcherPriority.Input,

			() =>
			{
				if (input.IsVisible)
				{
					input.Focus();
					Keyboard.Focus(input);
				}
			}
		);
	}

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='GetFocusedElement']/*"/>
	internal static object? GetFocusedElement(UIElement host) => Keyboard.FocusedElement;

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='RestoreFocus']/*"/>
	internal static void RestoreFocus(object? element, UIElement host)
	{
		if (element is UIElement { IsVisible: true, IsEnabled: true, Focusable: true } target)
			Keyboard.Focus(target);
	}

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='SubscribeInput']/*"/>
	internal static IDisposable SubscribeInput(TextBox input, ListBox list, Func<Key, bool> keyDown, Action textChanged, Action<int> rowClicked)
	{
		void keyHandler(object sender, KeyEventArgs e)
		{
			if (keyDown(e.Key))
				e.Handled = true;
		}

		void textHandler(object sender, TextChangedEventArgs e) => textChanged();

		// A press on a row is handled so the list does not select it, and its release accepts the item.
		void pressHandler(object sender, MouseButtonEventArgs e)
		{
			if (RowContainerOf(list, e.OriginalSource) is not null)
				e.Handled = true;
		}

		void releaseHandler(object sender, MouseButtonEventArgs e)
		{
			if (RowContainerOf(list, e.OriginalSource) is { } container)
			{
				e.Handled = true;
				rowClicked(list.ItemContainerGenerator.IndexFromContainer(container));
			}
		}

		input.PreviewKeyDown += keyHandler;
		input.TextChanged += textHandler;
		list.PreviewMouseLeftButtonDown += pressHandler;
		list.PreviewMouseLeftButtonUp += releaseHandler;

		return new QuickPickSubscription(() =>
		{
			input.PreviewKeyDown -= keyHandler;
			input.TextChanged -= textHandler;
			list.PreviewMouseLeftButtonDown -= pressHandler;
			list.PreviewMouseLeftButtonUp -= releaseHandler;
		});
	}

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='SubscribeDismissal']/*"/>
	/// <remarks>
	/// The keyboard focus leaving the window dismisses it too, which covers a click on another control of the host. The subscription starts
	/// before the focus enters the window, and a focus that never entered raises nothing.
	/// </remarks>
	internal static IDisposable SubscribeDismissal(UIElement host, Popup popup, Action dismiss)
	{
		var window = Window.GetWindow(host);
		UIElement? frame = popup.Child;

		void closedHandler(object? sender, EventArgs e) => dismiss();

		void focusHandler(object sender, DependencyPropertyChangedEventArgs e)
		{
			if (e.NewValue is false)
				dismiss();
		}

		popup.Closed += closedHandler;

		if (window is not null)
			window.Deactivated += closedHandler;

		if (frame is not null)
			frame.IsKeyboardFocusWithinChanged += focusHandler;

		return new QuickPickSubscription(() =>
		{
			popup.Closed -= closedHandler;

			if (window is not null)
				window.Deactivated -= closedHandler;

			if (frame is not null)
				frame.IsKeyboardFocusWithinChanged -= focusHandler;
		});
	}

	private static ListBoxItem? RowContainerOf(ListBox list, object originalSource) =>
		originalSource is DependencyObject source ? ItemsControl.ContainerFromElement(list, source) as ListBoxItem : null;

	// A list whose containers hold the row visual that the presenter writes, unless the host supplies a data template.
	private sealed class RowListBox(Func<QuickPickRow, UIElement> createRow) : ListBox
	{
		internal bool UsesTemplate { get; set; }

		protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
		{
			base.PrepareContainerForItemOverride(element, item);

			if (element is not ListBoxItem container || item is not QuickPickRow row)
				return;

			AutomationProperties.SetName(container, QuickPickRowEmitter.AccessibleNameOf(row));

			if (!UsesTemplate)
				container.Content = createRow(row);
		}
	}
}
