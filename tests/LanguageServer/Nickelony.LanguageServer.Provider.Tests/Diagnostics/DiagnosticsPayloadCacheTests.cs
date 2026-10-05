using System.Collections.ObjectModel;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class DiagnosticsPayloadCacheTests
{
	private static DiagnosticEntry CreateEntry(string message)
	{
		return new(
			new Diagnostic(DiagnosticSeverity.Warning, message, TextRange.FromBounds(0, 1)),
			new Lsp.DiagnosticPayload(Range: null, Severity: null, message, Source: null, Code: null)
		);
	}

	[TestMethod]
	public void GetProjection_BeforeAnyPayloadIsStored_IsEmpty()
	{
		var cache = new DiagnosticsPayloadCache();
		Assert.IsEmpty(cache.GetProjection());
	}

	[TestMethod]
	public void GetProjection_WhileTheStoredEntriesAreUnchanged_ReturnsTheSameInstance()
	{
		var cache = new DiagnosticsPayloadCache();

		Assert.IsTrue(cache.Entries.TryStore(1, [CreateEntry("first")]));

		ReadOnlyCollection<Diagnostic> first = cache.GetProjection();

		Assert.AreSame(first, cache.GetProjection());
		Assert.AreEqual("first", first[0].Message);
	}

	[TestMethod]
	public void GetProjection_AfterANewPayloadIsStored_ProjectsTheNewEntries()
	{
		var cache = new DiagnosticsPayloadCache();

		cache.Entries.TryStore(1, [CreateEntry("first")]);

		ReadOnlyCollection<Diagnostic> first = cache.GetProjection();

		cache.Entries.TryStore(2, [CreateEntry("second"), CreateEntry("third")]);

		ReadOnlyCollection<Diagnostic> second = cache.GetProjection();

		Assert.AreNotSame(first, second);
		Assert.AreSequenceEqual(["second", "third"], [.. second.Select(diagnostic => diagnostic.Message)]);
	}

	[TestMethod]
	public void AdoptProjection_ServesTheAdoptedInstanceUntilThePayloadChanges()
	{
		var cache = new DiagnosticsPayloadCache();

		DiagnosticEntry[] entries = [CreateEntry("first")];
		ReadOnlyCollection<Diagnostic> adopted = DiagnosticEntry.ProjectDiagnostics(entries);

		cache.Entries.TryStore(1, entries);
		cache.AdoptProjection(adopted);

		Assert.AreSame(adopted, cache.GetProjection());

		cache.Entries.TryStore(2, [CreateEntry("second")]);

		Assert.AreNotSame(adopted, cache.GetProjection());
	}

	[TestMethod]
	public void Clear_DropsTheEntriesAndTheProjection()
	{
		var cache = new DiagnosticsPayloadCache();

		cache.Entries.TryStore(1, [CreateEntry("first")]);
		Assert.HasCount(1, cache.GetProjection());

		cache.Clear();

		Assert.IsEmpty(cache.Entries.Items);
		Assert.IsEmpty(cache.GetProjection());
	}
}
