#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Diagnostics;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Diagnostics;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>
/// Verifies that the factory wires a diagnostics controller to a real text area: the renderer, the edit notifications and the anchored lookup.
/// The pull and push ordering is covered by the controller tests, which run without an editor.
/// </summary>
/// <remarks>
/// The editors are never shown in a window, so the class also runs in a headless leg.
/// </remarks>
[STATestClass]
public sealed class DiagnosticsControllerFactoryTests
{
	private const string DocumentId = "diagnostics-factory.lua";

	private static readonly DiagnosticsControllerOptions s_fastOptions = DiagnosticsControllerOptions.Default with
	{
		SettleDelay = TimeSpan.FromMilliseconds(1.0),
	};

	[TestMethod]
	public void Create_ConfiguresTheRendererBeforeAddingIt_AndDisposeRemovesIt()
	{
		TextEditor editor = TestHost.CreateEditor("call error here");
		bool? attachedWhenConfigured = null;

		DiagnosticsController controller = DiagnosticsControllerFactory.Create(
			textArea: editor.TextArea,
			DocumentId,
			provider: new ErrorWordProvider(),
			source: null,
			configureRenderer: renderer => attachedWhenConfigured = editor.TextArea.TextView.BackgroundRenderers.Contains(renderer),
			options: s_fastOptions
		);

		Assert.IsFalse(attachedWhenConfigured);
		Assert.ContainsSingle(editor.TextArea.TextView.BackgroundRenderers.OfType<DiagnosticsRenderer>());

		controller.Dispose();

		Assert.IsEmpty(editor.TextArea.TextView.BackgroundRenderers.OfType<DiagnosticsRenderer>());
	}

	[TestMethod]
	public void Pull_Underlines_AndFindAtFollowsAnEditBeforeTheNextAnswer()
	{
		TextEditor editor = TestHost.CreateEditor("call error here");
		var provider = new ErrorWordProvider();

		using DiagnosticsController controller = DiagnosticsControllerFactory.Create(
			textArea: editor.TextArea,
			DocumentId,
			provider,
			source: null,
			options: s_fastOptions
		);

		DispatcherTestHelpers.PumpUntil(() => controller.Current.Count == 1);

		Diagnostic error = controller.Current[0];

		Assert.AreSame(error, controller.FindAt(6));

		// The edit moves the underline with its text at once, before the pull it schedules answers, and the answers carry the moved range
		// while Current keeps the producer's.
		editor.Document.Insert(0, "xx");

		Diagnostic? moved = controller.FindAt(8);

		Assert.IsNotNull(moved);
		Assert.AreEqual(new TextRange(7, 5), moved.Range);
		Assert.AreEqual(error.Message, moved.Message);
		Assert.IsTrue(controller.TryGetShownRange(0, out TextRange shown));
		Assert.AreEqual(new TextRange(7, 5), shown);
		Assert.AreEqual(new TextRange(5, 5), controller.Current[0].Range);
		Assert.IsNull(controller.FindAt(5));

		DispatcherTestHelpers.PumpUntil(() => provider.Requests.Count == 2 && controller.Current.Count == 1 && controller.Current[0] != error);

		Assert.AreEqual("xxcall error here", provider.Requests[^1].DocumentText);
		Assert.AreEqual(new TextRange(7, 5), controller.Current[0].Range);
	}

	[TestMethod]
	public void Pull_AfterTheFaultyWordIsDeleted_RemovesTheUnderline()
	{
		TextEditor editor = TestHost.CreateEditor("call error here");
		var provider = new ErrorWordProvider();

		using DiagnosticsController controller = DiagnosticsControllerFactory.Create(
			textArea: editor.TextArea,
			DocumentId,
			provider,
			source: null,
			options: s_fastOptions
		);

		DispatcherTestHelpers.PumpUntil(() => controller.Current.Count == 1);

		editor.Document.Remove(5, 5);

		// The deletion removes the underline's text at once, and the next answer reports nothing.
		Assert.IsNull(controller.FindAt(5));
		Assert.IsFalse(controller.TryGetShownRange(0, out _));

		DispatcherTestHelpers.PumpUntil(() => provider.Requests.Count == 2 && controller.Current.Count == 0);
	}

