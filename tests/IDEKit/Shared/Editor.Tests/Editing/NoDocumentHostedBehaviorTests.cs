#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.AutoClosing;
using Nickelony.IDEKit.AvaloniaEdit.Editing;
using Nickelony.IDEKit.Core.AutoClosing;
using static Nickelony.IDEKit.AvaloniaEdit.Tests.EditingTestHelpers;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.AutoClosing;
using Nickelony.IDEKit.AvalonEdit.Editing;
using Nickelony.IDEKit.Core.AutoClosing;
using static Nickelony.IDEKit.AvalonEdit.Tests.EditingTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

/// <summary>
/// Verifies the auto-closing helper's behavior on an editor without a document when the input is delivered through a real host window.
/// </summary>
/// <remarks>
/// The non-hosted no-document cases live in <see cref="NoDocumentOperationTests"/>.
/// </remarks>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class NoDocumentHostedBehaviorTests
{
	private static TextEditor CreateEditorWithoutDocument() => new() { Document = null };

	[TestMethod]
	public void TryDeletePair_EditorWithoutDocument_ReturnsFalse()
	{
		var editor = CreateEditorWithoutDocument();
		var service = new AutoClosingService();

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var e = CreateBackspaceArgs(hostWindow);

		// An editor without a document has no pair to delete, so the editor's own Backspace applies.
		Assert.IsFalse(service.TryDeletePair(editor, e, AutoClosingOptions.Default));
		Assert.IsFalse(e.Handled);
	}
}
