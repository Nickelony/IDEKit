#if AVALONIAEDIT
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionTestHelpers;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>Verifies how an answer that arrives after the caret moved on is opened or discarded.</summary>
public sealed partial class CompletionControllerFactoryTests
{
	// The request offset is the end of "string.", where a server names the members of the table.
	private const string MemberAccess = "string.";

	[TestMethod]
	public void RequestAsync_WordTypedWhileTheAnswerWasPending_OpensTheListFilteredByThatWord()
	{
		using var host = LateAnswerHost.Start();

		host.Type("re");
		host.Answer();

		CompletionWindow window = host.OpenWindow();

		// The window spans the typed word, so the list filters by it and selects its best match, as a list opened before the typing would.
		Assert.AreEqual(MemberAccess.Length, window.StartOffset);
		Assert.AreEqual(MemberAccess.Length + 2, window.EndOffset);
		Assert.AreEqual(1, window.CompletionList.ListBox.Items.Count);
		Assert.AreEqual("rep", ((ICompletionData)window.CompletionList.ListBox.SelectedItem!).Text);
	}

	[TestMethod]
	public void RequestAsync_NothingTypedWhileTheAnswerWasPending_OpensTheUnfilteredList()
	{
		using var host = LateAnswerHost.Start();

		host.Answer();

		CompletionWindow window = host.OpenWindow();

		Assert.AreEqual(MemberAccess.Length, window.StartOffset);
		Assert.AreEqual(MemberAccess.Length, window.EndOffset);
		Assert.AreEqual(3, window.CompletionList.ListBox.Items.Count);
	}

	[TestMethod]
	public void RequestAsync_WordTypedWhileTheAnswerWasPending_CommitsTheItemOverTheTypedWord()
	{
		using var host = LateAnswerHost.Start();

		host.Type("re");
		host.Answer();

		CompletionWindow window = host.OpenWindow();
		window.CompletionList.RequestInsertion(EventArgs.Empty);

		// The item's own range ends where the request was made, and the typed word is part of the replaced text.
		Assert.AreEqual("string.rep", host.Hosted.Editor.Text);
	}

	/// <summary>A controller whose first request is held back, so the test types or moves the caret before the provider answers.</summary>
	private sealed class LateAnswerHost : IDisposable
	{
		private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

		private LateAnswerHost()
		{
			var provider = new ScriptedCompletionProvider
			{
				Answer = async (_, _) =>
				{
					await _gate.Task.ConfigureAwait(true);
					return [MemberItem("byte"), MemberItem("rep"), MemberItem("find")];
				},
			};

			// A long debounce keeps the requests that the typed letters schedule from superseding the pending one.
			Hosted = CreateHostedEditorController(
				options: s_fastOptions with
				{
					RequestDebounceDelay = TimeSpan.FromHours(1.0),
				},

				text: MemberAccess,
				configureEditor: editor => editor.TextArea.Caret.Offset = MemberAccess.Length,
				provider: provider
			);

			Request = Hosted.Controller.RequestAsync();
		}

		internal (
			TextEditor Editor,
			CompletionController Controller,
			CompletionWindowPresenter Presenter,
			HostWindow HostWindow,
			CompletionPresenter Completion
		) Hosted { get; }

		internal Task<bool> Request { get; }

		internal static LateAnswerHost Start() => new();

		internal void Type(string text) => Hosted.Editor.TextArea.PerformTextInput(text);

		/// <summary>Lets the provider answer and waits until the controller has applied or discarded the answer.</summary>
		internal void Answer()
		{
			_gate.SetResult();
			DispatcherTestHelpers.PumpUntil(() => Request.IsCompleted);
		}

		/// <summary>Returns the window the answer opened, after the initial selection ran.</summary>
		internal CompletionWindow OpenWindow()
		{
			Assert.IsTrue(Request.GetAwaiter().GetResult());

			CompletionWindow window = Hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The answer opened no window.");

			// The initial selection is posted at context-idle priority after the window opens.
			DispatcherTestHelpers.PumpUntil(
				() => window.CompletionList.ListBox.SelectedItem is ICompletionData { Text: "rep" or "byte" },
				timeout: null,
				priority: DispatcherPriority.ApplicationIdle
			);

			return window;
		}

		public void Dispose()
		{
			_gate.TrySetResult();

			Hosted.Controller.Dispose();
			Hosted.HostWindow.Dispose();
		}

		private static CompletionItem MemberItem(string label) =>
			new(label) { TextEdit = new CompletionTextEdit(new TextRange(MemberAccess.Length, 0), newText: label) };
	}
}
