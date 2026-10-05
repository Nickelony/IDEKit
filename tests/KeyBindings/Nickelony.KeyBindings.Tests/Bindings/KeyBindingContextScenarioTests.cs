using Nickelony.KeyBindings.Testing;
using static Nickelony.KeyBindings.Testing.KeyBindingTestHelpers;

namespace Nickelony.KeyBindings.Tests;

/// <summary>
/// Covers context-scoped bindings: the declared token set, context-scoped lookup and prefix probes, validation and replacement across contexts,
/// and the persistence round trip.
/// </summary>
[TestClass]
public sealed class KeyBindingContextScenarioTests
{
	private static readonly KeyStroke ExtrudeOrPaint = new(KeyCode.E, KeyModifiers.None);
	private static readonly KeyStroke SaveStroke = new(KeyCode.S, KeyModifiers.Control);
	private static readonly KeyStroke UndoStroke = new(KeyCode.Z, KeyModifiers.Control);
	private static readonly KeyStroke ChordPrefix = new(KeyCode.K, KeyModifiers.Control);
	private static readonly KeyStroke FindStroke = new(KeyCode.F, KeyModifiers.Control);

	/// <summary>
	/// Builds a catalog in which the <c>geometry</c> and <c>texture</c> commands are not co-active, so they may claim the same chord,
	/// while the always-active command competes with both.
	/// </summary>
	private static CommandCatalog<TestCommand> CreateTwoContextCatalog()
	{
		return new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.Build, nameof(TestCommand.Build), CommandRemappingPolicy.Remappable, ExtrudeOrPaint)
			{
				Context = GeometryContext,
			},

			new CommandDescriptor<TestCommand>(TestCommand.Find, nameof(TestCommand.Find), CommandRemappingPolicy.Remappable, FindStroke)
			{
				Context = TextureContext,
			},

