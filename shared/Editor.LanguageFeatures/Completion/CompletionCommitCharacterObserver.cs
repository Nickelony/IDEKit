using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Commit;
#if AVALONIAEDIT
using Avalonia.Input;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Editing;
#else
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Editing;
using TextInputEventArgs = System.Windows.Input.TextCompositionEventArgs;
#endif
#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

#endif
/// <summary>
/// Commits the selected completion item when its commit character is typed, so one keystroke accepts the item and inserts the character.
/// </summary>
/// <remarks>
/// <para>
/// The policy runs on the tunneling text-input stage of the text area (<see cref="CompletionSeam.SubscribeTunnelingTextInput"/> names
/// the stage of each engine), because that stage precedes the <c>TextEntering</c> stage the text area raises while it applies the character.
/// A <c>TextEntering</c> subscriber, such as an auto-closing service, therefore sees the committed item whatever its subscription order,
/// which a language change that rebuilds the presenter would otherwise flip at run time. The observer does not consume the event.
/// </para>
/// <para>
/// Programmatic input through <c>PerformTextInput</c> bypasses that stage and does not commit.
/// </para>
/// </remarks>
internal sealed class CompletionCommitCharacterObserver : IDisposable
{
	private readonly Func<CompletionWindow?> _getActiveWindow;
	private readonly bool _acceptOnCommitCharacters;
	private readonly IDisposable _subscription;

	/// <param name="textArea">The text area whose text input the observer watches.</param>
	/// <param name="getActiveWindow">Returns the shown completion window, or <see langword="null"/> when none is shown.</param>
	/// <param name="acceptOnCommitCharacters">Whether a typed commit character commits the selected item.</param>
	internal CompletionCommitCharacterObserver(TextArea textArea, Func<CompletionWindow?> getActiveWindow, bool acceptOnCommitCharacters)
	{
		_getActiveWindow = getActiveWindow;
		_acceptOnCommitCharacters = acceptOnCommitCharacters;
		_subscription = CompletionSeam.SubscribeTunnelingTextInput(textArea, HandleTextInput);
	}

	/// <summary>Removes the text-input subscription.</summary>
	public void Dispose() => _subscription.Dispose();

	private void HandleTextInput(TextInputEventArgs e)
	{
		if (_getActiveWindow() is not CompletionWindow completionWindow)
			return;

		var selected = completionWindow.CompletionList.SelectedItem as ICompletionCommitCharacters;

		bool shouldCommit = CompletionCommitPolicy.ShouldCommit(
			acceptOnCommitCharacters: _acceptOnCommitCharacters,
			isHandled: e.Handled,
			typedText: e.Text,
			selected
		);

		if (!shouldCommit)
			return;

		// The list closes the window and completes the selected item before the text pipeline inserts the
		// typed character at the caret the completion left behind.
		completionWindow.CompletionList.RequestInsertion(e);
	}
}
