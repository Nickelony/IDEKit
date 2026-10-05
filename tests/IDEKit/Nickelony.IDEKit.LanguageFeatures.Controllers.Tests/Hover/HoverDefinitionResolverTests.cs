using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Navigation;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.Testing;
using static Nickelony.IDEKit.LanguageFeatures.Controllers.Tests.DefinitionTestHelpers;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

[TestClass]
public sealed class HoverDefinitionResolverTests
{
	[TestMethod]
	public void Resolve_HoveredSymbol_ResolvesThroughTheDefinitionProvider()
	{
		var definitionProvider = new RecordingDefinitionProvider(LocationAt(3, 8));
		var hoverProvider = new StubHoverProvider(new HoverInformation("hover") { SymbolName = "charlie" });

		DefinitionNavigationTarget? target = Resolve(definitionProvider, hoverProvider, snapshot: CreateSnapshot(), offset: 0);

		// The hover probe composes into a symbol request over the same snapshot text.
		Assert.IsNotNull(target);
		Assert.IsNotNull(definitionProvider.LastRequest);
		Assert.AreEqual(DocumentText, definitionProvider.LastRequest.DocumentText);
		Assert.AreEqual("charlie", definitionProvider.LastRequest.SymbolName);
		Assert.IsNull(definitionProvider.LastRequest.Discriminator);
		Assert.AreEqual(new TextPosition(2, 7), target.DocumentStart);
	}

	[TestMethod]
	public void Resolve_HoverDiscriminator_ReachesTheDefinitionRequest()
	{
		var discriminator = new TestDiscriminator("variable");
		var definitionProvider = new RecordingDefinitionProvider(LocationAt(3, 8));
		var hoverProvider = new StubHoverProvider(new HoverInformation("hover") { SymbolName = "charlie", DefinitionDiscriminator = discriminator });

		DefinitionNavigationTarget? target = Resolve(definitionProvider, hoverProvider, snapshot: CreateSnapshot(), offset: 0);

		Assert.IsNotNull(target);
		Assert.IsNotNull(definitionProvider.LastRequest);
		Assert.AreSame(discriminator, definitionProvider.LastRequest.Discriminator);
	}

	[TestMethod]
	public void Resolve_OffsetOutsideSnapshot_ReturnsNullWithoutCallingTheProviders()
	{
		var definitionProvider = new RecordingDefinitionProvider(LocationAt(1));
		var hoverProvider = new StubHoverProvider(new HoverInformation("hover") { SymbolName = "alpha" });

		ITextSnapshot snapshot = CreateSnapshot();

		Assert.IsNull(Resolve(definitionProvider, hoverProvider, snapshot, offset: -1));
		Assert.IsNull(Resolve(definitionProvider, hoverProvider, snapshot, offset: snapshot.TextLength + 1));
		Assert.IsNull(definitionProvider.LastRequest);
	}

	[TestMethod]
	public void Resolve_HoverWithoutASymbol_ReturnsNullWithoutCallingTheDefinitionProvider()
	{
		var definitionProvider = new RecordingDefinitionProvider(LocationAt(1));

		// A hover result without a symbol reports no navigation before the provider is consulted. The hover
		// payload normalizes a blank symbol name to null, so both spellings of "no symbol at the position"
		// arrive here as the same state.
		Assert.IsNull(
			Resolve(definitionProvider, hoverProvider: new StubHoverProvider(new HoverInformation("hover")), snapshot: CreateSnapshot(), offset: 0)
		);

		Assert.IsNull(
			Resolve(
				definitionProvider,
				hoverProvider: new StubHoverProvider(new HoverInformation("hover") { SymbolName = "   " }),
				snapshot: CreateSnapshot(),
				offset: 0
			)
		);

		Assert.IsNull(definitionProvider.LastRequest);
	}

	[TestMethod]
	public void Resolve_HoverProviderWithoutAResult_ReturnsNull()
	{
		Assert.IsNull(
			Resolve(
				definitionProvider: new RecordingDefinitionProvider(LocationAt(1)),
				hoverProvider: new StubHoverProvider(null),
				snapshot: CreateSnapshot(),
				offset: 0
			)
		);
	}

