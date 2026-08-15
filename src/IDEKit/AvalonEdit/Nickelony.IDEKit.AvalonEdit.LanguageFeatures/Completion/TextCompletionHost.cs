using ICSharpCode.AvalonEdit.Editing;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

/// <summary>
/// Subscribes the completion controller's text-input and completion-list handlers through the WPF event
/// members and posts its dispatcher work through the WPF dispatcher API.
/// </summary>
/// <remarks>
/// The completion controller is shared binding source, and its event subscriptions are the spots where the
/// two engines disagree on names and shapes: WPF raises the preview text-input stage and the previewed
/// mouse-up, while Avalonia raises the routed text-input and pointer-released events; WPF posts dispatcher
/// work with <see cref="Dispatcher.BeginInvoke(Delegate, DispatcherPriority, object[])"/> while Avalonia
/// posts a callback to its dispatcher instead. This type is the WPF half of that seam; both bindings declare
/// the same members, so the shared controller reads one set of names.
/// </remarks>
internal static class TextCompletionHost
{
	/// <include file="../../../../../shared/docs/TextCompletionHost.xml" path="doc/members/member[@name='SubscribeInputStages']/*"/>
	public static IDisposable SubscribeInputStages(
		TextArea textArea,
		Action<TextCompositionEventArgs> previewStage,
		Action<TextCompositionEventArgs> enteringStage)
	{
		TextCompositionEventHandler previewHandler = (_, e) => previewStage(e);
		TextCompositionEventHandler enteringHandler = (_, e) => enteringStage(e);

		textArea.PreviewTextInput += previewHandler;
		textArea.TextEntering += enteringHandler;

		return new Subscription(() =>
		{
			textArea.PreviewTextInput -= previewHandler;
			textArea.TextEntering -= enteringHandler;
		});
	}

	/// <include file="../../../../../shared/docs/TextCompletionHost.xml" path="doc/members/member[@name='SubscribeItemClick']/*"/>
	public static IDisposable SubscribeItemClick(ListBox listBox, Action<ListBox, MouseButtonEventArgs> handler)
	{
		MouseButtonEventHandler clickHandler = (_, e) => handler(listBox, e);

		listBox.PreviewMouseLeftButtonUp += clickHandler;

		return new Subscription(() => listBox.PreviewMouseLeftButtonUp -= clickHandler);
	}

	/// <include file="../../../../../shared/docs/TextCompletionHost.xml" path="doc/members/member[@name='Post']/*"/>
	public static void Post(Dispatcher dispatcher, Action callback, DispatcherPriority priority)
		=> dispatcher.BeginInvoke(callback, priority);

	private sealed class Subscription : IDisposable
	{
		private Action? _unsubscribe;

		public Subscription(Action unsubscribe)
			=> _unsubscribe = unsubscribe;

		public void Dispose()
		{
			_unsubscribe?.Invoke();
			_unsubscribe = null;
		}
	}
}
