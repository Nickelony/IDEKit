using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Navigation;
using Nickelony.IDEKit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.Testing;
using static Nickelony.IDEKit.LanguageFeatures.Controllers.Tests.DefinitionTestHelpers;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

[TestClass]
public sealed class DefinitionResolverTests
{
	[TestMethod]
	public void ResolveFromSymbol_ResolvesTheRequestOverTheSnapshot()
	{
		var definitionProvider = new RecordingDefinitionProvider(LocationAt(2, 8));
		var discriminator = new TestDiscriminator("kind");

		DefinitionNavigationTarget? target = ResolveFromSymbol(definitionProvider, snapshot: CreateSnapshot(), symbolName: "bravo", discriminator);

		Assert.IsNotNull(target);
		Assert.IsNotNull(definitionProvider.LastRequest);
		Assert.AreEqual(DocumentText, definitionProvider.LastRequest.DocumentText);
		Assert.AreEqual("bravo", definitionProvider.LastRequest.SymbolName);
		Assert.AreSame(discriminator, definitionProvider.LastRequest.Discriminator);
		Assert.AreEqual(new TextPosition(1, 7), target.DocumentStart);
	}

	[TestMethod]
	public void ResolveFromSymbol_BlankSymbolName_ReturnsNullWithoutCallingTheProvider()
	{
		var definitionProvider = new RecordingDefinitionProvider(LocationAt(1));

		Assert.IsNull(ResolveFromSymbol(definitionProvider, CreateSnapshot(), null));
		Assert.IsNull(ResolveFromSymbol(definitionProvider, CreateSnapshot(), string.Empty));
		Assert.IsNull(ResolveFromSymbol(definitionProvider, CreateSnapshot(), "   "));
		Assert.IsNull(definitionProvider.LastRequest);
	}

	[TestMethod]
	public void ResolveFromSymbol_ProviderWithoutALocation_ReturnsNull() =>
		Assert.IsNull(ResolveFromSymbol(new RecordingDefinitionProvider(null), CreateSnapshot(), "alpha"));

	[TestMethod]
	public void ResolveFromSymbol_LocationWithSelectionRange_UsesTheSelectionStart()
	{
		var targetRange = new TextPositionRange(new TextPosition(1, 0), new TextPosition(1, 15));
		var selectionRange = new TextPositionRange(new TextPosition(1, 7), new TextPosition(1, 12));
		var provider = new RecordingDefinitionProvider(new DefinitionLocation(targetRange, selectionRange: selectionRange));

		DefinitionNavigationTarget? target = ResolveFromSymbol(provider, CreateSnapshot(), "bravo");

		Assert.IsNotNull(target);
		Assert.AreEqual(new TextPosition(1, 7), target.DocumentStart);
	}

	[TestMethod]
	public void ResolveFromSymbol_ColumnBeyondTheLineEnd_StaysApplicable()
	{
		// A column past the end of its line is the one tolerance: the host clamps it when it applies the
		// position, so the resolver keeps the target.
		DefinitionNavigationTarget? target = ResolveFromSymbol(new RecordingDefinitionProvider(LocationAt(3, 200)), CreateSnapshot(), "charlie");

		Assert.IsNotNull(target);
		Assert.AreEqual(new TextPosition(2, 199), target.DocumentStart);
	}

	[TestMethod]
	public void ResolveFromSymbol_PositionOutsideTheDocument_ReturnsNull()
	{
		// The provider contract uses zero-based positions, so a line beyond the snapshot and a negative line or
		// character are absent rather than clamped.
		Assert.IsNull(ResolveFromSymbol(new RecordingDefinitionProvider(LocationAt(99)), CreateSnapshot(), "alpha"));
		Assert.IsNull(ResolveFromSymbol(new RecordingDefinitionProvider(LocationAt(0)), CreateSnapshot(), "alpha"));
		Assert.IsNull(ResolveFromSymbol(new RecordingDefinitionProvider(LocationAt(2, -5)), CreateSnapshot(), "bravo"));
	}

