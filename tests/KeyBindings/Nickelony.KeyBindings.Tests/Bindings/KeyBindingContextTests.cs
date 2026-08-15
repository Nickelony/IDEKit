using Nickelony.KeyBindings.Testing;
using static Nickelony.KeyBindings.Testing.KeyBindingTestFixtures;

namespace Nickelony.KeyBindings.Tests;

/// <summary>
/// Covers context-scoped bindings: the declared token set, context-scoped lookup and prefix probes,
/// validation and replacement across contexts, and the persistence round trip.
/// </summary>
[TestClass]
public class KeyBindingContextTests
{
	private static readonly KeyCombo ExtrudeOrPaint = new(KeyCode.E, KeyModifierSet.None);
	private static readonly KeyCombo SaveCombo = new(KeyCode.S, KeyModifierSet.Control);
	private static readonly KeyCombo UndoCombo = new(KeyCode.Z, KeyModifierSet.Control);
	private static readonly KeyCombo ChordPrefix = new(KeyCode.K, KeyModifierSet.Control);
	private static readonly KeyCombo FindCombo = new(KeyCode.F, KeyModifierSet.Control);

	/// <summary>
	/// Builds a catalog in which the <c>geometry</c> and <c>texture</c> commands are not co-active, so
	/// they may claim the same chord, while the always-active command competes with both.
	/// </summary>
	private static CommandCatalog<TestCommand> CreateTwoContextCatalog()
	{
		return new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.Build, nameof(TestCommand.Build), CommandRemappingPolicy.Remappable,
				ExtrudeOrPaint) { Context = GeometryContext },
			new CommandDescriptor<TestCommand>(TestCommand.Find, nameof(TestCommand.Find), CommandRemappingPolicy.Remappable,
				FindCombo) { Context = TextureContext },
			new CommandDescriptor<TestCommand>(TestCommand.Undo, nameof(TestCommand.Undo), CommandRemappingPolicy.Remappable,
				UndoCombo)
		]);
	}

	// ---- The declared token set ----

	[TestMethod]
	public void Contexts_ListsExactlyTheDeclaredTokens()
	{
		KeyBindingService<TestCommand> service = CreateService(catalog: CreateTwoContextCatalog());

		Assert.HasCount(2, service.Contexts);
		Assert.IsTrue(service.Contexts.Contains(GeometryContext));
		Assert.IsTrue(service.Contexts.Contains(TextureContext));
	}

	/// <summary>
	/// Pins the declared set as a set of tokens: two commands scoped to the same token contribute one
	/// entry, so the set describes the contexts a host can publish rather than the commands in them.
	/// </summary>
	[TestMethod]
	public void Contexts_SharedTokenAcrossCommands_IsListedOnce()
	{
		KeyBindingService<TestCommand> service = CreateService(catalog: new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.Build, nameof(TestCommand.Build), CommandRemappingPolicy.Remappable,
				new KeyCombo(KeyCode.E, KeyModifierSet.None)) { Context = GeometryContext },
			new CommandDescriptor<TestCommand>(TestCommand.Save, nameof(TestCommand.Save), CommandRemappingPolicy.Remappable,
				new KeyCombo(KeyCode.S, KeyModifierSet.Control)) { Context = GeometryContext }
		]));

		Assert.HasCount(1, service.Contexts);
		Assert.IsTrue(service.Contexts.Contains(GeometryContext));
	}

	[TestMethod]
	public void Contexts_WithoutContextScopedCommands_IsEmpty()
		=> Assert.IsEmpty(CreateService().Contexts);

	// ---- Lookup ----

	[TestMethod]
	public void TryGetCommand_ContextScopedBinding_ResolvesOnlyInItsContext()
	{
		KeyBindingService<TestCommand> service = CreateContextService();

		Assert.IsTrue(service.TryGetCommand(ExtrudeOrPaint, GeometryContext, out TestCommand geometryCommand));
		Assert.AreEqual(TestCommand.Build, geometryCommand);

		Assert.IsTrue(service.TryGetCommand(ExtrudeOrPaint, TextureContext, out TestCommand textureCommand));
		Assert.AreEqual(TestCommand.Find, textureCommand);

		// An undeclared token and the always-active context resolve no token-scoped binding.
		Assert.IsFalse(service.TryGetCommand(ExtrudeOrPaint, "lighting", out _));
		Assert.IsFalse(service.TryGetCommand(ExtrudeOrPaint, null, out _));
	}

	[TestMethod]
	public void TryGetCommand_AlwaysActiveBinding_ResolvesInEveryContext()
	{
		KeyBindingService<TestCommand> service = CreateContextService();

		Assert.IsTrue(service.TryGetCommand(SaveCombo, GeometryContext, out TestCommand geometryCommand));
		Assert.AreEqual(TestCommand.Save, geometryCommand);

		Assert.IsTrue(service.TryGetCommand(SaveCombo, TextureContext, out TestCommand textureCommand));
		Assert.AreEqual(TestCommand.Save, textureCommand);

		Assert.IsTrue(service.TryGetCommand(SaveCombo, null, out TestCommand alwaysCommand));
		Assert.AreEqual(TestCommand.Save, alwaysCommand);
	}

	[TestMethod]
	public void TryGetCommand_TokenComparison_IsOrdinal()
	{
		KeyBindingService<TestCommand> service = CreateContextService();

		// A token that differs only in case is a different token, which is why the declared set exists.
		Assert.IsFalse(service.TryGetCommand(ExtrudeOrPaint, "Geometry", out _));
		Assert.IsFalse(service.Contexts.Contains("Geometry"));
	}

	[TestMethod]
	public void IsChordPrefix_PrefixDeclaredInAnotherContext_IsNotReported()
	{
		KeyBindingService<TestCommand> service = CreateService(catalog: new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.SaveAll, nameof(TestCommand.SaveAll), CommandRemappingPolicy.Remappable,
				new KeyChord(ChordPrefix, SaveCombo)) { Context = GeometryContext }
		]));

		Assert.IsTrue(service.IsChordPrefix(ChordPrefix, GeometryContext));
		Assert.IsFalse(service.IsChordPrefix(ChordPrefix, TextureContext));
		Assert.IsFalse(service.IsChordPrefix(ChordPrefix, null));
	}

	[TestMethod]
	public void IsChordPrefix_AlwaysActivePrefix_IsReportedInEveryContext()
	{
		KeyBindingService<TestCommand> service = CreateService(catalog: new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.SaveAll, nameof(TestCommand.SaveAll), CommandRemappingPolicy.Remappable,
				new KeyChord(ChordPrefix, SaveCombo))
		]));

		Assert.IsTrue(service.IsChordPrefix(ChordPrefix, GeometryContext));
		Assert.IsTrue(service.IsChordPrefix(ChordPrefix, TextureContext));
		Assert.IsTrue(service.IsChordPrefix(ChordPrefix, null));

		// A bound chord is never reported as a prefix of itself.
		Assert.IsFalse(service.IsChordPrefix(new KeyChord(ChordPrefix, SaveCombo), GeometryContext));
	}

	// ---- Validation ----

	[TestMethod]
	public void Validate_ChordUsedByAnotherContext_IsAccepted()
	{
		KeyBindingService<TestCommand> service = CreateService(catalog: CreateTwoContextCatalog());

		// Find may take E, which Build claims in the geometry context, because the two contexts are never
		// active at the same time - so even the Reject policy accepts the proposal.
		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Validate(TestCommand.Find, [ExtrudeOrPaint]));
	}

	[TestMethod]
	public void Validate_ChordUsedByAnAlwaysActiveCommand_ReturnsConflict()
	{
		KeyBindingService<TestCommand> service = CreateService(catalog: CreateTwoContextCatalog());

		// Undo is always active, so it competes with every context.
		Assert.AreEqual(KeyBindingOutcome.Conflict, service.Validate(TestCommand.Find, [UndoCombo]));
	}

	[TestMethod]
	public void Validate_ChordUsedByAnotherCommandInTheSameContext_ReturnsConflict()
	{
		KeyBindingService<TestCommand> service = CreateService(catalog: new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.Build, nameof(TestCommand.Build), CommandRemappingPolicy.Remappable,
				ExtrudeOrPaint) { Context = GeometryContext },
			new CommandDescriptor<TestCommand>(TestCommand.Undo, nameof(TestCommand.Undo), CommandRemappingPolicy.Remappable,
				UndoCombo) { Context = GeometryContext }
		]));

		Assert.AreEqual(KeyBindingOutcome.Conflict, service.Validate(TestCommand.Undo, [ExtrudeOrPaint]));
	}

	[TestMethod]
	public void Validate_PrefixOfAChordInAnotherContext_IsAccepted()
	{
		KeyBindingService<TestCommand> service = CreateService(catalog: new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.SaveAll, nameof(TestCommand.SaveAll), CommandRemappingPolicy.Remappable,
				new KeyChord(ChordPrefix, SaveCombo)) { Context = GeometryContext },
			new CommandDescriptor<TestCommand>(TestCommand.Undo, nameof(TestCommand.Undo), CommandRemappingPolicy.Remappable,
				UndoCombo) { Context = TextureContext }
		]));

		// The chord lives in the geometry context, so binding its prefix in the texture context leaves
		// both chords reachable.
		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Validate(TestCommand.Undo, [ChordPrefix], KeyBindingConflictPolicy.Replace));
	}

	[TestMethod]
	public void Validate_PrefixOfAnAlwaysActiveChord_ReturnsPrefixConflict()
	{
		KeyBindingService<TestCommand> service = CreateService(catalog: new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.SaveAll, nameof(TestCommand.SaveAll), CommandRemappingPolicy.Remappable,
				new KeyChord(ChordPrefix, SaveCombo)),
			new CommandDescriptor<TestCommand>(TestCommand.Undo, nameof(TestCommand.Undo), CommandRemappingPolicy.Remappable,
				UndoCombo) { Context = GeometryContext }
		]));

		// An always-active chord is active in every context, so its prefix can never be bound anywhere.
		Assert.AreEqual(KeyBindingOutcome.PrefixConflict, service.Validate(TestCommand.Undo, [ChordPrefix]));
		Assert.AreEqual(
			KeyBindingOutcome.PrefixConflict,
			service.Validate(TestCommand.Undo, [ChordPrefix], KeyBindingConflictPolicy.Replace));
	}

	[TestMethod]
	public void Validate_AlwaysActiveProposalCollidingWithAContextScopedBinding_ReturnsConflict()
	{
		KeyBindingService<TestCommand> service = CreateService(catalog: CreateTwoContextCatalog());

		// Undo is always active, so taking Control+E would leave its binding unreachable in the geometry
		// context; a context-scoped binding cannot be displaced by an always-active proposal.
		Assert.AreEqual(KeyBindingOutcome.Conflict, service.Validate(TestCommand.Undo, [ExtrudeOrPaint]));
		Assert.AreEqual(
			KeyBindingOutcome.Conflict,
			service.Validate(TestCommand.Undo, [ExtrudeOrPaint], KeyBindingConflictPolicy.Replace));
	}

	[TestMethod]
	public void Validate_AlwaysActiveProposalWithAContextScopedPrefix_ReturnsPrefixConflict()
	{
		KeyBindingService<TestCommand> service = CreateService(catalog: new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.Build, nameof(TestCommand.Build), CommandRemappingPolicy.Remappable,
				ChordPrefix) { Context = GeometryContext },
			new CommandDescriptor<TestCommand>(TestCommand.Undo, nameof(TestCommand.Undo), CommandRemappingPolicy.Remappable,
				UndoCombo)
		]));

		// The prefix belongs to a context the always-active proposal is active in.
		Assert.AreEqual(
			KeyBindingOutcome.PrefixConflict,
			service.Validate(TestCommand.Undo, [new KeyChord(ChordPrefix, SaveCombo)]));
	}

	[TestMethod]
	public void Validate_ChordClaimedByAPreservedEntry_ReturnsConflictInEveryContext()
	{
		// The preserved entry names a command the catalog does not contain, so its chord stays blocked in
		// every context until that command is cataloged.
		KeyBindingOverrides overrides = CreateOverrides("editor.unknown", ("E", (int)KeyModifierSet.None));
		KeyBindingService<TestCommand> service = CreateService(overrides, catalog: CreateTwoContextCatalog());

		Assert.AreEqual(KeyBindingOutcome.Conflict, service.Validate(TestCommand.Find, [ExtrudeOrPaint]));
		Assert.AreEqual(
			KeyBindingOutcome.Conflict,
			service.Validate(TestCommand.Find, [ExtrudeOrPaint], KeyBindingConflictPolicy.Replace));
	}

	// ---- Mutation ----

	[TestMethod]
	public void Apply_ReplaceOverAnAlwaysActiveDefault_ShadowsItOnlyInTheProposingContext()
	{
		KeyBindingService<TestCommand> service = CreateService(catalog: CreateTwoContextCatalog());

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Apply(TestCommand.Find, [UndoCombo], KeyBindingConflictPolicy.Replace));

		// Control+Z resolves to Find in the texture context and still to Undo in every other context.
		Assert.IsTrue(service.TryGetCommand(UndoCombo, TextureContext, out TestCommand textureCommand));
		Assert.AreEqual(TestCommand.Find, textureCommand);

		Assert.IsTrue(service.TryGetCommand(UndoCombo, GeometryContext, out TestCommand geometryCommand));
		Assert.AreEqual(TestCommand.Undo, geometryCommand);

		Assert.IsTrue(service.TryGetCommand(UndoCombo, null, out TestCommand alwaysCommand));
		Assert.AreEqual(TestCommand.Undo, alwaysCommand);

		// The owner's catalog default is shadowed, never rewritten.
		Assert.AreEqual(UndoCombo, service.GetBindings(TestCommand.Undo)[0]);
	}

	[TestMethod]
	public void Apply_ContextScopedOverride_RoundTripsThroughTheSnapshot()
	{
		KeyBindingOverrides? saved = null;
		KeyBindingService<TestCommand> service = CreateService(
			null,
			snapshot => { saved = snapshot; return true; },
			catalog: CreateTwoContextCatalog());

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Apply(TestCommand.Find, [SaveCombo]));

		Assert.IsNotNull(saved);

		// The context lives on the catalog descriptor, so the persisted override resolves only in the
		// context of the command it belongs to.
		KeyBindingService<TestCommand> reloaded = CreateService(saved.Clone(), catalog: CreateTwoContextCatalog());

		Assert.IsTrue(reloaded.TryGetCommand(SaveCombo, TextureContext, out TestCommand textureCommand));
		Assert.AreEqual(TestCommand.Find, textureCommand);
		Assert.IsFalse(reloaded.TryGetCommand(SaveCombo, GeometryContext, out _));
		Assert.IsFalse(reloaded.TryGetCommand(SaveCombo, null, out _));
	}

	[TestMethod]
	public void Reset_ContextScopedOverride_FallsBackToTheCatalogDefault()
	{
		KeyBindingService<TestCommand> service = CreateService(
			CreateOverrides(nameof(TestCommand.Find), ("S", (int)KeyModifierSet.Control)),
			catalog: CreateTwoContextCatalog());

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Reset(TestCommand.Find));

		Assert.IsTrue(service.TryGetCommand(FindCombo, TextureContext, out TestCommand textureCommand));
		Assert.AreEqual(TestCommand.Find, textureCommand);
	}
}
