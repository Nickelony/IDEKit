using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Hover;
using Nickelony.IDEKit.IntelliSense.Navigation;
using Nickelony.IDEKit.IntelliSense.Tests.TestSupport;

namespace Nickelony.IDEKit.IntelliSense.Tests.Navigation;

[TestClass]
public sealed class TextDefinitionNavigatorTests
{
	private const string DocumentText = "sample alpha = 1\nsample bravo = 2\nsample charlie = 3";

	[TestMethod]
	public void ResolveFromHover_HoveredSymbol_ResolvesThroughTheDefinitionProvider()
	{
		var definitionProvider = new RecordingDefinitionProvider(LocationAt(3, 8));
		var hoverProvider = new StubHoverProvider(new TextHoverInfo("hover") { SymbolName = "charlie" });

		TextDefinitionNavigationTarget? target = TextDefinitionNavigator.ResolveFromHover(
			definitionProvider, hoverProvider, CreateSnapshot(), 0);

		// The hover probe composes into a symbol request over the same snapshot text.
		Assert.IsNotNull(target);
		Assert.IsNotNull(definitionProvider.LastRequest);
		Assert.AreEqual(DocumentText, definitionProvider.LastRequest.DocumentText);
		Assert.AreEqual("charlie", definitionProvider.LastRequest.SymbolName);
		Assert.IsNull(definitionProvider.LastRequest.Discriminator);
		Assert.AreEqual(new TextPosition(2, 7), target.DocumentStart);
	}

	[TestMethod]
	public void ResolveFromHover_HoverDiscriminator_ReachesTheDefinitionRequest()
	{
		var discriminator = new TestDiscriminator("variable");
		var definitionProvider = new RecordingDefinitionProvider(LocationAt(3, 8));
		var hoverProvider = new StubHoverProvider(
			new TextHoverInfo("hover") { SymbolName = "charlie", DefinitionDiscriminator = discriminator });

		TextDefinitionNavigationTarget? target = TextDefinitionNavigator.ResolveFromHover(
			definitionProvider, hoverProvider, CreateSnapshot(), 0);

		Assert.IsNotNull(target);
		Assert.IsNotNull(definitionProvider.LastRequest);
		Assert.AreSame(discriminator, definitionProvider.LastRequest.Discriminator);
	}

	[TestMethod]
	public void ResolveFromHover_OffsetOutsideSnapshot_ReturnsNullWithoutCallingTheProviders()
	{
		var definitionProvider = new RecordingDefinitionProvider(LocationAt(1));
		var hoverProvider = new StubHoverProvider(new TextHoverInfo("hover") { SymbolName = "alpha" });
		ITextSnapshot snapshot = CreateSnapshot();

		Assert.IsNull(TextDefinitionNavigator.ResolveFromHover(definitionProvider, hoverProvider, snapshot, -1));
		Assert.IsNull(TextDefinitionNavigator.ResolveFromHover(
			definitionProvider, hoverProvider, snapshot, snapshot.TextLength + 1));
		Assert.IsNull(definitionProvider.LastRequest);
	}

	[TestMethod]
	public void ResolveFromHover_HoverWithoutASymbol_ReturnsNullWithoutCallingTheDefinitionProvider()
	{
		var definitionProvider = new RecordingDefinitionProvider(LocationAt(1));

		// A hover result without a symbol reports no navigation before the provider is consulted. The hover
		// payload normalizes a blank symbol name to null, so both spellings of "no symbol at the position"
		// arrive here as the same state.
		Assert.IsNull(TextDefinitionNavigator.ResolveFromHover(
			definitionProvider,
			new StubHoverProvider(new TextHoverInfo("hover")),
			CreateSnapshot(),
			0));
		Assert.IsNull(TextDefinitionNavigator.ResolveFromHover(
			definitionProvider,
			new StubHoverProvider(new TextHoverInfo("hover") { SymbolName = "   " }),
			CreateSnapshot(),
			0));
		Assert.IsNull(definitionProvider.LastRequest);
	}

	[TestMethod]
	public void ResolveFromHover_HoverProviderWithoutAResult_ReturnsNull()
	{
		Assert.IsNull(TextDefinitionNavigator.ResolveFromHover(
			new RecordingDefinitionProvider(LocationAt(1)),
			new StubHoverProvider(null),
			CreateSnapshot(),
			0));
	}