	[TestMethod]
	public void Push_FromASource_Underlines()
	{
		TextEditor editor = TestHost.CreateEditor("call error here");
		var source = new StaticDiagnosticsSource(new Diagnostic(DiagnosticSeverity.Error, "pushed", TextRange.FromBounds(5, 10)));

		using DiagnosticsController controller = DiagnosticsControllerFactory.Create(
			textArea: editor.TextArea,
			DocumentId,
			provider: null,
			source,
			options: s_fastOptions
		);

		DispatcherTestHelpers.PumpUntil(() => controller.Current.Count == 1);

		Assert.AreSame(controller.Current[0], controller.FindAt(9));
	}

	[TestMethod]
	public void Dispose_StopsPullingOnEdits()
	{
		TextEditor editor = TestHost.CreateEditor("call error here");
		var provider = new ErrorWordProvider();

		DiagnosticsController controller = DiagnosticsControllerFactory.Create(
			textArea: editor.TextArea,
			DocumentId,
			provider,
			source: null,
			options: s_fastOptions
		);

		DispatcherTestHelpers.PumpUntil(() => provider.Requests.Count == 1);

		controller.Dispose();

		editor.Document.Insert(0, "error ");
		editor.Document = new TextDocument("error");
		DispatcherTestHelpers.PumpFrames(TimeSpan.FromMilliseconds(40.0));

		Assert.HasCount(1, provider.Requests);
	}

	[TestMethod]
	public void DocumentSwap_ClearsTheUnderlines_AndPullsNothingMore()
	{
		TextEditor editor = TestHost.CreateEditor("call error here");
		var provider = new ErrorWordProvider();

		using DiagnosticsController controller = DiagnosticsControllerFactory.Create(
			textArea: editor.TextArea,
			DocumentId,
			provider,
			source: null,
			options: s_fastOptions
		);

		DispatcherTestHelpers.PumpUntil(() => controller.Current.Count == 1);

		int changes = 0;
		controller.Changed += (_, _) => changes++;

		TextDocument first = editor.Document;
		editor.Document = new TextDocument("an error");

		Assert.IsEmpty(controller.Current);
		Assert.AreEqual(1, changes);
		Assert.IsNull(controller.FindAt(4));

		// Neither document's edits pull, because a pull would carry the new document's text under the old document's id.
		first.Insert(0, "ignored ");
		editor.Document.Insert(0, "now ");

		DispatcherTestHelpers.PumpFrames(TimeSpan.FromMilliseconds(40.0));

		Assert.HasCount(1, provider.Requests);
		Assert.IsEmpty(controller.Current);
	}

	[TestMethod]
	public void DocumentSwap_IgnoresTheSourcesLaterPushes()
	{
		TextEditor editor = TestHost.CreateEditor("call error here");
		var source = new FakeDiagnosticsSource();

		using DiagnosticsController controller = DiagnosticsControllerFactory.Create(
			textArea: editor.TextArea,
			DocumentId,
			provider: null,
			source,
			options: s_fastOptions
		);

		source.Raise(new Diagnostic(DiagnosticSeverity.Error, "pushed", TextRange.FromBounds(5, 10)));
		DispatcherTestHelpers.PumpUntil(() => controller.Current.Count == 1);

		editor.Document = new TextDocument("an error");
		source.Raise(new Diagnostic(DiagnosticSeverity.Error, "pushed again", TextRange.FromBounds(3, 8)));
		DispatcherTestHelpers.PumpFrames(TimeSpan.FromMilliseconds(40.0));

		Assert.IsEmpty(controller.Current);
		Assert.AreEqual(0, source.SubscriberCount);
	}

	[TestMethod]
	public void Dispose_LeavesTheControllerCollectable_WhileTheEditorStaysAlive()
	{
		TextEditor editor = TestHost.CreateEditor("call error here");

		WeakReference controller = CreateAndDispose(editor);

		DispatcherTestHelpers.PumpFrames(TimeSpan.FromMilliseconds(40.0));

		for (int attempt = 0; attempt < 5 && controller.IsAlive; attempt++)
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
		}

