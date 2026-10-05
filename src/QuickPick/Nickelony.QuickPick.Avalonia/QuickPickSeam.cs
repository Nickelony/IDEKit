using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Nickelony.QuickPick.Avalonia;

/// <summary>Gives the shared quick-pick presenter its Avalonia calls: the popup, the list and its rows, the subscriptions, and the focus.</summary>
/// <remarks>
/// An Avalonia popup outside the logical tree takes no control themes, so the input and the list would render as empty frames;
/// the popup therefore takes the host as its logical parent. The list draws each row through a data template that calls the presenter, and the active
/// row takes its colors from a style that targets the selected container. The two toolkit packages declare the same members, so the shared presenter
/// reads one set of names.
/// </remarks>
internal static class QuickPickSeam
{
	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='CreatePopup']/*"/>
	internal static Popup CreatePopup(Control host, Border frame)
	{
		var popup = new Popup
		{
			PlacementTarget = host,
			Placement = PlacementMode.AnchorAndGravity,
			PlacementAnchor = PopupAnchor.Top,
			PlacementGravity = PopupGravity.Bottom,
			IsLightDismissEnabled = true,
			Child = frame,
		};

		((ISetLogicalParent)popup).SetParent(host);
		return popup;
	}

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='SetTopOffset']/*"/>
	internal static void SetTopOffset(Popup popup, double offset) => popup.VerticalOffset = offset;

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='ReleasePopup']/*"/>
	internal static void ReleasePopup(Popup popup)
	{
		((ISetLogicalParent)popup).SetParent(null);
		popup.PlacementTarget = null;
	}

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='CreateList']/*"/>
	internal static ListBox CreateList(Func<QuickPickRow, Control> createRow)
	{
		var list = new RowListBox(createRow)
		{
			BorderThickness = new Thickness(0.0),
			Background = Brushes.Transparent,
			Focusable = false,
			SelectionMode = SelectionMode.Single,
		};

		list.ItemTemplate = list.DefaultTemplate;

		ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);

		return list;
	}

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='SetItemTemplate']/*"/>
	internal static void SetItemTemplate(ListBox list, IDataTemplate? template) => list.ItemTemplate = template ?? ((RowListBox)list).DefaultTemplate;

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='SetAccessibleName']/*"/>
	internal static void SetAccessibleName(Control element, string name) => AutomationProperties.SetName(element, name);

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='StyleActiveRow']/*"/>
	/// <remarks>
	/// The styles target the content presenter of the selected container, whatever the pointer or the focus does to it, so the theme decides
	/// how the active row looks.
	/// </remarks>
	internal static void StyleActiveRow(ListBox list, IBrush background, IBrush foreground)
	{
		list.Styles.Clear();

		list.Styles.Add(SelectedStyle(selector => selector.Class(":selected"), background, foreground));
		list.Styles.Add(SelectedStyle(selector => selector.Class(":selected").Class(":pointerover"), background, foreground));
		list.Styles.Add(SelectedStyle(selector => selector.Class(":selected").Class(":focus"), background, foreground));
	}

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='SetVisible']/*"/>
	internal static void SetVisible(Control element, bool isVisible) => element.IsVisible = isVisible;

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='SetRuns']/*"/>
	internal static void SetRuns(TextBlock textBlock, IReadOnlyList<(string Text, bool IsBold)> runs)
	{
		List<Inline> inlines = [];

		foreach ((string text, bool isBold) in runs)
			inlines.Add(isBold ? new Run(text) { FontWeight = FontWeight.Bold } : new Run(text));

		textBlock.Inlines = [.. inlines];
	}

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='ScrollIntoView']/*"/>
	internal static void ScrollIntoView(ListBox list, int index) => list.ScrollIntoView(index);

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='GetWidth']/*"/>
	internal static double GetWidth(Control host) => host.Bounds.Width;

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='FocusInput']/*"/>
	/// <remarks>
	/// The popup content is laid out after the popup opens, so the focus is requested once the layout has run.
	/// </remarks>
	internal static void FocusInput(TextBox input)
	{
		Dispatcher.UIThread.Post(
			() =>
			{
				if (input.IsEffectivelyVisible)
					input.Focus();
			},

			DispatcherPriority.Loaded
		);
	}

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='GetFocusedElement']/*"/>
	internal static object? GetFocusedElement(Control host) => TopLevel.GetTopLevel(host)?.FocusManager?.GetFocusedElement();

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='RestoreFocus']/*"/>
	internal static void RestoreFocus(object? element, Control host)
	{
		if (element is InputElement { IsVisible: true, IsEffectivelyEnabled: true, Focusable: true } target)
			target.Focus();
	}

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='SubscribeInput']/*"/>
	internal static IDisposable SubscribeInput(TextBox input, ListBox list, Func<Key, bool> keyDown, Action textChanged, Action<int> rowClicked)
	{
		void keyHandler(object? sender, KeyEventArgs e)
		{
			if (keyDown(e.Key))
				e.Handled = true;
		}

		void textHandler(object? sender, TextChangedEventArgs e) => textChanged();

		// A press on a row is handled so the list neither selects the row nor takes the focus from the input, and its release accepts the item.
		void pressHandler(object? sender, PointerPressedEventArgs e)
		{
			if (RowContainerOf(e.Source) is not null)
				e.Handled = true;
		}

		void releaseHandler(object? sender, PointerReleasedEventArgs e)
		{
			if (e.InitialPressMouseButton == MouseButton.Left && RowContainerOf(e.Source) is { } container)
			{
				e.Handled = true;
				rowClicked(list.IndexFromContainer(container));
			}
		}

		input.AddHandler(InputElement.KeyDownEvent, keyHandler, RoutingStrategies.Tunnel);
		input.TextChanged += textHandler;

		list.AddHandler(InputElement.PointerPressedEvent, pressHandler, RoutingStrategies.Tunnel);
		list.AddHandler(InputElement.PointerReleasedEvent, releaseHandler, RoutingStrategies.Tunnel);

		return new QuickPickSubscription(() =>
		{
			input.RemoveHandler(InputElement.KeyDownEvent, keyHandler);
			input.TextChanged -= textHandler;

			list.RemoveHandler(InputElement.PointerPressedEvent, pressHandler);
			list.RemoveHandler(InputElement.PointerReleasedEvent, releaseHandler);
		});
	}

	/// <include file="../../../shared/docs/QuickPickSeam.xml" path="doc/members/member[@name='SubscribeDismissal']/*"/>
	/// <remarks>
	/// The keyboard focus leaving the window dismisses it too, which covers a click on another control of the host. The subscription starts
	/// before the focus enters the window, and a focus that never entered raises nothing.
	/// </remarks>
	internal static IDisposable SubscribeDismissal(Control host, Popup popup, Action dismiss)
	{
		var window = TopLevel.GetTopLevel(host) as WindowBase;
		Control? frame = popup.Child;

		void closedHandler(object? sender, EventArgs e) => dismiss();

		void focusHandler(object? sender, AvaloniaPropertyChangedEventArgs e)
		{
			if (e.Property == InputElement.IsKeyboardFocusWithinProperty && e.NewValue is false)
				dismiss();
		}

		popup.Closed += closedHandler;

		if (window is not null)
			window.Deactivated += closedHandler;

		if (frame is not null)
			frame.PropertyChanged += focusHandler;

		return new QuickPickSubscription(() =>
		{
			popup.Closed -= closedHandler;

			if (window is not null)
				window.Deactivated -= closedHandler;

			if (frame is not null)
				frame.PropertyChanged -= focusHandler;
		});
	}

	private static Style SelectedStyle(Func<Selector?, Selector> container, IBrush background, IBrush foreground)
	{
		return new Style(selector => container(selector.OfType<ListBoxItem>()).Template().OfType<ContentPresenter>())
		{
			Setters = { new Setter(ContentPresenter.BackgroundProperty, background), new Setter(ContentPresenter.ForegroundProperty, foreground) },
		};
	}

	private static ListBoxItem? RowContainerOf(object? source) =>
		source is Visual visual ? visual.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() : null;

	// A list whose default template draws each row through the presenter and whose containers carry the accessible name of their row.
	private sealed class RowListBox : ListBox
	{
		internal RowListBox(Func<QuickPickRow, Control> createRow)
		{
			// A container that leaves the list clears its content, and the template then builds from nothing, which draws nothing.
			DefaultTemplate = new FuncDataTemplate<QuickPickRow>((row, _) => row is null ? null : createRow(row));
		}

		internal IDataTemplate DefaultTemplate { get; }

		protected override Type StyleKeyOverride => typeof(ListBox);

		protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
		{
			base.PrepareContainerForItemOverride(container, item, index);

			if (item is QuickPickRow row)
				AutomationProperties.SetName(container, QuickPickRowEmitter.AccessibleNameOf(row));
		}
	}
}
