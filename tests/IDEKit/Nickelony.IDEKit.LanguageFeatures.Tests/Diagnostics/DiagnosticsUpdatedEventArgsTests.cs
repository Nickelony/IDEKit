using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class DiagnosticsUpdatedEventArgsTests
{
	[TestMethod]
	public void Constructor_NullElement_ThrowsArgumentException() =>
		Assert.ThrowsExactly<ArgumentException>(() => new DiagnosticsUpdatedEventArgs("a.lua", [null!]));

	[TestMethod]
	public void Constructor_StoresDocumentIdAndCopiesDiagnostics()
	{
		IReadOnlyList<Diagnostic> diagnostics =
		[
			new Diagnostic(severity: DiagnosticSeverity.Warning, message: "message", range: TextRange.FromBounds(0, 1)),
		];

		var eventArgs = new DiagnosticsUpdatedEventArgs("doc.lua", diagnostics);

		Assert.AreEqual("doc.lua", eventArgs.DocumentId);

		// The payload owns a defensive copy, so it is a distinct instance with the same contents and stays valid
		// after the source collection is released.
		Assert.AreNotSame(diagnostics, eventArgs.Diagnostics);
		Assert.AreSequenceEqual([.. diagnostics], [.. eventArgs.Diagnostics]);
	}

	[TestMethod]
	public void Constructor_EmptyDiagnostics_AreAccepted()
	{
		var eventArgs = new DiagnosticsUpdatedEventArgs("doc.lua", []);
		Assert.IsEmpty(eventArgs.Diagnostics);
	}
}
