using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.KeyBindings.OverrideEntries;
using Nickelony.KeyBindings.Testing;
using Nickelony.Testing;

namespace Nickelony.KeyBindings.Tests;

[TestClass]
public sealed class KeyBindingMapBuilderTests
{
	private static readonly KeyChord s_controlS = Chord(KeyCode.S);
	private static readonly KeyChord s_controlN = Chord(KeyCode.N);
	private static readonly KeyChord s_controlQ = Chord(KeyCode.Q);
	private static readonly KeyChord s_controlY = Chord(KeyCode.Y);
	private static readonly KeyChord s_controlK = Chord(KeyCode.K);

	private static readonly KeyChord s_controlKThenS = new(
		new KeyStroke(KeyCode.K, KeyModifiers.Control),
		new KeyStroke(KeyCode.S, KeyModifiers.Control)
	);

	private static readonly KeyChord s_e = new(new KeyStroke(KeyCode.E, KeyModifiers.None));

	[TestMethod]
	public void Build_NoOverrides_ClaimsEveryCatalogDefault()
	{
		PublishedSnapshot<TestCommand> maps = Succeed(KeyBindingTestHelpers.CreateCatalog(), new KeyBindingOverrides());

		Assert.AreEqual(TestCommand.Save, maps.CommandsByChord[new ContextChord(null, s_controlS)]);
		Assert.AreSequenceEqual([s_controlS], maps.BindingsByCommand[TestCommand.Save]);
	}

	[TestMethod]
	public void Build_TwoEntriesForOneCommand_FailsAsADuplicateEntry()
	{
		KeyBindingOverrides overrides = Overrides(Entry(TestCommand.Save, s_controlQ), Entry(TestCommand.Save, s_controlN));

		KeyBindingMapBuilder<TestCommand>.MapBuildResult result = Build(KeyBindingTestHelpers.CreateCatalog(), overrides);

		Assert.AreEqual(KeyBindingMapBuilder<TestCommand>.MapBuildOutcome.DuplicateEntry, result.Outcome);
		Assert.IsTrue(result.IsDuplicateEntryFailure);
		Assert.IsNull(result.Maps);
		Assert.Contains(nameof(TestCommand.Save), result.Reason);
	}

	[TestMethod]
	public void Build_AnOverride_ReplacesTheDefaultsOfItsCommand()
	{
		PublishedSnapshot<TestCommand> maps = Succeed(KeyBindingTestHelpers.CreateCatalog(), Overrides(Entry(TestCommand.Save, s_controlQ)));

		Assert.AreSequenceEqual([s_controlQ], maps.BindingsByCommand[TestCommand.Save]);
		Assert.AreEqual(TestCommand.Save, maps.CommandsByChord[new ContextChord(null, s_controlQ)]);
		Assert.IsFalse(maps.CommandsByChord.ContainsKey(new ContextChord(null, s_controlS)));
	}

	[TestMethod]
	public void Build_AnOverrideWithNoBindings_UnbindsTheCommand()
	{
		PublishedSnapshot<TestCommand> maps = Succeed(KeyBindingTestHelpers.CreateCatalog(), Overrides(Entry(TestCommand.Save)));

		Assert.IsTrue(maps.BindingsByCommand[TestCommand.Save].IsEmpty);
		Assert.IsFalse(maps.CommandsByChord.ContainsKey(new ContextChord(null, s_controlS)));
	}

	[TestMethod]
	public void Build_AnOverrideOfAHostReservedCommand_KeepsItsDefaultsAndLogsWhenAsked()
	{
		var logger = new RecordingLogger();
		KeyBindingOverrides overrides = Overrides(Entry(TestCommand.Exit, s_controlQ));

		KeyBindingMapBuilder<TestCommand>.MapBuildResult result = Build(
			catalog: KeyBindingTestHelpers.CreateCatalog(),
			overrides,
			logger,
			logDiagnostics: true
		);

		Assert.IsTrue(result.Succeeded, result.Reason);
		Assert.IsNotNull(result.Maps);
		Assert.IsFalse(result.Maps.CommandsByChord.ContainsKey(new ContextChord(null, s_controlQ)));
		Assert.HasCount(1, logger.Entries);

		WarningLogTestHelpers.AssertSingleWarning(logger, 4000, nameof(TestCommand.Exit));
	}