	[TestMethod]
	public async Task ResolveAsync_HoveredSymbol_ResolvesThroughTheDefinitionResolver()
	{
		DefinitionRequest? definitionRequest = null;

		DefinitionNavigationTarget? target = await HoverDefinitionResolver.ResolveAsync(
			definitionResolverAsync: (request, _) =>
			{
				definitionRequest = request;
				return Task.FromResult<DefinitionLocation?>(LocationAt(3, 8));
			},

			hoverResolverAsync: (_, _) => Task.FromResult<HoverInformation?>(new HoverInformation("hover") { SymbolName = "charlie" }),
			DocumentId,
			snapshot: CreateSnapshot(),
			offset: 0,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.IsNotNull(target);
		Assert.IsNotNull(definitionRequest);
		Assert.AreEqual(DocumentText, definitionRequest.DocumentText);
		Assert.AreEqual("charlie", definitionRequest.SymbolName);
		Assert.AreEqual(new TextPosition(2, 7), target.DocumentStart);
	}

	[TestMethod]
	public async Task ResolveAsync_DefinitionRequest_CarriesTheHoveredOffsetAndTheDocumentId()
	{
		HoverRequest? hoverRequest = null;
		DefinitionRequest? definitionRequest = null;

		await HoverDefinitionResolver.ResolveAsync(
			definitionResolverAsync: (request, _) =>
			{
				definitionRequest = request;
				return Task.FromResult<DefinitionLocation?>(LocationAt(1));
			},

			hoverResolverAsync: (request, _) =>
			{
				hoverRequest = request;
				return Task.FromResult<HoverInformation?>(new HoverInformation("hover") { SymbolName = "bravo" });
			},

			DocumentId,
			snapshot: CreateSnapshot(),
			offset: 24,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.IsNotNull(hoverRequest);
		Assert.IsNotNull(definitionRequest);
		Assert.AreEqual(DocumentId, hoverRequest.DocumentId);
		Assert.AreEqual(24, hoverRequest.HoveredOffset);
		Assert.AreEqual(DocumentId, definitionRequest.DocumentId);
		Assert.AreEqual(24, definitionRequest.SymbolOffset);
	}

	[TestMethod]
	public async Task ResolveAsync_HoverWithoutASymbol_ReturnsNullWithoutCallingTheDefinitionResolver()
	{
		bool definitionResolverCalled = false;
		bool hoverResolverCalled = false;

		// The offset is outside the snapshot, so neither resolver runs.
		Assert.IsNull(
			await HoverDefinitionResolver.ResolveAsync(
				definitionResolverAsync: (_, _) =>
				{
					definitionResolverCalled = true;
					return Task.FromResult<DefinitionLocation?>(LocationAt(1));
				},

				hoverResolverAsync: (_, _) =>
				{
					hoverResolverCalled = true;
					return Task.FromResult<HoverInformation?>(new HoverInformation("hover") { SymbolName = "alpha" });
				},

				DocumentId,
				snapshot: CreateSnapshot(),
				offset: -1,
				cancellationToken: TestContext.CancellationToken
			)
		);

		Assert.IsFalse(definitionResolverCalled);
		Assert.IsFalse(hoverResolverCalled);

		hoverResolverCalled = false;

		// A hover result without a symbol still stops before the definition resolver.
		Assert.IsNull(
			await HoverDefinitionResolver.ResolveAsync(
				definitionResolverAsync: (_, _) =>
				{
					definitionResolverCalled = true;
					return Task.FromResult<DefinitionLocation?>(LocationAt(1));
				},

				hoverResolverAsync: (_, _) =>
				{
					hoverResolverCalled = true;
					return Task.FromResult<HoverInformation?>(new HoverInformation("hover"));
				},

				DocumentId,
				snapshot: CreateSnapshot(),
				offset: 0,
				cancellationToken: TestContext.CancellationToken
			)
		);

		Assert.IsTrue(hoverResolverCalled);
		Assert.IsFalse(definitionResolverCalled);
	}

	[TestMethod]
	public async Task ResolveAsync_PreCanceledToken_ThrowsOperationCanceledException()
	{
		using var cancellationTokenSource = new CancellationTokenSource();
		cancellationTokenSource.Cancel();

		await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
			HoverDefinitionResolver.ResolveAsync(
				definitionResolverAsync: (_, _) => Task.FromResult<DefinitionLocation?>(LocationAt(1)),
				hoverResolverAsync: (_, _) => Task.FromResult<HoverInformation?>(new HoverInformation("hover") { SymbolName = "alpha" }),
				DocumentId,
				snapshot: CreateSnapshot(),
				offset: 0,
				cancellationToken: cancellationTokenSource.Token
			)
		);
	}

	[TestMethod]
	public async Task ResolveAsync_NullArguments_Throw()
	{
		ITextSnapshot snapshot = CreateSnapshot();

		Task<DefinitionLocation?> definitionResolver(DefinitionRequest _1, CancellationToken _2) =>
			Task.FromResult<DefinitionLocation?>(LocationAt(1));

		Task<HoverInformation?> hoverResolver(HoverRequest _1, CancellationToken _2) =>
			Task.FromResult<HoverInformation?>(new HoverInformation("hover") { SymbolName = "alpha" });

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
			HoverDefinitionResolver.ResolveAsync(
				definitionResolverAsync: null!,
				hoverResolverAsync: hoverResolver,
				DocumentId,
				snapshot,
				offset: 0,
				cancellationToken: TestContext.CancellationToken
			)
		);

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
			HoverDefinitionResolver.ResolveAsync(
				definitionResolverAsync: definitionResolver,
				hoverResolverAsync: null!,
				DocumentId,
				snapshot,
				offset: 0,
				cancellationToken: TestContext.CancellationToken
			)
		);

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
			HoverDefinitionResolver.ResolveAsync(
				definitionResolverAsync: definitionResolver,
				hoverResolverAsync: hoverResolver,
				DocumentId,
				snapshot: null!,
				offset: 0,
				cancellationToken: TestContext.CancellationToken
			)
		);
	}

	private static DefinitionNavigationTarget? Resolve(
		RecordingDefinitionProvider definitionProvider,
		StubHoverProvider hoverProvider,
		ITextSnapshot snapshot,
		int offset
	)
	{
		return HoverDefinitionResolver
			.ResolveAsync(
				definitionResolverAsync: definitionProvider.GetDefinitionAsync,
				hoverResolverAsync: hoverProvider.GetHoverAsync,
				DocumentId,
				snapshot,
				offset
			)
			.GetAwaiter()
			.GetResult();
	}

	public required TestContext TestContext { get; set; }
}
