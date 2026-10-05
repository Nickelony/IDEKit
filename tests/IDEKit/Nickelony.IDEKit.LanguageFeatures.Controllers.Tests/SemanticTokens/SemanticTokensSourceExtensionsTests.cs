using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.SemanticTokens;
using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

[TestClass]
public sealed class SemanticTokensSourceExtensionsTests
{
	[TestMethod]
	public void Subscribe_DeliversTheCurrentTokensFirst_ThenTheUpdatesOfTheDocumentOnly()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);
		var current = new SemanticToken(new TextRange(0, 5), "function");
		var updated = new SemanticToken(new TextRange(6, 5), "variable");
		var source = new RecordingSemanticTokensSource(current);

		List<IReadOnlyList<SemanticToken>> delivered = [];

		using IDisposable subscription = source.Subscribe("a.lua", dispatcher, delivered.Add);

		source.Raise("b.lua", [current]);
		source.Raise("a.lua", [updated]);

		dispatcher.PumpUntilIdle();

		Assert.HasCount(2, delivered);
		Assert.AreEqual(current, delivered[0].Single());
		Assert.AreEqual(updated, delivered[1].Single());
	}

	[TestMethod]
	public void Subscribe_WhenTheCurrentReadThrows_RemovesItsHandlerAndRethrows()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);

		var source = new RecordingSemanticTokensSource(new SemanticToken(new TextRange(0, 5), "function"))
		{
			ReadFailure = new InvalidOperationException("The source failed."),
		};

		Assert.ThrowsExactly<InvalidOperationException>(() => source.Subscribe("a.lua", dispatcher, static _ => { }));
		Assert.AreEqual(0, source.HandlerCount);
	}

	private sealed class RecordingSemanticTokensSource(SemanticToken current) : ISemanticTokensSource
	{
		public event EventHandler<SemanticTokensUpdatedEventArgs>? SemanticTokensUpdated;

		internal Exception? ReadFailure { get; init; }

		internal int HandlerCount => SemanticTokensUpdated?.GetInvocationList().Length ?? 0;

		public IReadOnlyList<SemanticToken> GetSemanticTokens(string documentId)
		{
			if (ReadFailure is { } failure)
				throw failure;

			return documentId == "a.lua" ? [current] : [];
		}

		internal void Raise(string documentId, IReadOnlyList<SemanticToken> tokens) =>
			SemanticTokensUpdated?.Invoke(this, new SemanticTokensUpdatedEventArgs(documentId, tokens));
	}
}
