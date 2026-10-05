using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.Core.Notifications;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

/// <summary>
/// Gives the completion presenter its Avalonia calls: the text-input and completion-list subscriptions, the window activation policy,
/// and the text-measurement inputs.
/// </summary>
/// <remarks>
/// <para>
/// These are the spots where the engines disagree on names and shapes: Avalonia raises the routed text-input and pointer-released events,
/// and its formatted text takes no DPI factor. The bindings declare the same members, so the shared presenter reads one set of names.
/// </para>
/// <para>
/// A completion window is a <c>Popup</c> hosted inside the editor's window, so a click in the list cannot activate a separate top-level window
/// and take focus from the editor. No platform hook is needed or available, so <see cref="EnsureNonActivatable"/> does nothing; the completion list
/// installs its own pointer-accept handling.
/// </para>
/// </remarks>
internal static class CompletionSeam
{
	/// <include file="../../../../../shared/docs/CompletionSeam.xml" path="doc/members/member[@name='SubscribeTunnelingTextInput']/*"/>
	internal static IDisposable SubscribeTunnelingTextInput(TextArea textArea, Action<TextInputEventArgs> handler)
	{
		void tunnelHandler(object? _, TextInputEventArgs e) => handler(e);

		// The text area handles the bubbling pass of the event itself and marks it handled, so an instance handler
		// on that pass never sees typed text; the tunnel pass runs before it and before TextEntering.
		textArea.AddHandler(InputElement.TextInputEvent, tunnelHandler, RoutingStrategies.Tunnel);

		return new ActionSubscription(() => textArea.RemoveHandler(InputElement.TextInputEvent, tunnelHandler));
	}

	/// <include file="../../../../../shared/docs/CompletionSeam.xml" path="doc/members/member[@name='SubscribeItemClick']/*"/>
	internal static IDisposable SubscribeItemClick(ListBox listBox, Action<ListBox, PointerReleasedEventArgs> handler)
	{
		void clickHandler(object? _, PointerReleasedEventArgs e) => handler(listBox, e);

		// Subscribe on the tunnel stage so a class handler that marks the bubbling release handled cannot skip
		// the explicit click-selection.
		listBox.AddHandler(InputElement.PointerReleasedEvent, clickHandler, RoutingStrategies.Tunnel);

		return new ActionSubscription(() => listBox.RemoveHandler(InputElement.PointerReleasedEvent, clickHandler));
	}

	/// <include file="../../../../../shared/docs/CompletionSeam.xml" path="doc/members/member[@name='FindClickedItem']/*"/>
	/// <remarks>
	/// The items control exposes no container-from-element helper, so the item container is resolved by walking the visual
	/// tree from the clicked element, which also covers non-visual content such as a <c>Run</c> in an inline-based item template. Only a left-button
	/// release counts.
	/// </remarks>
	internal static object? FindClickedItem(ListBox listBox, PointerReleasedEventArgs e)
	{
		if (e.Handled || e.InitialPressMouseButton != MouseButton.Left)
			return null;

		if (e.Source is not Visual source)
			return null;

		ListBoxItem? listBoxItem = source.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();

		return listBoxItem is null ? null : listBox.ItemFromContainer(listBoxItem);
	}

	/// <include file="../../../../../shared/docs/CompletionSeam.xml" path="doc/members/member[@name='SetContentWidth']/*"/>
	/// <remarks>
	/// The completion window is a popup that draws no chrome of its own, so the width is applied to its completion list.
	/// </remarks>
	internal static void SetContentWidth(CompletionWindow window, double width) => window.CompletionList.Width = width;

	/// <include file="../../../../../shared/docs/CompletionSeam.xml" path="doc/members/member[@name='CreateWindow']/*"/>
	/// <remarks>
	/// The window is a popup that draws no chrome of its own; the visible frame and the height cap belong to its completion list, so the theme
	/// is applied there.
	/// </remarks>
	internal static CompletionWindow CreateWindow(TextArea textArea, CompletionWindowTheme theme, double maxHeight)
	{
		var window = new CompletionWindow(textArea);
		CompletionList completionList = window.CompletionList;
		completionList.Background = theme.Background;
		completionList.Foreground = theme.Foreground;
		completionList.BorderBrush = theme.BorderBrush;
		completionList.BorderThickness = new Thickness(theme.BorderThickness);
		completionList.MaxHeight = maxHeight;

		return window;
	}

	/// <include file="../../../../../shared/docs/CompletionSeam.xml" path="doc/members/member[@name='EnsureNonActivatable']/*"/>
	/// <remarks>
	/// Does nothing, because the popup cannot activate a separate window.
	/// </remarks>
	internal static void EnsureNonActivatable(CompletionWindow window) => _ = window;

	/// <include file="../../../../../shared/docs/CompletionSeam.xml" path="doc/members/member[@name='GetPixelsPerDip']/*"/>
	/// <remarks>
	/// Always 1, because Avalonia measures text in device-independent units.
	/// </remarks>
	internal static double GetPixelsPerDip(TextArea textArea)
	{
		_ = textArea;
		return 1.0;
	}

	/// <include file="../../../../../shared/docs/CompletionSeam.xml" path="doc/members/member[@name='CreateFormattedText']/*"/>
	internal static FormattedText CreateFormattedText(TextArea textArea, string text, Typeface typeface, double pixelsPerDip)
	{
		_ = pixelsPerDip;

		// CurrentUICulture is used deliberately so measurement follows the UI language rather than the
		// formatting culture FormattedText defaults to.
		return new FormattedText(
			textToFormat: text,
			culture: CultureInfo.CurrentUICulture,
			flowDirection: textArea.FlowDirection,
			typeface,
			emSize: textArea.FontSize,
			foreground: textArea.Foreground
		);
	}
}