	[TestMethod]
	public void ResolveFromSymbol_CrossDocumentLocation_ReturnsATargetWithoutAnInDocumentStart()
	{
		DefinitionLocation location = LocationAt(99, documentId: "other.txt");

		DefinitionNavigationTarget? target = ResolveFromSymbol(new RecordingDefinitionProvider(location), CreateSnapshot(), "source");

		// A cross-document location is the host's to open, so its coordinates are not validated against a
		// document they do not address.
		Assert.IsNotNull(target);
		Assert.AreSame(location, target.Location);
		Assert.IsNull(target.DocumentStart);
	}

	[TestMethod]
	public void ResolveFromSymbol_BlankDocumentId_NormalizesToAnInDocumentTarget()
	{
		DefinitionNavigationTarget? target = ResolveFromSymbol(new RecordingDefinitionProvider(LocationAt(2, 8, "   ")), CreateSnapshot(), "bravo");

		Assert.IsNotNull(target);
		Assert.IsNull(target.Location.DocumentId);
		Assert.AreEqual(new TextPosition(1, 7), target.DocumentStart);
	}

	[TestMethod]
	public async Task ResolveFromSymbolAsync_DefinitionRequest_CarriesNoSymbolOffset()
	{
		DefinitionRequest? definitionRequest = null;

		await DefinitionResolver.ResolveFromSymbolAsync(
			definitionResolverAsync: (request, _) =>
			{
				definitionRequest = request;
				return Task.FromResult<DefinitionLocation?>(LocationAt(1));
			},

			DocumentId,
			snapshot: CreateSnapshot(),
			symbolName: "bravo",
			cancellationToken: TestContext.CancellationToken
		);

		Assert.IsNotNull(definitionRequest);
		Assert.AreEqual(DocumentId, definitionRequest.DocumentId);
		Assert.IsNull(definitionRequest.SymbolOffset);
	}

	[TestMethod]
	public async Task ResolveFromSymbolAsync_BlankSymbolName_ReturnsNullWithoutCallingTheResolver()
	{
		bool resolverCalled = false;

		Task<DefinitionLocation?> Resolver(DefinitionRequest request, CancellationToken cancellationToken)
		{
			resolverCalled = true;
			return Task.FromResult<DefinitionLocation?>(LocationAt(1));
		}

		Assert.IsNull(
			await DefinitionResolver.ResolveFromSymbolAsync(
				definitionResolverAsync: Resolver,
				DocumentId,
				snapshot: CreateSnapshot(),
				symbolName: null,
				cancellationToken: TestContext.CancellationToken
			)
		);

		Assert.IsNull(
			await DefinitionResolver.ResolveFromSymbolAsync(
				definitionResolverAsync: Resolver,
				DocumentId,
				snapshot: CreateSnapshot(),
				symbolName: "   ",
				cancellationToken: TestContext.CancellationToken
			)
		);

		Assert.IsFalse(resolverCalled);
	}

	[TestMethod]
	public async Task ResolveFromSymbolAsync_CrossDocumentLocation_ReturnsATargetWithoutAnInDocumentStart()
	{
		DefinitionLocation location = LocationAt(1, documentId: "other.txt");

		DefinitionNavigationTarget? target = await DefinitionResolver.ResolveFromSymbolAsync(
			definitionResolverAsync: (_, _) => Task.FromResult<DefinitionLocation?>(location),
			DocumentId,
			snapshot: CreateSnapshot(),
			symbolName: "source",
			cancellationToken: TestContext.CancellationToken
		);

		Assert.IsNotNull(target);
		Assert.AreSame(location, target.Location);
		Assert.IsNull(target.DocumentStart);
	}