	[TestMethod]
	public void ResolveFromSymbol_ResolvesTheRequestOverTheSnapshot()
	{
		var definitionProvider = new RecordingDefinitionProvider(LocationAt(2, 8));
		var discriminator = new TestDiscriminator("kind");

		TextDefinitionNavigationTarget? target = TextDefinitionNavigator.ResolveFromSymbol(
			definitionProvider, CreateSnapshot(), "bravo", discriminator);

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

		Assert.IsNull(TextDefinitionNavigator.ResolveFromSymbol(definitionProvider, CreateSnapshot(), null));
		Assert.IsNull(TextDefinitionNavigator.ResolveFromSymbol(definitionProvider, CreateSnapshot(), string.Empty));
		Assert.IsNull(TextDefinitionNavigator.ResolveFromSymbol(definitionProvider, CreateSnapshot(), "   "));
		Assert.IsNull(definitionProvider.LastRequest);
	}

	[TestMethod]
	public void ResolveFromSymbol_ProviderWithoutALocation_ReturnsNull()
	{
		Assert.IsNull(TextDefinitionNavigator.ResolveFromSymbol(
			new RecordingDefinitionProvider(null), CreateSnapshot(), "alpha"));
	}

	[TestMethod]
	public void ResolveFromSymbol_LocationWithSelectionRange_UsesTheSelectionStart()
	{
		var targetRange = new TextPositionRange(new TextPosition(1, 0), new TextPosition(1, 15));
		var selectionRange = new TextPositionRange(new TextPosition(1, 7), new TextPosition(1, 12));
		var provider = new RecordingDefinitionProvider(
			new TextDefinitionLocation(targetRange, selectionRange: selectionRange));

		TextDefinitionNavigationTarget? target = TextDefinitionNavigator.ResolveFromSymbol(
			provider, CreateSnapshot(), "bravo");

		Assert.IsNotNull(target);
		Assert.AreEqual(new TextPosition(1, 7), target.DocumentStart);
	}

	[TestMethod]
	public void ResolveFromSymbol_ColumnBeyondTheLineEnd_StaysApplicable()
	{
		// A column past the end of its line is the one tolerance: the host clamps it when it applies the
		// position, so the navigator keeps the target.
		TextDefinitionNavigationTarget? target = TextDefinitionNavigator.ResolveFromSymbol(
			new RecordingDefinitionProvider(LocationAt(3, 200)), CreateSnapshot(), "charlie");

		Assert.IsNotNull(target);
		Assert.AreEqual(new TextPosition(2, 199), target.DocumentStart);
	}

	[TestMethod]
	public void ResolveFromSymbol_PositionOutsideTheDocument_ReturnsNull()
	{
		// The provider contract uses zero-based positions, so a line beyond the snapshot and a negative line or
		// character are absent rather than clamped.
		Assert.IsNull(TextDefinitionNavigator.ResolveFromSymbol(
			new RecordingDefinitionProvider(LocationAt(99)), CreateSnapshot(), "alpha"));
		Assert.IsNull(TextDefinitionNavigator.ResolveFromSymbol(
			new RecordingDefinitionProvider(LocationAt(0)), CreateSnapshot(), "alpha"));
		Assert.IsNull(TextDefinitionNavigator.ResolveFromSymbol(
			new RecordingDefinitionProvider(LocationAt(2, -5)), CreateSnapshot(), "bravo"));
	}

	[TestMethod]
	public void ResolveFromSymbol_CrossDocumentLocation_ReturnsATargetWithoutAnInDocumentStart()
	{
		TextDefinitionLocation location = LocationAt(99, documentId: "other.txt");

		TextDefinitionNavigationTarget? target = TextDefinitionNavigator.ResolveFromSymbol(
			new RecordingDefinitionProvider(location), CreateSnapshot(), "source");

		// A cross-document location is the host's to open, so its coordinates are not validated against a
		// document they do not address.
		Assert.IsNotNull(target);
		Assert.AreSame(location, target.Location);
		Assert.IsNull(target.DocumentStart);
	}