	[TestMethod]
	public void Build_DiagnosticsNotRequested_LogsNothing()
	{
		var logger = new RecordingLogger();
		KeyBindingOverrides overrides = Overrides(Entry(TestCommand.Exit, s_controlQ));

		Build(catalog: KeyBindingTestHelpers.CreateCatalog(), overrides, logger, logDiagnostics: false);

		Assert.IsEmpty(logger.Entries);
	}

	[TestMethod]
	public void Build_AnOverrideWhoseBindingsAreAllInvalid_FallsBackToTheDefaults()
	{
		var entry = new KeyBindingOverrideEntry { SerializedId = nameof(TestCommand.Save) };

		entry.Bindings.Add(new KeyBindingOverrideChord { Strokes = [new KeyBindingOverrideStroke { KeyName = "NotAKey", Modifiers = 0 }] });

		PublishedSnapshot<TestCommand> maps = Succeed(KeyBindingTestHelpers.CreateCatalog(), Overrides(entry));

		Assert.AreSequenceEqual([s_controlS], maps.BindingsByCommand[TestCommand.Save]);
	}

	[TestMethod]
	public void Build_TwoOverridesOfOneChordInTheSameContext_FailsAsAClaimConflict()
	{
		KeyBindingOverrides overrides = Overrides(Entry(TestCommand.Save, s_controlQ), Entry(TestCommand.Undo, s_controlQ));

		KeyBindingMapBuilder<TestCommand>.MapBuildResult result = Build(KeyBindingTestHelpers.CreateCatalog(), overrides);

		Assert.AreEqual(KeyBindingMapBuilder<TestCommand>.MapBuildOutcome.ClaimConflict, result.Outcome);
		Assert.IsNull(result.Maps);
	}

	[TestMethod]
	public void Build_AnOverrideOfARemappableDefault_TakesTheChordFromItsOwner()
	{
		PublishedSnapshot<TestCommand> maps = Succeed(KeyBindingTestHelpers.CreateCatalog(), Overrides(Entry(TestCommand.Save, s_controlN)));

		Assert.AreEqual(TestCommand.Save, maps.CommandsByChord[new ContextChord(null, s_controlN)]);
		Assert.IsTrue(maps.BindingsByCommand[TestCommand.NewFile].IsEmpty);
	}

	[TestMethod]
	public void Build_AnOverrideOfAHostManagedDefault_FailsAsAClaimConflict()
	{
		KeyBindingMapBuilder<TestCommand>.MapBuildResult result = Build(
			KeyBindingTestHelpers.CreateCatalog(),
			Overrides(Entry(TestCommand.Save, s_controlY))
		);

		Assert.AreEqual(KeyBindingMapBuilder<TestCommand>.MapBuildOutcome.ClaimConflict, result.Outcome);
		Assert.Contains(nameof(TestCommand.Redo), result.Reason);
	}

	[TestMethod]
	public void Build_AScopedOverrideOfAnAlwaysActiveDefault_KeepsBothClaims()
	{
		PublishedSnapshot<TestCommand> maps = Succeed(KeyBindingTestHelpers.CreateContextCatalog(), Overrides(Entry(TestCommand.Build, s_controlS)));

		Assert.AreEqual(TestCommand.Build, maps.CommandsByChord[new ContextChord(KeyBindingTestHelpers.GeometryContext, s_controlS)]);
		Assert.AreEqual(TestCommand.Save, maps.CommandsByChord[new ContextChord(null, s_controlS)]);
		Assert.AreSequenceEqual([s_controlS], maps.BindingsByCommand[TestCommand.Save]);
	}