			new CommandDescriptor<TestCommand>(TestCommand.Undo, nameof(TestCommand.Undo), CommandRemappingPolicy.Remappable, UndoStroke),
		]);
	}

	// ---- The declared token set ----

	[TestMethod]
	public void Contexts_ListsExactlyTheDeclaredTokens()
	{
		KeyBindingService<TestCommand> service = CreateService(catalog: CreateTwoContextCatalog());

		Assert.HasCount(2, service.Contexts);
		Assert.Contains(GeometryContext, service.Contexts);
		Assert.Contains(TextureContext, service.Contexts);
	}

	/// <summary>
	/// Pins the declared set as a set of tokens: two commands scoped to the same token contribute one entry, so the set describes the contexts a host
	/// can publish rather than the commands in them.
	/// </summary>
	[TestMethod]
	public void Contexts_SharedTokenAcrossCommands_IsListedOnce()
	{
		KeyBindingService<TestCommand> service = CreateService(
			catalog: new CommandCatalog<TestCommand>([
				new CommandDescriptor<TestCommand>(
					TestCommand.Build,
					nameof(TestCommand.Build),
					CommandRemappingPolicy.Remappable,
					new KeyStroke(KeyCode.E, KeyModifiers.None)
				)
				{
					Context = GeometryContext,
				},

				new CommandDescriptor<TestCommand>(
					TestCommand.Save,
					nameof(TestCommand.Save),
					CommandRemappingPolicy.Remappable,
					new KeyStroke(KeyCode.S, KeyModifiers.Control)
				)
				{
					Context = GeometryContext,
				},
			])
		);

		Assert.HasCount(1, service.Contexts);
		Assert.Contains(GeometryContext, service.Contexts);
	}

	[TestMethod]
	public void Contexts_WithoutContextScopedCommands_IsEmpty() => Assert.IsEmpty(CreateService().Contexts);

	// ---- Lookup ----

	[TestMethod]
	public void TryGetCommand_ContextScopedBinding_ResolvesOnlyInItsContext()
	{
		KeyBindingService<TestCommand> service = CreateContextService();

		Assert.IsTrue(service.TryGetCommand(ExtrudeOrPaint, GeometryContext, out TestCommand geometryCommand));
		Assert.AreEqual(TestCommand.Build, geometryCommand);

		Assert.IsTrue(service.TryGetCommand(ExtrudeOrPaint, TextureContext, out TestCommand textureCommand));
		Assert.AreEqual(TestCommand.Find, textureCommand);

		// An undeclared token and the always-active context resolve no context-scoped binding.
		Assert.IsFalse(service.TryGetCommand(ExtrudeOrPaint, "lighting", out _));
		Assert.IsFalse(service.TryGetCommand(ExtrudeOrPaint, null, out _));
	}

	[TestMethod]
	public void TryGetCommand_AlwaysActiveBinding_ResolvesInEveryContext()
	{
		KeyBindingService<TestCommand> service = CreateContextService();

		Assert.IsTrue(service.TryGetCommand(SaveStroke, GeometryContext, out TestCommand geometryCommand));
		Assert.AreEqual(TestCommand.Save, geometryCommand);

		Assert.IsTrue(service.TryGetCommand(SaveStroke, TextureContext, out TestCommand textureCommand));
		Assert.AreEqual(TestCommand.Save, textureCommand);

		Assert.IsTrue(service.TryGetCommand(SaveStroke, null, out TestCommand alwaysCommand));
		Assert.AreEqual(TestCommand.Save, alwaysCommand);
	}

	[TestMethod]
	public void TryGetCommand_TokenComparison_IsOrdinal()
	{
		KeyBindingService<TestCommand> service = CreateContextService();

		// A token that differs only in case is a different token, which is why the declared set exists.
		Assert.IsFalse(service.TryGetCommand(ExtrudeOrPaint, "Geometry", out _));
		Assert.DoesNotContain("Geometry", service.Contexts);
	}

	[TestMethod]
	public void IsChordPrefix_PrefixDeclaredInAnotherContext_IsNotReported()
	{
		KeyBindingService<TestCommand> service = CreateService(
			catalog: new CommandCatalog<TestCommand>([
				new CommandDescriptor<TestCommand>(
					TestCommand.SaveAll,
					nameof(TestCommand.SaveAll),
					CommandRemappingPolicy.Remappable,
					new KeyChord(ChordPrefix, SaveStroke)
				)
				{
					Context = GeometryContext,
				},
			])
		);

		Assert.IsTrue(service.IsChordPrefix(ChordPrefix, GeometryContext, _ => true));
		Assert.IsFalse(service.IsChordPrefix(ChordPrefix, TextureContext, _ => true));
		Assert.IsFalse(service.IsChordPrefix(ChordPrefix, null, _ => true));
	}

	[TestMethod]
	public void IsChordPrefix_AlwaysActivePrefix_IsReportedInEveryContext()
	{
		KeyBindingService<TestCommand> service = CreateService(
			catalog: new CommandCatalog<TestCommand>([
				new CommandDescriptor<TestCommand>(
					TestCommand.SaveAll,
					nameof(TestCommand.SaveAll),
					CommandRemappingPolicy.Remappable,
					new KeyChord(ChordPrefix, SaveStroke)
				),
			])
		);

		Assert.IsTrue(service.IsChordPrefix(ChordPrefix, GeometryContext, _ => true));
		Assert.IsTrue(service.IsChordPrefix(ChordPrefix, TextureContext, _ => true));
		Assert.IsTrue(service.IsChordPrefix(ChordPrefix, null, _ => true));

		// A bound chord is never reported as a prefix of itself.
		Assert.IsFalse(service.IsChordPrefix(new KeyChord(ChordPrefix, SaveStroke), GeometryContext, _ => true));
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
		Assert.AreEqual(KeyBindingOutcome.Conflict, service.Validate(TestCommand.Find, [UndoStroke]));
	}

	[TestMethod]
	public void Validate_ChordUsedByAnotherCommandInTheSameContext_ReturnsConflict()
	{
		KeyBindingService<TestCommand> service = CreateService(
			catalog: new CommandCatalog<TestCommand>([
				new CommandDescriptor<TestCommand>(TestCommand.Build, nameof(TestCommand.Build), CommandRemappingPolicy.Remappable, ExtrudeOrPaint)
				{
					Context = GeometryContext,
				},

				new CommandDescriptor<TestCommand>(TestCommand.Undo, nameof(TestCommand.Undo), CommandRemappingPolicy.Remappable, UndoStroke)
				{
					Context = GeometryContext,
				},
			])
		);

		Assert.AreEqual(KeyBindingOutcome.Conflict, service.Validate(TestCommand.Undo, [ExtrudeOrPaint]));
	}

	[TestMethod]
	public void Validate_PrefixOfAChordInAnotherContext_IsAccepted()
	{
		KeyBindingService<TestCommand> service = CreateService(
			catalog: new CommandCatalog<TestCommand>([
				new CommandDescriptor<TestCommand>(
					TestCommand.SaveAll,
					nameof(TestCommand.SaveAll),
					CommandRemappingPolicy.Remappable,
					new KeyChord(ChordPrefix, SaveStroke)
				)
				{
					Context = GeometryContext,
				},

				new CommandDescriptor<TestCommand>(TestCommand.Undo, nameof(TestCommand.Undo), CommandRemappingPolicy.Remappable, UndoStroke)
				{
					Context = TextureContext,
				},
			])
		);

		// The chord lives in the geometry context, so binding its prefix in the texture context leaves
		// both chords reachable.
		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Validate(TestCommand.Undo, [ChordPrefix], KeyBindingConflictPolicy.Replace));
	}

	[TestMethod]
	public void Validate_PrefixOfAnAlwaysActiveChord_ReturnsPrefixConflict()
	{
		KeyBindingService<TestCommand> service = CreateService(
			catalog: new CommandCatalog<TestCommand>([
				new CommandDescriptor<TestCommand>(
					TestCommand.SaveAll,
					nameof(TestCommand.SaveAll),
					CommandRemappingPolicy.Remappable,
					new KeyChord(ChordPrefix, SaveStroke)
				),

				new CommandDescriptor<TestCommand>(TestCommand.Undo, nameof(TestCommand.Undo), CommandRemappingPolicy.Remappable, UndoStroke)
				{
					Context = GeometryContext,
				},
			])
		);

		// An always-active chord is active in every context, so its prefix can never be bound anywhere.
		Assert.AreEqual(KeyBindingOutcome.PrefixConflict, service.Validate(TestCommand.Undo, [ChordPrefix]));
		Assert.AreEqual(KeyBindingOutcome.PrefixConflict, service.Validate(TestCommand.Undo, [ChordPrefix], KeyBindingConflictPolicy.Replace));
	}

	[TestMethod]
	public void Validate_AlwaysActiveProposalCollidingWithAContextScopedBinding_ReturnsConflict()
	{
		KeyBindingService<TestCommand> service = CreateService(catalog: CreateTwoContextCatalog());

		// Undo is always active, so taking Control+E would leave its binding unreachable in the geometry
		// context; a context-scoped binding cannot be displaced by an always-active proposal.
		Assert.AreEqual(KeyBindingOutcome.Conflict, service.Validate(TestCommand.Undo, [ExtrudeOrPaint]));
		Assert.AreEqual(KeyBindingOutcome.Conflict, service.Validate(TestCommand.Undo, [ExtrudeOrPaint], KeyBindingConflictPolicy.Replace));
	}

	[TestMethod]
	public void Validate_AlwaysActiveProposalWithAContextScopedPrefix_ReturnsPrefixConflict()
	{
		KeyBindingService<TestCommand> service = CreateService(
			catalog: new CommandCatalog<TestCommand>([
				new CommandDescriptor<TestCommand>(TestCommand.Build, nameof(TestCommand.Build), CommandRemappingPolicy.Remappable, ChordPrefix)
				{
					Context = GeometryContext,
				},

				new CommandDescriptor<TestCommand>(TestCommand.Undo, nameof(TestCommand.Undo), CommandRemappingPolicy.Remappable, UndoStroke),
			])
		);

		// The prefix belongs to a context the always-active proposal is active in.
		Assert.AreEqual(KeyBindingOutcome.PrefixConflict, service.Validate(TestCommand.Undo, [new KeyChord(ChordPrefix, SaveStroke)]));
	}

	[TestMethod]
	public void Validate_ChordClaimedByAPreservedEntry_ReturnsConflictInEveryContext()
	{
		// The preserved entry names a command the catalog does not contain, so its chord stays blocked in
		// every context until that command is cataloged.
		KeyBindingOverrides overrides = CreateOverrides("editor.unknown", ("E", (int)KeyModifiers.None));
		KeyBindingService<TestCommand> service = CreateService(overrides, catalog: CreateTwoContextCatalog());

		Assert.AreEqual(KeyBindingOutcome.Conflict, service.Validate(TestCommand.Find, [ExtrudeOrPaint]));
		Assert.AreEqual(KeyBindingOutcome.Conflict, service.Validate(TestCommand.Find, [ExtrudeOrPaint], KeyBindingConflictPolicy.Replace));
	}

	// ---- Mutation ----

	[TestMethod]
	public void Apply_ReplaceOverAnAlwaysActiveDefault_ShadowsItOnlyInTheProposingContext()
	{
		KeyBindingService<TestCommand> service = CreateService(catalog: CreateTwoContextCatalog());

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Apply(TestCommand.Find, [UndoStroke], KeyBindingConflictPolicy.Replace));

		// Control+Z resolves to Find in the texture context and still to Undo in every other context.
		Assert.IsTrue(service.TryGetCommand(UndoStroke, TextureContext, out TestCommand textureCommand));
		Assert.AreEqual(TestCommand.Find, textureCommand);

		Assert.IsTrue(service.TryGetCommand(UndoStroke, GeometryContext, out TestCommand geometryCommand));
		Assert.AreEqual(TestCommand.Undo, geometryCommand);

		Assert.IsTrue(service.TryGetCommand(UndoStroke, null, out TestCommand alwaysCommand));
		Assert.AreEqual(TestCommand.Undo, alwaysCommand);

		// The owner's catalog default is shadowed, never rewritten.
		Assert.AreEqual(UndoStroke, service.GetBindings(TestCommand.Undo)[0]);
	}

	[TestMethod]
	public void Apply_ContextScopedOverride_RoundTripsThroughTheSnapshot()
	{
		KeyBindingOverrides? saved = null;

		KeyBindingService<TestCommand> service = CreateService(
			null,

			snapshot =>
			{
				saved = snapshot;
				return true;
			},

			catalog: CreateTwoContextCatalog()
		);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Apply(TestCommand.Find, [SaveStroke]));

		Assert.IsNotNull(saved);

		// The context lives on the catalog descriptor, so the persisted override resolves only in the
		// context of the command it belongs to.
		KeyBindingService<TestCommand> reloaded = CreateService(saved.Clone(), catalog: CreateTwoContextCatalog());

		Assert.IsTrue(reloaded.TryGetCommand(SaveStroke, TextureContext, out TestCommand textureCommand));
		Assert.AreEqual(TestCommand.Find, textureCommand);
		Assert.IsFalse(reloaded.TryGetCommand(SaveStroke, GeometryContext, out _));
		Assert.IsFalse(reloaded.TryGetCommand(SaveStroke, null, out _));
	}

	[TestMethod]
	public void Reset_ContextScopedOverride_FallsBackToTheCatalogDefault()
	{
		KeyBindingService<TestCommand> service = CreateService(
			CreateOverrides(nameof(TestCommand.Find), ("S", (int)KeyModifiers.Control)),
			catalog: CreateTwoContextCatalog()
		);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Reset(TestCommand.Find));

		Assert.IsTrue(service.TryGetCommand(FindStroke, TextureContext, out TestCommand textureCommand));
		Assert.AreEqual(TestCommand.Find, textureCommand);
	}
}