		Assert.IsFalse(controller.IsAlive, "The editor still holds the disposed controller.");
		Assert.IsEmpty(editor.TextArea.TextView.BackgroundRenderers.OfType<DiagnosticsRenderer>());

		GC.KeepAlive(editor);
	}

	[TestMethod]
	public void Create_WithoutAProviderOrASource_ThrowsAndLeavesNoRenderer()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		Assert.ThrowsExactly<ArgumentException>(() =>
			DiagnosticsControllerFactory.Create(textArea: editor.TextArea, DocumentId, provider: null, source: null)
		);

		Assert.IsEmpty(editor.TextArea.TextView.BackgroundRenderers.OfType<DiagnosticsRenderer>());
	}

	// The controller lives only in this frame, so after it returns the editor's references are the only ones that could keep it alive.
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	private static WeakReference CreateAndDispose(TextEditor editor)
	{
		var provider = new ErrorWordProvider();

		DiagnosticsController controller = DiagnosticsControllerFactory.Create(
			textArea: editor.TextArea,
			DocumentId,
			provider,
			source: null,
			options: s_fastOptions
		);

		DispatcherTestHelpers.PumpUntil(() => controller.Current.Count == 1);
		editor.Document.Insert(0, "x");

		controller.Dispose();

		return new WeakReference(controller);
	}

	[TestMethod]
	public void Create_InvalidArguments_Throw()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			DiagnosticsControllerFactory.Create(textArea: null!, DocumentId, provider: new ErrorWordProvider(), source: null)
		);

		Assert.ThrowsExactly<ArgumentException>(() =>
			DiagnosticsControllerFactory.Create(textArea: editor.TextArea, documentId: " ", provider: new ErrorWordProvider(), source: null)
		);

		Assert.IsEmpty(editor.TextArea.TextView.BackgroundRenderers.OfType<DiagnosticsRenderer>());
	}

	/// <summary>Reports an error on each occurrence of the word "error" in the text it is asked about.</summary>
	private sealed class ErrorWordProvider : IDiagnosticsProvider
	{
		internal List<DiagnosticsRequest> Requests { get; } = [];

		public Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(DiagnosticsRequest request, CancellationToken cancellationToken = default)
		{
			Requests.Add(request);

			List<Diagnostic> diagnostics = [];

			for (
				int offset = request.DocumentText.IndexOf("error", StringComparison.Ordinal);
				offset >= 0;
				offset = request.DocumentText.IndexOf("error", offset + 1, StringComparison.Ordinal)
			)
			{
				diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "An error.", new TextRange(offset, 5)));
			}

			return Task.FromResult<IReadOnlyList<Diagnostic>>(diagnostics);
		}
	}

	/// <summary>Raises the updates a test pushes for the test document, and starts with none.</summary>
	private sealed class FakeDiagnosticsSource : IDiagnosticsSource
	{
		private EventHandler<DiagnosticsUpdatedEventArgs>? _updated;

		public event EventHandler<DiagnosticsUpdatedEventArgs>? DiagnosticsUpdated
		{
			add => _updated += value;
			remove => _updated -= value;
		}

		internal int SubscriberCount => _updated?.GetInvocationList().Length ?? 0;

		public IReadOnlyList<Diagnostic> GetDiagnostics(string documentId) => [];

		internal void Raise(params Diagnostic[] diagnostics) => _updated?.Invoke(this, new DiagnosticsUpdatedEventArgs(DocumentId, diagnostics));
	}

	private sealed class StaticDiagnosticsSource(params Diagnostic[] diagnostics) : IDiagnosticsSource
	{
		public event EventHandler<DiagnosticsUpdatedEventArgs>? DiagnosticsUpdated
		{
			add { }
			remove { }
		}

		public IReadOnlyList<Diagnostic> GetDiagnostics(string documentId) => documentId == DocumentId ? diagnostics : [];
	}
}
