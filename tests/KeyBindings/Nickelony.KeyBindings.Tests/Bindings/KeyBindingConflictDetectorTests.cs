using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.KeyBindings.OverrideEntries;
using Nickelony.KeyBindings.Testing;
using Nickelony.Testing;
using static Nickelony.KeyBindings.Testing.KeyBindingTestHelpers;

namespace Nickelony.KeyBindings.Tests;

/// <summary>Drives the conflict detector directly with the catalog and the published maps it reads.</summary>
[TestClass]
public sealed class KeyBindingConflictDetectorTests
{
	private const string PreservedId = "Vendor.UncatalogedCommand";

	private static readonly KeyChord s_controlQ = Chord(KeyCode.Q);
	private static readonly KeyChord s_controlW = Chord(KeyCode.W);

	private static readonly KeyChord s_controlQThenW = new(
		new KeyStroke(KeyCode.Q, KeyModifiers.Control),
		new KeyStroke(KeyCode.W, KeyModifiers.Control)
	);

	[TestMethod]
	public void Analyze_ChordNoCommandOwns_Succeeds()
	{
		Analysis analysis = Reject(CreateSubject(CreateCatalog()), TestCommand.Save, [s_controlQ]);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, analysis.Outcome);
		Assert.IsEmpty(analysis.Conflicts);
	}

	[TestMethod]
	public void Analyze_ChordTheCommandAlreadyOwns_Succeeds()
	{
		Analysis analysis = Reject(CreateSubject(CreateCatalog()), TestCommand.Save, [Chord(KeyCode.S)]);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, analysis.Outcome);
		Assert.IsEmpty(analysis.Conflicts);
	}

	[TestMethod]
	public void Analyze_ChordAnotherCommandOwns_UnderReject_ReturnsConflict() =>
		Assert.AreEqual(KeyBindingOutcome.Conflict, Reject(CreateSubject(CreateCatalog()), TestCommand.Save, [Chord(KeyCode.N)]).Outcome);

	[TestMethod]
	public void Analyze_ChordARemappableCommandOwns_UnderReplace_SucceedsAndNamesTheOwner()
	{
		KeyChord newFile = Chord(KeyCode.N);
		Analysis analysis = Replace(CreateSubject(CreateCatalog()), TestCommand.Save, [newFile]);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, analysis.Outcome);
		Assert.HasCount(1, analysis.Conflicts);

		Assert.AreSequenceEqual([newFile], analysis.Conflicts[TestCommand.NewFile]);
	}

	[TestMethod]
	public void Analyze_ChordsOfOneOwner_UnderReplace_AreCollectedUnderThatOwner()
	{
		KeyChord[] findChords = [Chord(KeyCode.F), Chord(KeyCode.H)];
		Analysis analysis = Replace(CreateSubject(CreateCatalog()), TestCommand.Save, findChords);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, analysis.Outcome);
		Assert.HasCount(1, analysis.Conflicts);

		Assert.AreSequenceEqual(findChords, analysis.Conflicts[TestCommand.Find]);
	}

	[TestMethod]
	public void Analyze_ChordsOfTwoOwners_UnderReplace_AreCollectedPerOwner()
	{
		KeyChord[] chords = [Chord(KeyCode.N), Chord(KeyCode.Z)];
		Analysis analysis = Replace(CreateSubject(CreateCatalog()), TestCommand.Save, chords);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, analysis.Outcome);
		Assert.HasCount(2, analysis.Conflicts);

		Assert.AreSequenceEqual([chords[0]], analysis.Conflicts[TestCommand.NewFile]);
		Assert.AreSequenceEqual([chords[1]], analysis.Conflicts[TestCommand.Undo]);
	}

	[TestMethod]
	public void Analyze_ChordAnUnremappableCommandOwns_UnderReplace_ReturnsConflict()
	{
		Subject subject = CreateSubject(CreateCatalog());

		// Redo is host-managed and Exit is host-reserved, so neither gives its chord up.
		Assert.AreEqual(KeyBindingOutcome.Conflict, Replace(subject, TestCommand.Save, [Chord(KeyCode.Y)]).Outcome);
		Assert.AreEqual(KeyBindingOutcome.Conflict, Replace(subject, TestCommand.Save, [Chord(KeyCode.F4, KeyModifiers.Alt)]).Outcome);
	}

	[TestMethod]
	public void Analyze_ChordThatPrefixesAnotherCommandsChord_ReturnsPrefixConflictUnderBothPolicies()
	{
		Subject subject = CreateSubject(CreateChordCatalog());
		KeyChord controlK = Chord(KeyCode.K);

		Assert.AreEqual(KeyBindingOutcome.PrefixConflict, Reject(subject, TestCommand.Undo, [controlK]).Outcome);
		Assert.AreEqual(KeyBindingOutcome.PrefixConflict, Replace(subject, TestCommand.Undo, [controlK]).Outcome);
	}

	[TestMethod]
	public void Analyze_ChordAnotherCommandsChordPrefixes_ReturnsPrefixConflict()
	{
		Subject subject = CreateSubject(CreateChordCatalog());
		var longer = new KeyChord(new KeyStroke(KeyCode.S, KeyModifiers.Control), new KeyStroke(KeyCode.X, KeyModifiers.Control));

		Assert.AreEqual(KeyBindingOutcome.PrefixConflict, Replace(subject, TestCommand.Build, [longer]).Outcome);
	}

	[TestMethod]
	public void Analyze_SetWithAChordAndItsPrefix_ReturnsPrefixConflict()
	{
		Analysis analysis = Reject(CreateSubject(CreateChordCatalog()), TestCommand.Build, [s_controlQ, s_controlQThenW]);
		Assert.AreEqual(KeyBindingOutcome.PrefixConflict, analysis.Outcome);
	}

	[TestMethod]
	public void Analyze_PrefixAndExactCollisionInOneSet_ReportsThePrefixConflict()
	{
		Subject subject = CreateSubject(CreateChordCatalog());

		KeyChord save = Chord(KeyCode.S);
		KeyChord controlK = Chord(KeyCode.K);

		// Save owns Control+S (an exact collision) and Control+K prefixes two chords; the prefix wins in either order.
		Assert.AreEqual(KeyBindingOutcome.PrefixConflict, Reject(subject, TestCommand.Build, [save, controlK]).Outcome);
		Assert.AreEqual(KeyBindingOutcome.PrefixConflict, Reject(subject, TestCommand.Build, [controlK, save]).Outcome);
	}

	[TestMethod]
	public void Analyze_AlwaysActiveChordAScopedCommandOwns_ReturnsConflictUnderBothPolicies()
	{
		Subject subject = CreateSubject(CreateContextCatalog());
		KeyChord e = new(new KeyStroke(KeyCode.E, KeyModifiers.None));

		Assert.AreEqual(KeyBindingOutcome.Conflict, Reject(subject, TestCommand.Save, [e]).Outcome);
		Assert.AreEqual(KeyBindingOutcome.Conflict, Replace(subject, TestCommand.Save, [e]).Outcome);
	}

	[TestMethod]
	public void Analyze_ScopedChordTheAlwaysActiveContextOwns_ConflictsUnlessReplaceMayTakeIt()
	{
		Subject subject = CreateSubject(CreateContextCatalog());
		KeyChord undo = Chord(KeyCode.Z);
		Analysis replaced = Replace(subject, TestCommand.Build, [undo]);

		Assert.AreEqual(KeyBindingOutcome.Conflict, Reject(subject, TestCommand.Build, [undo]).Outcome);
		Assert.AreEqual(KeyBindingOutcome.Succeeded, replaced.Outcome);

		Assert.AreSequenceEqual([undo], replaced.Conflicts[TestCommand.Undo]);
	}

	[TestMethod]
	public void Analyze_ScopedChordAnotherContextOwns_Succeeds()
	{
		// Find owns Control+W in the texture context only, which is never active together with the geometry context.
		Analysis analysis = Reject(CreateSubject(CreateTwoContextCatalog()), TestCommand.Build, [s_controlW]);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, analysis.Outcome);
		Assert.IsEmpty(analysis.Conflicts);
	}

	[TestMethod]
	public void CollidesWithPreservedEntry_WithoutAPreservedEntry_ReturnsFalse()
	{
		Subject subject = CreateSubject(CreateCatalog());
		PublishedSnapshot<TestCommand> candidate = BuildMaps(subject.Catalog, OverridesOf(Entry(nameof(TestCommand.Save), s_controlQ)));

		Assert.IsFalse(subject.Detector.CollidesWithPreservedEntry(candidate, new KeyBindingOverrides(), subject.Published));
	}

	[TestMethod]
	public void CollidesWithPreservedEntry_NewClaimOfThePreservedChord_ReturnsTrue()
	{
		Subject subject = CreateSubject(CreateCatalog());
		PublishedSnapshot<TestCommand> candidate = BuildMaps(subject.Catalog, OverridesOf(Entry(nameof(TestCommand.Save), s_controlQ)));

		Assert.IsTrue(subject.Detector.CollidesWithPreservedEntry(candidate, OverridesOf(Entry(PreservedId, s_controlQ)), subject.Published));
	}

	[TestMethod]
	public void CollidesWithPreservedEntry_NewClaimThatExtendsThePreservedChord_ReturnsTrue()
	{
		Subject subject = CreateSubject(CreateCatalog());
		PublishedSnapshot<TestCommand> candidate = BuildMaps(subject.Catalog, OverridesOf(Entry(nameof(TestCommand.Save), s_controlQThenW)));

		Assert.IsTrue(subject.Detector.CollidesWithPreservedEntry(candidate, OverridesOf(Entry(PreservedId, s_controlQ)), subject.Published));
	}

	[TestMethod]
	public void CollidesWithPreservedEntry_NewClaimThePreservedChordExtends_ReturnsTrue()
	{
		Subject subject = CreateSubject(CreateCatalog());
		PublishedSnapshot<TestCommand> candidate = BuildMaps(subject.Catalog, OverridesOf(Entry(nameof(TestCommand.Save), s_controlQ)));

		Assert.IsTrue(subject.Detector.CollidesWithPreservedEntry(candidate, OverridesOf(Entry(PreservedId, s_controlQThenW)), subject.Published));
	}

	[TestMethod]
	public void CollidesWithPreservedEntry_ClaimTheCurrentStateAlreadyHolds_ReturnsFalse()
	{
		Subject subject = CreateSubject(CreateCatalog());
		KeyBindingOverrides applied = OverridesOf(Entry(PreservedId, Chord(KeyCode.S)));

		// Control+S is Save's default and already published, so a candidate that only keeps it introduces no claim.
		Assert.IsFalse(subject.Detector.CollidesWithPreservedEntry(subject.Published, applied, subject.Published));
	}

	[TestMethod]
	public void CollidesWithPreservedEntry_EntryOfACatalogedCommand_IsNotPreserved()
	{
		Subject subject = CreateSubject(CreateCatalog());
		PublishedSnapshot<TestCommand> candidate = BuildMaps(subject.Catalog, OverridesOf(Entry(nameof(TestCommand.Save), s_controlQ)));

		Assert.IsFalse(
			subject.Detector.CollidesWithPreservedEntry(candidate, OverridesOf(Entry(nameof(TestCommand.NewFile), s_controlQ)), subject.Published)
		);
	}

	[TestMethod]
	public void CollidesWithPreservedEntry_UnrelatedPreservedChord_ReturnsFalse()
	{
		Subject subject = CreateSubject(CreateCatalog());
		PublishedSnapshot<TestCommand> candidate = BuildMaps(subject.Catalog, OverridesOf(Entry(nameof(TestCommand.Save), s_controlQ)));

		Assert.IsFalse(subject.Detector.CollidesWithPreservedEntry(candidate, OverridesOf(Entry(PreservedId, s_controlW)), subject.Published));
	}

	[TestMethod]
	public void CollidesWithPreservedEntry_PreservedBindingThatCannotBeParsed_IsSkippedWithoutLogging()
	{
		var logger = new RecordingLogger();

		Subject subject = CreateSubject(CreateCatalog(), logger);
		PublishedSnapshot<TestCommand> candidate = BuildMaps(subject.Catalog, OverridesOf(Entry(nameof(TestCommand.Save), s_controlQ)));

		var preserved = new KeyBindingOverrideEntry { SerializedId = PreservedId };

		preserved.Bindings.Add(new KeyBindingOverrideChord { Strokes = [new KeyBindingOverrideStroke { KeyName = "NotAKey", Modifiers = 0 }] });
		preserved.Bindings.Add(KeyBindingOverrideCodec.CreateBinding(s_controlW));

		Assert.IsFalse(subject.Detector.CollidesWithPreservedEntry(candidate, OverridesOf(preserved), subject.Published));
		Assert.IsEmpty(logger.Entries);
	}

	private sealed record Subject(
		KeyBindingConflictDetector<TestCommand> Detector,
		CommandCatalog<TestCommand> Catalog,
		PublishedSnapshot<TestCommand> Published
	);

	private sealed record Analysis(KeyBindingOutcome Outcome, Dictionary<TestCommand, List<KeyChord>> Conflicts);

	private static KeyChord Chord(KeyCode key, KeyModifiers modifiers = KeyModifiers.Control) => new(new KeyStroke(key, modifiers));

	private static KeyBindingOverrideEntry Entry(string serializedId, params KeyChord[] chords) =>
		KeyBindingOverrideCodec.CreateEntry(serializedId, chords);

	private static KeyBindingOverrides OverridesOf(params KeyBindingOverrideEntry[] entries) => new() { Entries = [.. entries] };

	private static Subject CreateSubject(CommandCatalog<TestCommand> catalog, ILogger? logger = null)
	{
		HashSet<string> serializedIds = [.. catalog.Descriptors.Select(descriptor => descriptor.SerializedId)];

		var detector = new KeyBindingConflictDetector<TestCommand>(
			catalog,
			serializedIds,
			new KeyBindingOverrideCodec(logger ?? NullLogger.Instance)
		);

		return new Subject(detector, catalog, BuildMaps(catalog, new KeyBindingOverrides()));
	}

	private static PublishedSnapshot<TestCommand> BuildMaps(CommandCatalog<TestCommand> catalog, KeyBindingOverrides overrides)
	{
		var builder = new KeyBindingMapBuilder<TestCommand>(catalog, NullLogger.Instance, KeyDisplayTextFormatter.Default);

		KeyBindingMapBuilder<TestCommand>.MapBuildResult result = builder.Build(overrides, logDiagnostics: false);

		Assert.IsTrue(result.Succeeded, result.Reason);
		Assert.IsNotNull(result.Maps);

		return result.Maps;
	}

	private static Analysis Reject(Subject subject, TestCommand command, IReadOnlyList<KeyChord> bindings) =>
		Analyze(subject, command, bindings, policy: KeyBindingConflictPolicy.Reject);

	private static Analysis Replace(Subject subject, TestCommand command, IReadOnlyList<KeyChord> bindings) =>
		Analyze(subject, command, bindings, policy: KeyBindingConflictPolicy.Replace);

	private static Analysis Analyze(Subject subject, TestCommand command, IReadOnlyList<KeyChord> bindings, KeyBindingConflictPolicy policy)
	{
		if (!subject.Catalog.TryGetDescriptor(command, out CommandDescriptor<TestCommand>? descriptor))
			Assert.Fail($"{command} is not cataloged.");

		KeyBindingOutcome outcome = subject.Detector.Analyze(
			descriptor,
			bindings,
			conflictPolicy: policy,
			published: subject.Published,
			out Dictionary<TestCommand, List<KeyChord>> conflictsByOwner
		);

		return new Analysis(outcome, conflictsByOwner);
	}

	private static CommandCatalog<TestCommand> CreateTwoContextCatalog()
	{
		return new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(
				TestCommand.Build,
				nameof(TestCommand.Build),
				CommandRemappingPolicy.Remappable,
				new KeyStroke(KeyCode.E, KeyModifiers.Control)
			)
			{
				Context = GeometryContext,
			},

			new CommandDescriptor<TestCommand>(
				TestCommand.Find,
				nameof(TestCommand.Find),
				CommandRemappingPolicy.Remappable,
				new KeyStroke(KeyCode.W, KeyModifiers.Control)
			)
			{
				Context = TextureContext,
			},
		]);
	}
}
