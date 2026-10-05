#if AVALONIAEDIT
using System.Runtime.CompilerServices;
using Avalonia;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Hover;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Runtime.CompilerServices;
using System.Windows;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Hover;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>
/// Verifies that the pointer source tracks the pointer events of a real text area and resolves the tracked position through its mapping
/// on every read. The controller that consumes the source is covered by the controller tests, which run without an editor.
/// </summary>
[STATestClass]
public sealed class HoverPointerSourceTests
{
	[TestMethod]
	public void GetHoveredOffset_BeforeAnyPointerMove_ReturnsNull()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		int mappingCalls = 0;

		using var source = new HoverPointerSource(
			editor.TextArea,

			offsetFromPoint: _ =>
			{
				mappingCalls++;
				return 3;
			}
		);

		// A source that has not seen the pointer yet has no position to map, so it reports no offset without asking the mapping.
		Assert.IsNull(source.GetHoveredOffset());
		Assert.AreEqual(0, mappingCalls);
	}

	[TestMethod]
	public void GetHoveredOffset_AfterAPointerMove_ResolvesTheTrackedPositionThroughTheMapping()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		int mappingCalls = 0;

		using var source = new HoverPointerSource(
			editor.TextArea,

			offsetFromPoint: _ =>
			{
				mappingCalls++;
				return 3;
			}
		);

		TestHoverPointerEvents.RaiseMoved(editor.TextArea);

		Assert.AreEqual(3, source.GetHoveredOffset());
		Assert.AreEqual(1, mappingCalls);
	}

	[TestMethod]
	public void GetHoveredOffset_AfterAHandledPointerMove_StillTracksThePosition()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		using var source = new HoverPointerSource(editor.TextArea, offsetFromPoint: static _ => 3);

		// A child that consumes a pointer move must not leave a stale position behind, so the source listens to handled events too.
		TestHoverPointerEvents.RaiseMoved(editor.TextArea, handled: true);

		Assert.AreEqual(3, source.GetHoveredOffset());
	}

	[TestMethod]
	public void GetHoveredOffset_ResolvesThePositionAgainOnEveryRead()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		int answer = 3;

		using var source = new HoverPointerSource(editor.TextArea, offsetFromPoint: _ => answer);

		TestHoverPointerEvents.RaiseMoved(editor.TextArea);

		Assert.AreEqual(3, source.GetHoveredOffset());

		// The pointer did not move, but a scroll or an edit moved the offset under it, which the next read must see.
		answer = 9;

		Assert.AreEqual(9, source.GetHoveredOffset());
	}

	[TestMethod]
	public void GetHoveredOffset_MappingWithoutAnOffset_ReturnsNull()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		using var source = new HoverPointerSource(editor.TextArea, offsetFromPoint: static _ => null);

		TestHoverPointerEvents.RaiseMoved(editor.TextArea);

		Assert.IsNull(source.GetHoveredOffset());
	}

	[TestMethod]
	public void GetHoveredOffset_AfterThePointerLeft_ReturnsNull()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		using var source = new HoverPointerSource(editor.TextArea, offsetFromPoint: static _ => 3);

		TestHoverPointerEvents.RaiseMoved(editor.TextArea);
		TestHoverPointerEvents.RaiseLeft(editor.TextArea);

		// A hover that arrives after the pointer left, with no move since, has no target even though the last position still maps to a line.
		Assert.IsNull(source.GetHoveredOffset());
	}

	[TestMethod]
	public void GetHoveredOffset_AfterThePointerLeftAndMovedBack_ResolvesAgain()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		using var source = new HoverPointerSource(editor.TextArea, offsetFromPoint: static _ => 3);

		TestHoverPointerEvents.RaiseMoved(editor.TextArea);
		TestHoverPointerEvents.RaiseLeft(editor.TextArea);
		TestHoverPointerEvents.RaiseMoved(editor.TextArea);

		Assert.AreEqual(3, source.GetHoveredOffset());
	}

	[TestMethod]
	public void GetHoveredOffset_AfterDisposal_ReturnsNullAndTracksNothing()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		var source = new HoverPointerSource(editor.TextArea, offsetFromPoint: static _ => 3);

		TestHoverPointerEvents.RaiseMoved(editor.TextArea);
		source.Dispose();

		// Disposal clears the tracked position and detaches the handlers, so a later move cannot bring it back.
		TestHoverPointerEvents.RaiseMoved(editor.TextArea);

		Assert.IsNull(source.GetHoveredOffset());
	}

	[TestMethod]
	public void DocumentVersion_AdvancesOnEveryTextChangeAndDocumentSwap()
	{
		TextEditor editor = TestHost.CreateEditor("sample");
		TextDocument first = editor.Document;

		using var source = new HoverPointerSource(editor.TextArea, offsetFromPoint: static _ => 3);

		int initial = source.DocumentVersion;

		first.Insert(0, "x");
		int afterEdit = source.DocumentVersion;

		editor.Document = new TextDocument("other");
		int afterSwap = source.DocumentVersion;

		// The text area shows another document now, so a change of the swapped-out one leaves the version where it is.
		first.Insert(0, "y");
		int afterOldDocumentEdit = source.DocumentVersion;

		editor.Document.Insert(0, "z");

		Assert.IsGreaterThan(initial, afterEdit);
		Assert.IsGreaterThan(afterEdit, afterSwap);
		Assert.AreEqual(afterSwap, afterOldDocumentEdit);
		Assert.IsGreaterThan(afterOldDocumentEdit, source.DocumentVersion);
	}

	[TestMethod]
	public void Dispose_DetachesTheHandlers_SoTheTextAreaDoesNotKeepTheSourceAlive()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		WeakReference sourceReference = CreateAndDisposeSource(editor.TextArea);

		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();

		// The text area outlives the source, so a pointer handler that disposal left attached would keep the source reachable.
		Assert.IsFalse(sourceReference.IsAlive);

		GC.KeepAlive(editor);
	}

	// The source is created and disposed in a frame of its own, so no local of the calling test keeps it reachable.
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference CreateAndDisposeSource(TextArea textArea)
	{
		var source = new HoverPointerSource(textArea, offsetFromPoint: static _ => 3);
		source.Dispose();

		return new WeakReference(source);
	}
}
