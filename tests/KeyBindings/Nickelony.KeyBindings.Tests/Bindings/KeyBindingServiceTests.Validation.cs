using Nickelony.KeyBindings.Testing;
using static Nickelony.KeyBindings.Testing.KeyBindingTestFixtures;

namespace Nickelony.KeyBindings.Tests;

/// <summary>
/// Covers proposal validation: the outcome for free, owned, duplicate, conflicting, unknown,
/// host-managed, host-reserved, uninitialized, preserved-entry, and chord combinations.
/// </summary>
public partial class KeyBindingServiceTests
{
	[TestMethod]
	public void Validate_WithReplacePolicy_AcceptsARemappableConflict()
	{
		KeyBindingService<TestCommand> service = CreateReplacementService();

		// Undo owns Control+Z: the default policy reports the conflict, and the Replace policy accepts the
		// proposal because Undo is remappable.
		Assert.AreEqual(
			KeyBindingOutcome.Conflict,
			service.Validate(TestCommand.Save, [new KeyCombo(KeyCode.Z, KeyModifierSet.Control)]));
		Assert.AreEqual(
			KeyBindingOutcome.Succeeded,
			service.Validate(TestCommand.Save, [new KeyCombo(KeyCode.Z, KeyModifierSet.Control)], KeyBindingConflictPolicy.Replace));
	}

	[TestMethod]
	public void Validate_WithReplacePolicy_RejectsAnUnremappableConflict()
	{
		KeyBindingService<TestCommand> service = CreateService();

		// Redo is host-managed, so Replace cannot take its combo and the policy does not change the outcome.
		Assert.AreEqual(
			KeyBindingOutcome.Conflict,
			service.Validate(TestCommand.Save, [new KeyCombo(KeyCode.Y, KeyModifierSet.Control)], KeyBindingConflictPolicy.Replace));
	}

	[TestMethod]
	public void Validate_UndefinedConflictPolicy_Throws()
	{
		KeyBindingService<TestCommand> service = CreateService();

		Assert.ThrowsExactly<ArgumentException>(() =>
			service.Validate(TestCommand.Save, [new KeyCombo(KeyCode.F5, KeyModifierSet.None)], (KeyBindingConflictPolicy)42));
	}

	[TestMethod]
	public void Validate_FreeCombo_ReturnsSuccess()
		=> Assert.AreEqual(KeyBindingOutcome.Succeeded,
			CreateService().Validate(TestCommand.Save, [new KeyCombo(KeyCode.F5, KeyModifierSet.None)]));

	[TestMethod]
	public void Validate_OwnCurrentBinding_ReturnsSuccess()
		=> Assert.AreEqual(KeyBindingOutcome.Succeeded,
			CreateService().Validate(TestCommand.Save, [new KeyCombo(KeyCode.S, KeyModifierSet.Control)]));

	[TestMethod]
	public void Validate_EmptySet_ReturnsSuccess()
		=> Assert.AreEqual(KeyBindingOutcome.Succeeded, CreateService().Validate(TestCommand.Save, []));

	[TestMethod]
	public void Validate_DuplicateInSet_ReturnsDuplicateInCommand()
		=> Assert.AreEqual(KeyBindingOutcome.DuplicateInCommand,
			CreateService().Validate(TestCommand.Save, [new KeyCombo(KeyCode.F5, KeyModifierSet.None), new KeyCombo(KeyCode.F5, KeyModifierSet.None)]));

	[TestMethod]
	public void Validate_OtherCommandsBinding_ReturnsConflict()
		=> Assert.AreEqual(KeyBindingOutcome.Conflict,
			CreateService().Validate(TestCommand.Save, [new KeyCombo(KeyCode.Z, KeyModifierSet.Control)]));

	[TestMethod]
	public void Validate_UnknownCommand_ReturnsUnknownCommand()
		=> Assert.AreEqual(KeyBindingOutcome.UnknownCommand,
			CreateService().Validate(TestCommand.None, [new KeyCombo(KeyCode.F5, KeyModifierSet.None)]));

	[TestMethod]
	public void Validate_HostManagedCommand_ReturnsNotRemappable()
		=> Assert.AreEqual(KeyBindingOutcome.NotRemappable,
			CreateService().Validate(TestCommand.Redo, [new KeyCombo(KeyCode.F5, KeyModifierSet.None)]));

	[TestMethod]
	public void Validate_HostReservedCommand_ReturnsReserved()
		=> Assert.AreEqual(KeyBindingOutcome.Reserved,
			CreateService().Validate(TestCommand.Exit, [new KeyCombo(KeyCode.F5, KeyModifierSet.None)]));