	[TestMethod]
	public async Task ResolveFromAsync_EveryProbe_RejectsNullArguments()
	{
		ITextSnapshot snapshot = CreateSnapshot();

		static Task<DefinitionLocation?> definitionResolver(DefinitionRequest _1, CancellationToken _2) =>
			Task.FromResult<DefinitionLocation?>(LocationAt(1));

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
			DefinitionResolver.ResolveFromSymbolAsync(
				definitionResolverAsync: null!,
				DocumentId,
				snapshot,
				symbolName: "alpha",
				cancellationToken: TestContext.CancellationToken
			)
		);

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
			DefinitionResolver.ResolveFromSymbolAsync(
				definitionResolverAsync: definitionResolver,
				DocumentId,
				snapshot: null!,
				symbolName: "alpha",
				cancellationToken: TestContext.CancellationToken
			)
		);

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
			DefinitionResolver.ResolveAtOffsetAsync(
				definitionResolverAsync: null!,
				DocumentId,
				snapshot,
				offset: 0,
				cancellationToken: TestContext.CancellationToken
			)
		);

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
			DefinitionResolver.ResolveAtOffsetAsync(
				definitionResolverAsync: definitionResolver,
				DocumentId,
				snapshot: null!,
				offset: 0,
				cancellationToken: TestContext.CancellationToken
			)
		);
	}

	[TestMethod]
	public async Task ResolveAtOffsetAsync_DefinitionRequest_CarriesTheIdentifierAtTheOffsetAndTheOffset()
	{
		DefinitionRequest? definitionRequest = null;

		// The offset is the end of "bravo", where a caret rests after the name was typed.
		int offset = DocumentText.IndexOf("bravo", StringComparison.Ordinal) + "bravo".Length;

		await DefinitionResolver.ResolveAtOffsetAsync(
			definitionResolverAsync: (request, _) =>
			{
				definitionRequest = request;
				return Task.FromResult<DefinitionLocation?>(LocationAt(1));
			},

			DocumentId,
			snapshot: CreateSnapshot(),
			offset,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.IsNotNull(definitionRequest);
		Assert.AreEqual(DocumentId, definitionRequest.DocumentId);
		Assert.AreEqual("bravo", definitionRequest.SymbolName);
		Assert.AreEqual(offset, definitionRequest.SymbolOffset);
	}

	[TestMethod]
	public async Task ResolveAtOffsetAsync_ALocationThatNamesTheRequestingDocument_ResolvesInTheDocument()
	{
		// A language server names the document of every location, the requesting one included.
		DefinitionNavigationTarget? target = await DefinitionResolver.ResolveAtOffsetAsync(
			definitionResolverAsync: (_, _) => Task.FromResult<DefinitionLocation?>(LocationAt(3, 8, DocumentId)),
			DocumentId,
			snapshot: CreateSnapshot(),
			offset: 0,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.IsNotNull(target);
		Assert.AreEqual(new TextPosition(2, 7), target.DocumentStart);
	}

	[TestMethod]
	public async Task ResolveAtOffsetAsync_ALocationInAnotherDocument_ReturnsATargetWithoutAnInDocumentStart()
	{
		DefinitionNavigationTarget? target = await DefinitionResolver.ResolveAtOffsetAsync(
			definitionResolverAsync: (_, _) => Task.FromResult<DefinitionLocation?>(LocationAt(1, documentId: "other.txt")),
			DocumentId,
			snapshot: CreateSnapshot(),
			offset: 0,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.IsNotNull(target);
		Assert.IsNull(target.DocumentStart);
	}

	[TestMethod]
	public async Task ResolveAtOffsetAsync_OffsetOutsideTheSnapshot_ReturnsNullWithoutCallingTheResolver()
	{
		bool resolverCalled = false;

		DefinitionNavigationTarget? target = await DefinitionResolver.ResolveAtOffsetAsync(
			definitionResolverAsync: (_, _) =>
			{
				resolverCalled = true;
				return Task.FromResult<DefinitionLocation?>(LocationAt(1));
			},

			DocumentId,
			snapshot: CreateSnapshot(),
			offset: DocumentText.Length + 1,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.IsNull(target);
		Assert.IsFalse(resolverCalled);
	}

	[TestMethod]
	public void Target_NullLocation_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => new DefinitionNavigationTarget(null!));

	[TestMethod]
	public void Target_WithoutAnInDocumentStart_LeavesItNull()
	{
		var target = new DefinitionNavigationTarget(LocationAt(1, documentId: "other.txt"));
		Assert.IsNull(target.DocumentStart);
	}

	private static DefinitionNavigationTarget? ResolveFromSymbol(
		RecordingDefinitionProvider definitionProvider,
		ITextSnapshot snapshot,
		string? symbolName,
		DefinitionDiscriminator? discriminator = null
	)
	{
		return DefinitionResolver
			.ResolveFromSymbolAsync(definitionResolverAsync: definitionProvider.GetDefinitionAsync, DocumentId, snapshot, symbolName, discriminator)
			.GetAwaiter()
			.GetResult();
	}

	public TestContext? TestContext { get; set; }
}
