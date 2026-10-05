using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

/// <summary>Verifies the code-action request record: storage, equality, and construction guards.</summary>
[TestClass]
public sealed class CodeActionRequestTests
{
	private const string DocumentId = "doc.lua";

	[TestMethod]
	public void Request_StoresDocumentAndRange()
	{
		var request = new CodeActionRequest(DocumentId, "text", new TextRange(1, 2));

		Assert.AreEqual("text", request.DocumentText);
		Assert.AreEqual(new TextRange(1, 2), request.Range);
	}

	[TestMethod]
	public void Request_Equality_ComparesEveryComponent()
	{
		var request = new CodeActionRequest(DocumentId, "text", new TextRange(1, 2));

		Assert.AreEqual(request, new CodeActionRequest(DocumentId, "text", new TextRange(1, 2)));
		Assert.AreNotEqual(request, new CodeActionRequest(DocumentId, "other", new TextRange(1, 2)));
		Assert.AreNotEqual(request, new CodeActionRequest(DocumentId, "text", new TextRange(0, 3)));
		Assert.AreNotEqual(request, new CodeActionRequest(DocumentId, "text", new TextRange(1, 3)));
	}

	[TestMethod]
	public void Request_InvalidRange_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new CodeActionRequest(DocumentId, null!, default));

		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			new CodeActionRequest(DocumentId, "text", new TextRange(0, 5))
		);

		Assert.AreEqual("range", exception.ParamName);
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CodeActionRequest(DocumentId, "text", new TextRange(3, 2)));
	}

	[TestMethod]
	public void Request_ContextDefaultsToNone()
	{
		var request = new CodeActionRequest(DocumentId, "text", new TextRange(1, 2));

		Assert.IsNull(request.Diagnostics);
		Assert.IsNull(request.Only);
	}

	[TestMethod]
	public void Request_Diagnostics_AreCopiedAndMayBeEmpty()
	{
		var diagnostic = new Diagnostic(DiagnosticSeverity.Error, "bad", new TextRange(0, 2));
		var diagnostics = new List<Diagnostic> { diagnostic };

		var request = new CodeActionRequest(DocumentId, "text", new TextRange(0, 2)) { Diagnostics = diagnostics };

		diagnostics.Clear();

		Assert.HasCount(1, request.Diagnostics!);
		Assert.AreSame(diagnostic, request.Diagnostics[0]);
		Assert.IsEmpty(new CodeActionRequest(DocumentId, "text", new TextRange(0, 2)) { Diagnostics = [] }.Diagnostics!);
	}

	[TestMethod]
	public void Request_DiagnosticsOutsideTheText_Throw()
	{
		var request = new CodeActionRequest(DocumentId, "text", new TextRange(0, 2));
		var outside = new Diagnostic(DiagnosticSeverity.Error, "bad", new TextRange(2, 5));

		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = request with { Diagnostics = [outside] });

		Assert.AreEqual("Diagnostics", exception.ParamName);
		Assert.ThrowsExactly<ArgumentException>(() => _ = request with { Diagnostics = [null!] });
	}

	[TestMethod]
	public void Request_Only_IsCopiedAndRejectsBlankKinds()
	{
		var kinds = new List<string> { "quickfix", "refactor" };
		var request = new CodeActionRequest(DocumentId, "text", new TextRange(0, 2)) { Only = kinds };

		kinds.Clear();

		Assert.AreSequenceEqual(["quickfix", "refactor"], request.Only);
		Assert.ThrowsExactly<ArgumentException>(() => _ = request with { Only = ["quickfix", " "] });
		Assert.ThrowsExactly<ArgumentException>(() => _ = request with { Only = [null!] });
	}

	[TestMethod]
	public void Request_Only_TrimsTheSurroundingWhitespaceOfEachKind()
	{
		var request = new CodeActionRequest(DocumentId, "text", new TextRange(0, 2)) { Only = [" quickfix ", "\trefactor.extract"] };
		Assert.AreSequenceEqual(["quickfix", "refactor.extract"], request.Only);
	}

	[TestMethod]
	public void Request_RangeAtTheDocumentEnd_IsAllowed()
	{
		// An offset equal to the document length is a valid caret position, and the empty document only
		// admits the zero range.
		var atEnd = new CodeActionRequest(DocumentId, "text", new TextRange(4, 0));

		Assert.AreEqual(new TextRange(4, 0), atEnd.Range);

		var empty = new CodeActionRequest(DocumentId, string.Empty, new TextRange(0, 0));

		Assert.AreEqual(0, empty.Range.EndOffset);
	}
}