	[TestMethod]
	public void ResolveFromSymbol_BlankDocumentId_NormalizesToAnInDocumentTarget()
	{
		TextDefinitionNavigationTarget? target = TextDefinitionNavigator.ResolveFromSymbol(
			new RecordingDefinitionProvider(LocationAt(2, 8, "   ")), CreateSnapshot(), "bravo");

		Assert.IsNotNull(target);
		Assert.IsNull(target.Location.DocumentId);
		Assert.AreEqual(new TextPosition(1, 7), target.DocumentStart);
	}

	[TestMethod]
	public void ResolveFromOffset_Resolver_ReceivesTheSnapshotAndTheOffset()
	{
		ITextSnapshot? resolvedSnapshot = null;
		int? resolvedOffset = null;

		TextDefinitionNavigationTarget? target = TextDefinitionNavigator.ResolveFromOffset(
			(snapshot, offset) =>
			{
				resolvedSnapshot = snapshot;
				resolvedOffset = offset;
				return LocationAt(2, 8);
			},
			CreateSnapshot(),
			4);

		Assert.IsNotNull(target);
		Assert.IsNotNull(resolvedSnapshot);
		Assert.AreEqual(DocumentText, resolvedSnapshot.GetText(0, resolvedSnapshot.TextLength));
		Assert.AreEqual(4, resolvedOffset);
		Assert.AreEqual(new TextPosition(1, 7), target.DocumentStart);
	}

	[TestMethod]
	public void ResolveFromOffset_OffsetAtTheSnapshotEnd_IsAccepted()
	{
		ITextSnapshot snapshot = CreateSnapshot();

		Assert.IsNotNull(TextDefinitionNavigator.ResolveFromOffset(
			(_, _) => LocationAt(1), snapshot, snapshot.TextLength));
	}

	[TestMethod]
	public void ResolveFromOffset_OffsetOutsideTheSnapshot_ReturnsNullWithoutCallingTheResolver()
	{
		bool resolverCalled = false;
		ITextSnapshot snapshot = CreateSnapshot();

		Assert.IsNull(TextDefinitionNavigator.ResolveFromOffset(
			(_, _) =>
			{
				resolverCalled = true;
				return LocationAt(1);
			},
			snapshot,
			-1));
		Assert.IsNull(TextDefinitionNavigator.ResolveFromOffset(
			(_, _) =>
			{
				resolverCalled = true;
				return LocationAt(1);
			},
			snapshot,
			snapshot.TextLength + 1));
		Assert.IsFalse(resolverCalled);
	}

	[TestMethod]
	public void ResolveFromOffset_ResolverWithoutALocation_ReturnsNull()
	{
		Assert.IsNull(TextDefinitionNavigator.ResolveFromOffset((_, _) => null, CreateSnapshot(), 0));
	}

	[TestMethod]
	public async Task ResolveFromHoverAsync_HoveredSymbol_ResolvesThroughTheDefinitionResolver()
	{
		TextDefinitionRequest? definitionRequest = null;

		TextDefinitionNavigationTarget? target = await TextDefinitionNavigator.ResolveFromHoverAsync(
			(request, _) =>
			{
				definitionRequest = request;
				return Task.FromResult<TextDefinitionLocation?>(LocationAt(3, 8));
			},
			(_, _) => Task.FromResult<TextHoverInfo?>(new TextHoverInfo("hover") { SymbolName = "charlie" }),
			CreateSnapshot(),
			0);

		Assert.IsNotNull(target);
		Assert.IsNotNull(definitionRequest);
		Assert.AreEqual(DocumentText, definitionRequest.DocumentText);
		Assert.AreEqual("charlie", definitionRequest.SymbolName);
		Assert.AreEqual(new TextPosition(2, 7), target.DocumentStart);
	}