	[TestMethod]
	public void Validate_UninitializedCombo_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => CreateService().Validate(TestCommand.Save, [default]));

	[TestMethod]
	public void Validate_ComboClaimedByAPreservedEntry_ReturnsConflict()
	{
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.None), ("X", (int)KeyModifierSet.Control));
		KeyBindingService<TestCommand> service = CreateService(overrides);

		// The preserved entry is not part of the runtime map, but a later run may catalog its command, so an
		// overlapping proposal could produce a document this service can no longer load.
		Assert.AreEqual(
			KeyBindingOutcome.Conflict,
			service.Validate(TestCommand.Save, [new KeyCombo(KeyCode.X, KeyModifierSet.Control)]));
	}

	[TestMethod]
	public void Validate_ComboInAPrefixRelationshipWithAPreservedEntry_ReturnsConflict()
	{
		// The preserved entry's chord is the two-stroke Ctrl+X, Ctrl+K; a single-stroke Ctrl+X proposal is a strict
		// prefix of it, so a later run that catalogs the preserved command could not load the produced document.
		KeyBindingOverrides chordOverrides = CreateChordOverrides(
			nameof(TestCommand.None),
			[("X", (int)KeyModifierSet.Control), ("K", (int)KeyModifierSet.None)]);

		Assert.AreEqual(
			KeyBindingOutcome.Conflict,
			CreateService(chordOverrides).Validate(TestCommand.Save, [new KeyCombo(KeyCode.X, KeyModifierSet.Control)]));

		// The reverse direction: a proposal that is longer than the preserved entry's chord is rejected too.
		KeyBindingOverrides singleStrokeOverrides = CreateOverrides(nameof(TestCommand.None), ("X", (int)KeyModifierSet.Control));
		var longerProposal = new KeyChord([new KeyCombo(KeyCode.X, KeyModifierSet.Control), new KeyCombo(KeyCode.K, KeyModifierSet.None)]);

		Assert.AreEqual(
			KeyBindingOutcome.Conflict,
			CreateService(singleStrokeOverrides).Validate(TestCommand.Save, [longerProposal]));
	}

	[TestMethod]
	public void Validate_ChordThatIsAPrefixOfAnotherCommandsChord_ReturnsPrefixConflict()
	{
		KeyBindingService<TestCommand> service = CreateChordService();
		KeyChord prefix = new(new KeyCombo(KeyCode.K, KeyModifierSet.Control));

		Assert.AreEqual(KeyBindingOutcome.PrefixConflict, service.Validate(TestCommand.Build, [prefix]));

		// Replace resolves competing claims, not prefix relationships: the shorter chord would always
		// resolve first, so taking the other chord over cannot make both reachable.
		Assert.AreEqual(
			KeyBindingOutcome.PrefixConflict,
			service.Validate(TestCommand.Build, [prefix], KeyBindingConflictPolicy.Replace));
	}

	[TestMethod]
	public void Validate_ChordBuiltFromAnotherPrefixInsideTheSet_ReturnsPrefixConflict()
	{
		KeyBindingService<TestCommand> service = CreateChordService();

		Assert.AreEqual(
			KeyBindingOutcome.PrefixConflict,
			service.Validate(
				TestCommand.Build,
				[
					new KeyChord(new KeyCombo(KeyCode.F8, KeyModifierSet.None)),
					new KeyChord(new KeyCombo(KeyCode.F8, KeyModifierSet.None), new KeyCombo(KeyCode.F9, KeyModifierSet.None))
				]));
	}

	/// <summary>
	/// Pins the enum contract: <see cref="KeyBindingOutcome.Unspecified"/> is the zero value, and no
	/// operation returns it. The battery produces every other member, so a regression that started
	/// reporting the unassigned value - or stopped reaching one of the outcomes - fails here.
	/// </summary>
	[TestMethod]
	public void Operations_NeverReturnUnspecified()
	{
		KeyBindingService<TestCommand> service = CreateService(CreateOverrides(nameof(TestCommand.Save), ("X", (int)KeyModifierSet.Control)));
		KeyBindingService<TestCommand> failingStore = CreateService(null, _ => false);

		var combo = new KeyCombo(KeyCode.F5, KeyModifierSet.None);

		List<KeyBindingOutcome> outcomes =
		[
			service.Validate(TestCommand.Save, [combo]),
			service.Apply(TestCommand.Save, [combo]),
			service.Reset(TestCommand.Save),
			service.ResetAll(),
			service.Validate(TestCommand.None, [combo]),
			service.Apply(TestCommand.None, [combo]),
			service.Validate(TestCommand.Exit, [combo]),
			service.Apply(TestCommand.Exit, [combo]),
			service.Validate(TestCommand.Redo, [combo]),
			service.Apply(TestCommand.Redo, [combo]),
			service.Validate(TestCommand.Save, [combo, combo]),
			service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.Z, KeyModifierSet.Control)]),
			CreateChordService().Apply(TestCommand.Build, [new KeyCombo(KeyCode.K, KeyModifierSet.Control)]),
			failingStore.Apply(TestCommand.Save, [combo])
		];

		// Pinning the enum's zero value is part of the public contract, so the constant comparison is the
		// point rather than an accident MSTEST0032 should flag.
#pragma warning disable MSTEST0032
		Assert.AreEqual(KeyBindingOutcome.Unspecified, default(KeyBindingOutcome));
#pragma warning restore MSTEST0032
		Assert.IsFalse(outcomes.Contains(KeyBindingOutcome.Unspecified));

		foreach (KeyBindingOutcome expected in Enum.GetValues<KeyBindingOutcome>())
		{
			if (expected != KeyBindingOutcome.Unspecified)
				Assert.IsTrue(outcomes.Contains(expected), $"No operation in the battery reported {expected}.");
		}
	}
}