	[TestMethod]
	public void Build_AnAlwaysActiveOverrideOfAScopedDefault_FailsAsAClaimConflict()
	{
		KeyBindingMapBuilder<TestCommand>.MapBuildResult result = Build(
			KeyBindingTestHelpers.CreateContextCatalog(),
			Overrides(Entry(TestCommand.Save, s_e))
		);

		Assert.AreEqual(KeyBindingMapBuilder<TestCommand>.MapBuildOutcome.ClaimConflict, result.Outcome);
	}

	[TestMethod]
	public void Build_AnOverrideThatMakesAnotherChordItsPrefix_FailsAsAPrefixConflict()
	{
		KeyBindingMapBuilder<TestCommand>.MapBuildResult result = Build(
			KeyBindingTestHelpers.CreateChordCatalog(),
			Overrides(Entry(TestCommand.Undo, s_controlK))
		);

		Assert.AreEqual(KeyBindingMapBuilder<TestCommand>.MapBuildOutcome.PrefixConflict, result.Outcome);
		Assert.IsTrue(result.IsPrefixConflictFailure);
		Assert.IsNull(result.Maps);
	}

	[TestMethod]
	public void Build_ChordsOfSeveralStrokes_PublishEveryStrictPrefixInTheContextOfTheChord()
	{
		PublishedSnapshot<TestCommand> maps = Succeed(KeyBindingTestHelpers.CreateChordCatalog(), new KeyBindingOverrides());

		Assert.IsTrue(maps.CommandsByChordPrefix.ContainsKey(new ContextChord(null, s_controlK)));
		Assert.IsFalse(maps.CommandsByChordPrefix.ContainsKey(new ContextChord(null, s_controlKThenS)));
	}

	[TestMethod]
	public void Build_ChordsSharingAPrefix_PublishTheCommandOfEachChordUnderIt()
	{
		PublishedSnapshot<TestCommand> maps = Succeed(KeyBindingTestHelpers.CreateChordCatalog(), new KeyBindingOverrides());

		IReadOnlyList<TestCommand> commands = maps.CommandsByChordPrefix[new ContextChord(null, s_controlK)];

		Assert.HasCount(2, commands);
		Assert.Contains(TestCommand.SaveAll, commands);
		Assert.Contains(TestCommand.Find, commands);
	}

	[TestMethod]
	public void Build_ScopedClaims_PublishTheirChordsAsContextScopedAndTheAlwaysActiveOnesNot()
	{
		PublishedSnapshot<TestCommand> maps = Succeed(KeyBindingTestHelpers.CreateContextCatalog(), new KeyBindingOverrides());

		Assert.Contains(s_e, maps.ContextScopedChords);
		Assert.DoesNotContain(s_controlS, maps.ContextScopedChords);
	}

	private static KeyBindingMapBuilder<TestCommand>.MapBuildResult Build(
		CommandCatalog<TestCommand> catalog,
		KeyBindingOverrides overrides,
		ILogger? logger = null,
		bool logDiagnostics = false
	)
	{
		var builder = new KeyBindingMapBuilder<TestCommand>(catalog, logger ?? NullLogger.Instance, KeyDisplayTextFormatter.Default);
		return builder.Build(overrides, logDiagnostics);
	}

	private static PublishedSnapshot<TestCommand> Succeed(CommandCatalog<TestCommand> catalog, KeyBindingOverrides overrides)
	{
		KeyBindingMapBuilder<TestCommand>.MapBuildResult result = Build(catalog, overrides);

		Assert.IsTrue(result.Succeeded, result.Reason);
		Assert.IsNotNull(result.Maps);

		return result.Maps;
	}

	private static KeyChord Chord(KeyCode key) => new(new KeyStroke(key, KeyModifiers.Control));

	private static KeyBindingOverrideEntry Entry(TestCommand command, params KeyChord[] chords) =>
		KeyBindingOverrideCodec.CreateEntry(command.ToString(), chords);

	private static KeyBindingOverrides Overrides(params KeyBindingOverrideEntry[] entries) => new() { Entries = [.. entries] };
}