	[TestMethod]
	public async Task ResolveFromHoverAsync_HoverWithoutASymbol_ReturnsNullWithoutCallingTheDefinitionResolver()
	{
		bool definitionResolverCalled = false;
		bool hoverResolverCalled = false;

		// The offset is outside the snapshot, so neither resolver runs.
		Assert.IsNull(await TextDefinitionNavigator.ResolveFromHoverAsync(
			(_, _) =>
			{
				definitionResolverCalled = true;
				return Task.FromResult<TextDefinitionLocation?>(LocationAt(1));
			},
			(_, _) =>
			{
				hoverResolverCalled = true;
				return Task.FromResult<TextHoverInfo?>(new TextHoverInfo("hover") { SymbolName = "alpha" });
			},
			CreateSnapshot(),
			-1));

		Assert.IsFalse(definitionResolverCalled);
		Assert.IsFalse(hoverResolverCalled);

		hoverResolverCalled = false;

		// A hover result without a symbol still stops before the definition resolver.
		Assert.IsNull(await TextDefinitionNavigator.ResolveFromHoverAsync(
			(_, _) =>
			{
				definitionResolverCalled = true;
				return Task.FromResult<TextDefinitionLocation?>(LocationAt(1));
			},
			(_, _) =>
			{
				hoverResolverCalled = true;
				return Task.FromResult<TextHoverInfo?>(new TextHoverInfo("hover"));
			},
			CreateSnapshot(),
			0));

		Assert.IsTrue(hoverResolverCalled);
		Assert.IsFalse(definitionResolverCalled);
	}

	[TestMethod]
	public async Task ResolveFromSymbolAsync_BlankSymbolName_ReturnsNullWithoutCallingTheResolver()
	{
		bool resolverCalled = false;

		Task<TextDefinitionLocation?> Resolver(TextDefinitionRequest request, CancellationToken cancellationToken)
		{
			resolverCalled = true;
			return Task.FromResult<TextDefinitionLocation?>(LocationAt(1));
		}

		Assert.IsNull(await TextDefinitionNavigator.ResolveFromSymbolAsync(Resolver, CreateSnapshot(), null));
		Assert.IsNull(await TextDefinitionNavigator.ResolveFromSymbolAsync(Resolver, CreateSnapshot(), "   "));
		Assert.IsFalse(resolverCalled);
	}

	[TestMethod]
	public async Task ResolveFromSymbolAsync_CrossDocumentLocation_ReturnsATargetWithoutAnInDocumentStart()
	{
		TextDefinitionLocation location = LocationAt(1, documentId: "other.txt");

		TextDefinitionNavigationTarget? target = await TextDefinitionNavigator.ResolveFromSymbolAsync(
			(_, _) => Task.FromResult<TextDefinitionLocation?>(location), CreateSnapshot(), "source");

		Assert.IsNotNull(target);
		Assert.AreSame(location, target.Location);
		Assert.IsNull(target.DocumentStart);
	}

	[TestMethod]
	public async Task ResolveFromOffsetAsync_Resolver_ReceivesTheSnapshotAndTheOffset()
	{
		ITextSnapshot? resolvedSnapshot = null;
		int? resolvedOffset = null;

		TextDefinitionNavigationTarget? target = await TextDefinitionNavigator.ResolveFromOffsetAsync(
			(snapshot, offset, _) =>
			{
				resolvedSnapshot = snapshot;
				resolvedOffset = offset;
				return Task.FromResult<TextDefinitionLocation?>(LocationAt(3, 8));
			},
			CreateSnapshot(),
			4);

		Assert.IsNotNull(target);
		Assert.IsNotNull(resolvedSnapshot);
		Assert.AreEqual(DocumentText, resolvedSnapshot.GetText(0, resolvedSnapshot.TextLength));
		Assert.AreEqual(4, resolvedOffset);
		Assert.AreEqual(new TextPosition(2, 7), target.DocumentStart);
	}

	[TestMethod]
	public async Task ResolveFromOffsetAsync_PositionOutsideTheDocument_ReturnsNull()
	{
		Assert.IsNull(await TextDefinitionNavigator.ResolveFromOffsetAsync(
			(_, _, _) => Task.FromResult<TextDefinitionLocation?>(LocationAt(99)), CreateSnapshot(), 0));
	}

