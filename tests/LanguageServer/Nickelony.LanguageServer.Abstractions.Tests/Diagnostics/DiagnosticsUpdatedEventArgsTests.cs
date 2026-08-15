using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.LanguageServer.Abstractions.Tests;

[TestClass]
public sealed class DiagnosticsUpdatedEventArgsTests
{
	[TestMethod]
	public void Constructor_StoresFilePathAndCopiesDiagnostics()
	{
		IReadOnlyList<TextDiagnostic> diagnostics =
		[
			new TextDiagnostic(TextDiagnosticSeverity.Warning, "message", 0, 1)
		];

		var eventArgs = new DiagnosticsUpdatedEventArgs("doc.lua", diagnostics);

		Assert.AreEqual("doc.lua", eventArgs.FilePath);

		// The payload owns a defensive copy, so it is a distinct instance with the same contents and stays valid
		// after the source collection is released.
		Assert.AreNotSame(diagnostics, eventArgs.Diagnostics);
		CollectionAssert.AreEqual(diagnostics.ToArray(), eventArgs.Diagnostics.ToArray());
	}

	[TestMethod]
	public void Constructor_EmptyDiagnostics_AreAccepted()
	{
		var eventArgs = new DiagnosticsUpdatedEventArgs("doc.lua", []);

		Assert.AreEqual(0, eventArgs.Diagnostics.Count);
	}
}
