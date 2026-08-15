using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit.Editing;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

/// <summary>
/// Subscribes the completion controller's text-input and completion-list handlers through the Avalonia event
/// members and posts its dispatcher work through the Avalonia dispatcher API.
/// </summary>
/// <remarks>
/// The completion controller is shared binding source, and its event subscriptions are the spots where the
/// two engines disagree on names and shapes: Avalonia raises the routed text-input and pointer-released
/// events, while WPF raises the preview text-input stage and the previewed mouse-up; Avalonia posts
/// dispatcher work with <see cref="Dispatcher.Post(Action, DispatcherPriority)"/> while WPF begins an invoke
/// with a priority instead. This type is the Avalonia half of that seam; both bindings declare the same
/// members, so the shared controller reads one set of names.
/// </remarks>
internal static class TextCompletionHost
{
	/// <include file="../../../../../shared/docs/TextCompletionHost.xml" path="doc/members/member[@name='SubscribeInputStages']/*"/>
	public static IDisposable SubscribeInputStages(
		TextArea textArea,
		Action<TextInputEventArgs> previewStage,
		Action<TextInputEventArgs> enteringStage)
	{
		EventHandler<TextInputEventArgs> previewHandler = (_, e) => previewStage(e);
		EventHandler<TextInputEventArgs> enteringHandler = (_, e) => enteringStage(e);

		textArea.TextInput += previewHandler;
		textArea.TextEntering += enteringHandler;

		return new Subscription(() =>
		{
			textArea.TextInput -= previewHandler;
			textArea.TextEntering -= enteringHandler;
		});
	}

	/// <include file="../../../../../shared/docs/TextCompletionHost.xml" path="doc/members/member[@name='SubscribeItemClick']/*"/>
	public static IDisposable SubscribeItemClick(ListBox listBox, Action<ListBox, PointerReleasedEventArgs> handler)
	{
		EventHandler<PointerReleasedEventArgs> clickHandler = (_, e) => handler(listBox, e);

		listBox.PointerReleased += clickHandler;

		return new Subscription(() => listBox.PointerReleased -= clickHandler);
	}

	/// <include file="../../../../../shared/docs/TextCompletionHost.xml" path="doc/members/member[@name='Post']/*"/>
	public static void Post(Dispatcher dispatcher, Action callback, DispatcherPriority priority)
		=> dispatcher.Post(callback, priority);

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