	[TestMethod]
	public async Task ResolveFromHoverAsync_PreCanceledToken_ThrowsOperationCanceledException()
	{
		using var cancellationTokenSource = new CancellationTokenSource();
		cancellationTokenSource.Cancel();

		await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => TextDefinitionNavigator.ResolveFromHoverAsync(
			(_, _) => Task.FromResult<TextDefinitionLocation?>(LocationAt(1)),
			(_, _) => Task.FromResult<TextHoverInfo?>(new TextHoverInfo("hover") { SymbolName = "alpha" }),
			CreateSnapshot(),
			0,
			cancellationTokenSource.Token));
	}

	[TestMethod]
	public void ResolveFrom_EveryProbe_RejectsNullArguments()
	{
		ITextSnapshot snapshot = CreateSnapshot();
		var definitionProvider = new RecordingDefinitionProvider(LocationAt(1));
		var hoverProvider = new StubHoverProvider(new TextHoverInfo("hover") { SymbolName = "alpha" });

		Assert.ThrowsExactly<ArgumentNullException>(
			() => TextDefinitionNavigator.ResolveFromHover(null!, hoverProvider, snapshot, 0));
		Assert.ThrowsExactly<ArgumentNullException>(
			() => TextDefinitionNavigator.ResolveFromHover(definitionProvider, null!, snapshot, 0));
		Assert.ThrowsExactly<ArgumentNullException>(
			() => TextDefinitionNavigator.ResolveFromHover(definitionProvider, hoverProvider, null!, 0));
		Assert.ThrowsExactly<ArgumentNullException>(
			() => TextDefinitionNavigator.ResolveFromSymbol(null!, snapshot, "alpha"));
		Assert.ThrowsExactly<ArgumentNullException>(
			() => TextDefinitionNavigator.ResolveFromSymbol(definitionProvider, null!, "alpha"));
		Assert.ThrowsExactly<ArgumentNullException>(
			() => TextDefinitionNavigator.ResolveFromOffset(null!, snapshot, 0));
		Assert.ThrowsExactly<ArgumentNullException>(
			() => TextDefinitionNavigator.ResolveFromOffset((_, _) => null, null!, 0));
	}

	[TestMethod]
	public async Task ResolveFromAsync_EveryProbe_RejectsNullArguments()
	{
		ITextSnapshot snapshot = CreateSnapshot();
		Func<TextDefinitionRequest, CancellationToken, Task<TextDefinitionLocation?>> definitionResolver =
			(_, _) => Task.FromResult<TextDefinitionLocation?>(LocationAt(1));
		Func<TextHoverRequest, CancellationToken, Task<TextHoverInfo?>> hoverResolver =
			(_, _) => Task.FromResult<TextHoverInfo?>(new TextHoverInfo("hover") { SymbolName = "alpha" });

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(
			() => TextDefinitionNavigator.ResolveFromHoverAsync(null!, hoverResolver, snapshot, 0));
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(
			() => TextDefinitionNavigator.ResolveFromHoverAsync(definitionResolver, null!, snapshot, 0));
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(
			() => TextDefinitionNavigator.ResolveFromHoverAsync(definitionResolver, hoverResolver, null!, 0));
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(
			() => TextDefinitionNavigator.ResolveFromSymbolAsync(null!, snapshot, "alpha"));
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(
			() => TextDefinitionNavigator.ResolveFromSymbolAsync(definitionResolver, null!, "alpha"));
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(
			() => TextDefinitionNavigator.ResolveFromOffsetAsync(null!, snapshot, 0));
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(
			() => TextDefinitionNavigator.ResolveFromOffsetAsync((_, _, _) => Task.FromResult<TextDefinitionLocation?>(null), null!, 0));
	}

	[TestMethod]
	public void Target_NullLocation_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextDefinitionNavigationTarget(null!));
	}

	[TestMethod]
	public void Target_WithoutAnInDocumentStart_LeavesItNull()
	{
		var target = new TextDefinitionNavigationTarget(LocationAt(1, documentId: "other.txt"));

		Assert.IsNull(target.DocumentStart);
	}

	private static ITextSnapshot CreateSnapshot() => new StringTextSnapshot(DocumentText, "script.txt");

	private static TextDefinitionLocation LocationAt(int oneBasedLine, int oneBasedColumn = 1, string? documentId = null)
		=> new(
			new TextPositionRange(
				new TextPosition(oneBasedLine - 1, oneBasedColumn - 1),
				new TextPosition(oneBasedLine - 1, oneBasedColumn - 1)),
			documentId);

	private sealed class RecordingDefinitionProvider(TextDefinitionLocation? location) : ITextDefinitionProvider
	{
		public TextDefinitionRequest? LastRequest { get; private set; }

		public TextDefinitionLocation? GetDefinition(TextDefinitionRequest request)
		{
			LastRequest = request;
			return location;
		}
	}

	private sealed class StubHoverProvider(TextHoverInfo? hoverInfo) : ITextHoverProvider
	{
		public TextHoverInfo? GetHoverInfo(TextHoverRequest request) => hoverInfo;
	}
}
