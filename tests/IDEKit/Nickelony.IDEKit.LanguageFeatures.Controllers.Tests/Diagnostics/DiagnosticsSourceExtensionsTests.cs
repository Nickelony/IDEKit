using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

[TestClass]
public sealed class DiagnosticsSourceExtensionsTests
{
	private static readonly Diagnostic s_error = new(DiagnosticSeverity.Error, "error", new TextRange(0, 1));
	private static readonly Diagnostic s_warning = new(DiagnosticSeverity.Warning, "warning", new TextRange(2, 1));

	[TestMethod]
	public void Subscribe_DeliversTheCurrentDiagnosticsFirst_ThenEachUpdateOfTheDocument_OnTheDispatcher()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);
		var source = new RecordingDiagnosticsSource();

		source.Current["a.lua"] = [s_error];
		List<IReadOnlyList<Diagnostic>> delivered = [];

		using IDisposable subscription = source.Subscribe("a.lua", dispatcher, delivered.Add);

		// Nothing runs on the caller's turn: the deliveries wait for the dispatcher.
		Assert.IsEmpty(delivered);
		dispatcher.PumpUntilIdle();
		Assert.AreSequenceEqual([s_error], [.. delivered.Single()]);

		source.Raise("b.lua", [s_warning]);
		source.Raise("a.lua", [s_warning]);

		dispatcher.PumpUntilIdle();

		Assert.HasCount(2, delivered);
		Assert.AreSequenceEqual([s_warning], [.. delivered[1]]);
	}

	[TestMethod]
	public void Subscribe_MatchesTheDocumentThroughTheComparer()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);
		var source = new RecordingDiagnosticsSource();

		List<IReadOnlyList<Diagnostic>> delivered = [];

		using IDisposable subscription = source.Subscribe(
			documentId: "A.lua",
			dispatcher,
			onChanged: delivered.Add,
			documentIdComparer: StringComparer.OrdinalIgnoreCase
		);

		source.Raise("a.lua", [s_error]);
		dispatcher.PumpUntilIdle();

		Assert.HasCount(2, delivered);
	}

	[TestMethod]
	public void Dispose_DropsAnUpdateThatWasPostedBeforeIt_AndUnsubscribes()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);
		var source = new RecordingDiagnosticsSource();

		List<IReadOnlyList<Diagnostic>> delivered = [];

		IDisposable subscription = source.Subscribe("a.lua", dispatcher, delivered.Add);
		source.Raise("a.lua", [s_error]);
		subscription.Dispose();
		dispatcher.PumpUntilIdle();

		Assert.IsEmpty(delivered);
		Assert.AreEqual(0, source.HandlerCount);
	}

	[TestMethod]
	public void Subscribe_WhenTheCurrentReadThrows_RemovesItsHandlerAndRethrows()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);
		var source = new RecordingDiagnosticsSource { ReadFailure = new InvalidOperationException("The source failed.") };

		Assert.ThrowsExactly<InvalidOperationException>(() => source.Subscribe("a.lua", dispatcher, static _ => { }));
		Assert.AreEqual(0, source.HandlerCount);
	}

	private sealed class RecordingDiagnosticsSource : IDiagnosticsSource
	{
		public event EventHandler<DiagnosticsUpdatedEventArgs>? DiagnosticsUpdated;

		internal Dictionary<string, IReadOnlyList<Diagnostic>> Current { get; } = [];

		internal Exception? ReadFailure { get; init; }

		internal int HandlerCount => DiagnosticsUpdated?.GetInvocationList().Length ?? 0;

		public IReadOnlyList<Diagnostic> GetDiagnostics(string documentId)
		{
			if (ReadFailure is { } failure)
				throw failure;

			return Current.TryGetValue(documentId, out IReadOnlyList<Diagnostic>? diagnostics) ? diagnostics : [];
		}

		internal void Raise(string documentId, IReadOnlyList<Diagnostic> diagnostics) =>
			DiagnosticsUpdated?.Invoke(this, new DiagnosticsUpdatedEventArgs(documentId, diagnostics));
	}
}
