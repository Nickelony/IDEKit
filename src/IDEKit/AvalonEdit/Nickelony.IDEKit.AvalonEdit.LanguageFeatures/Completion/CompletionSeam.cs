using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.Core.Notifications;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

/// <summary>
/// Gives the completion presenter its WPF calls: the text-input and completion-list subscriptions, the window activation policy,
/// and the text-measurement inputs.
/// </summary>
/// <remarks>
/// The completion presenter is shared binding source, and these are the spots where the engines disagree on names and shapes: WPF raises the preview
/// text-input stage and the previewed mouse-up, a completion window is a separate top-level window that a Win32 hook keeps from activating,
/// and formatted text takes a DPI factor. This type is the WPF half of that seam; the bindings declare the same members, so the shared presenter
/// reads one set of names.
/// </remarks>
internal static class CompletionSeam
{
	/// <include file="../../../../../shared/docs/CompletionSeam.xml" path="doc/members/member[@name='SubscribeTunnelingTextInput']/*"/>
	internal static IDisposable SubscribeTunnelingTextInput(TextArea textArea, Action<TextCompositionEventArgs> handler)
	{
		void previewHandler(object _, TextCompositionEventArgs e) => handler(e);

		textArea.PreviewTextInput += previewHandler;

		return new ActionSubscription(() => textArea.PreviewTextInput -= previewHandler);
	}

	/// <include file="../../../../../shared/docs/CompletionSeam.xml" path="doc/members/member[@name='SubscribeItemClick']/*"/>
	internal static IDisposable SubscribeItemClick(ListBox listBox, Action<ListBox, MouseButtonEventArgs> handler)
	{
		void clickHandler(object _, MouseButtonEventArgs e) => handler(listBox, e);

		listBox.PreviewMouseLeftButtonUp += clickHandler;

		return new ActionSubscription(() => listBox.PreviewMouseLeftButtonUp -= clickHandler);
	}

	/// <include file="../../../../../shared/docs/CompletionSeam.xml" path="doc/members/member[@name='FindClickedItem']/*"/>
	/// <remarks>
	/// <c>ItemsControl.ContainerFromElement</c> resolves the item container from any clicked element, including non-visual content
	/// such as a <c>Run</c> in an inline-based item template; walking the visual tree instead would fail because <c>VisualTreeHelper</c> rejects
	/// elements that are not visuals.
	/// </remarks>
	internal static object? FindClickedItem(ListBox listBox, MouseButtonEventArgs e)
	{
		if (e.Handled || e.OriginalSource is not DependencyObject source)
			return null;

		return (ItemsControl.ContainerFromElement(listBox, source) as ListBoxItem)?.DataContext;
	}

	/// <include file="../../../../../shared/docs/CompletionSeam.xml" path="doc/members/member[@name='SetContentWidth']/*"/>
	internal static void SetContentWidth(CompletionWindow window, double width) => window.Width = width;

	/// <include file="../../../../../shared/docs/CompletionSeam.xml" path="doc/members/member[@name='CreateWindow']/*"/>
	/// <remarks>
	/// The window is a separate top-level window that draws its own frame, so the theme and the height cap go on the window.
	/// </remarks>
	internal static CompletionWindow CreateWindow(TextArea textArea, CompletionWindowTheme theme, double maxHeight)
	{
		return new(textArea)
		{
			ResizeMode = ResizeMode.NoResize,
			BorderThickness = new Thickness(theme.BorderThickness),
			Background = theme.Background,
			Foreground = theme.Foreground,
			BorderBrush = theme.BorderBrush,
			MaxHeight = maxHeight,
		};
	}

	/// <include file="../../../../../shared/docs/CompletionSeam.xml" path="doc/members/member[@name='EnsureNonActivatable']/*"/>
	/// <remarks>
	/// Installs the Win32 activation hook; see <see cref="CompletionWindowInterop"/>.
	/// </remarks>
	internal static void EnsureNonActivatable(CompletionWindow window) => CompletionWindowInterop.EnsureNonActivatable(window);

	/// <include file="../../../../../shared/docs/CompletionSeam.xml" path="doc/members/member[@name='GetPixelsPerDip']/*"/>
	internal static double GetPixelsPerDip(TextArea textArea) => VisualTreeHelper.GetDpi(textArea).PixelsPerDip;

	/// <include file="../../../../../shared/docs/CompletionSeam.xml" path="doc/members/member[@name='CreateFormattedText']/*"/>
	internal static FormattedText CreateFormattedText(TextArea textArea, string text, Typeface typeface, double pixelsPerDip)
	{
		// CurrentUICulture is used deliberately so measurement follows the UI language rather than the
		// formatting culture FormattedText defaults to.
		return new FormattedText(
			textToFormat: text,
			culture: CultureInfo.CurrentUICulture,
			flowDirection: textArea.FlowDirection,
			typeface,
			emSize: textArea.FontSize,
			foreground: textArea.Foreground,
			pixelsPerDip
		);
	}
}
