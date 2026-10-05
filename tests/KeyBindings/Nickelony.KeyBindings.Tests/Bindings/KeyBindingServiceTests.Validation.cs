using Nickelony.KeyBindings.Testing;
using static Nickelony.KeyBindings.Testing.KeyBindingTestHelpers;

namespace Nickelony.KeyBindings.Tests;

/// <summary>
/// Covers proposal validation:
/// the outcome for free, owned, duplicate, conflicting, unknown, host-managed, host-reserved, uninitialized, preserved-entry, and chord combinations.
/// </summary>
public partial class KeyBindingServiceTests
{
	[TestMethod]
	public void Validate_WithReplacePolicy_AcceptsARemappableConflict()
	{
		KeyBindingService<TestCommand> service = CreateReplacementService();

		// Undo owns Control+Z: the default policy reports the conflict, and the Replace policy accepts the
		// proposal because Undo is remappable.
		Assert.AreEqual(KeyBindingOutcome.Conflict, service.Validate(TestCommand.Save, [new KeyStroke(KeyCode.Z, KeyModifiers.Control)]));

		Assert.AreEqual(
			KeyBindingOutcome.Succeeded,
			service.Validate(TestCommand.Save, [new KeyStroke(KeyCode.Z, KeyModifiers.Control)], KeyBindingConflictPolicy.Replace)
		);
	}

	[TestMethod]
	public void Validate_WithReplacePolicy_RejectsAnUnremappableConflict()
	{
		KeyBindingService<TestCommand> service = CreateService();

		// Redo is host-managed, so Replace cannot take its stroke and the policy does not change the outcome.
		Assert.AreEqual(
			KeyBindingOutcome.Conflict,
			service.Validate(TestCommand.Save, [new KeyStroke(KeyCode.Y, KeyModifiers.Control)], KeyBindingConflictPolicy.Replace)
		);
	}

	[TestMethod]
	public void Validate_UndefinedConflictPolicy_Throws()
	{
		KeyBindingService<TestCommand> service = CreateService();

		Assert.ThrowsExactly<ArgumentException>(() =>
			service.Validate(TestCommand.Save, [new KeyStroke(KeyCode.F5, KeyModifiers.None)], (KeyBindingConflictPolicy)42)
		);
	}

	[TestMethod]
	public void Validate_FreeStroke_ReturnsSuccess() =>
		Assert.AreEqual(KeyBindingOutcome.Succeeded, CreateService().Validate(TestCommand.Save, [new KeyStroke(KeyCode.F5, KeyModifiers.None)]));

	[TestMethod]
	public void Validate_OwnCurrentBinding_ReturnsSuccess() =>
		Assert.AreEqual(KeyBindingOutcome.Succeeded, CreateService().Validate(TestCommand.Save, [new KeyStroke(KeyCode.S, KeyModifiers.Control)]));

	[TestMethod]
	public void Validate_EmptySet_ReturnsSuccess() => Assert.AreEqual(KeyBindingOutcome.Succeeded, CreateService().Validate(TestCommand.Save, []));

	[TestMethod]
	public void Validate_DuplicateInSet_ReturnsDuplicateInCommand()
	{
		Assert.AreEqual(
			KeyBindingOutcome.DuplicateInCommand,
			CreateService().Validate(TestCommand.Save, [new KeyStroke(KeyCode.F5, KeyModifiers.None), new KeyStroke(KeyCode.F5, KeyModifiers.None)])
		);
	}

	[TestMethod]
	public void Validate_OtherCommandsBinding_ReturnsConflict() =>
		Assert.AreEqual(KeyBindingOutcome.Conflict, CreateService().Validate(TestCommand.Save, [new KeyStroke(KeyCode.Z, KeyModifiers.Control)]));

	[TestMethod]
	public void Validate_UnknownCommand_ReturnsUnknownCommand() =>
		Assert.AreEqual(KeyBindingOutcome.UnknownCommand, CreateService().Validate(TestCommand.None, [new KeyStroke(KeyCode.F5, KeyModifiers.None)]));

	[TestMethod]
	public void Validate_HostManagedCommand_ReturnsHostManaged() =>
		Assert.AreEqual(KeyBindingOutcome.HostManaged, CreateService().Validate(TestCommand.Redo, [new KeyStroke(KeyCode.F5, KeyModifiers.None)]));

	[TestMethod]
	public void Validate_HostReservedCommand_ReturnsHostReserved() =>
		Assert.AreEqual(KeyBindingOutcome.HostReserved, CreateService().Validate(TestCommand.Exit, [new KeyStroke(KeyCode.F5, KeyModifiers.None)]));

	[TestMethod]
	public void Validate_UninitializedStroke_Throws() =>
		Assert.ThrowsExactly<ArgumentException>(() => CreateService().Validate(TestCommand.Save, [default]));

	[TestMethod]
	public void Validate_StrokeClaimedByAPreservedEntry_ReturnsConflict()
	{
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.None), ("X", (int)KeyModifiers.Control));
		KeyBindingService<TestCommand> service = CreateService(overrides);

		// The preserved entry is not part of the runtime map, but a later run may catalog its command, so an
		// overlapping proposal could produce a document this service cannot load.
		Assert.AreEqual(KeyBindingOutcome.Conflict, service.Validate(TestCommand.Save, [new KeyStroke(KeyCode.X, KeyModifiers.Control)]));
	}

	[TestMethod]
	public void Validate_StrokeInAPrefixRelationshipWithAPreservedEntry_ReturnsConflict()
	{
		// The preserved entry's chord is the two-stroke Ctrl+X, Ctrl+K; a single-stroke Ctrl+X proposal is a strict
		// prefix of it, so a later run that catalogs the preserved command could not load the produced document.
		KeyBindingOverrides chordOverrides = CreateChordOverrides(
			nameof(TestCommand.None),
			[("X", (int)KeyModifiers.Control), ("K", (int)KeyModifiers.None)]
		);

		Assert.AreEqual(
			KeyBindingOutcome.Conflict,
			CreateService(chordOverrides).Validate(TestCommand.Save, [new KeyStroke(KeyCode.X, KeyModifiers.Control)])
		);

		// The reverse direction: a proposal that is longer than the preserved entry's chord is rejected too.
		KeyBindingOverrides singleStrokeOverrides = CreateOverrides(nameof(TestCommand.None), ("X", (int)KeyModifiers.Control));
		var longerProposal = new KeyChord([new KeyStroke(KeyCode.X, KeyModifiers.Control), new KeyStroke(KeyCode.K, KeyModifiers.None)]);

		Assert.AreEqual(KeyBindingOutcome.Conflict, CreateService(singleStrokeOverrides).Validate(TestCommand.Save, [longerProposal]));
	}

	[TestMethod]
	public void Validate_ChordThatIsAPrefixOfAnotherCommandsChord_ReturnsPrefixConflict()
	{
		KeyBindingService<TestCommand> service = CreateChordService();
		KeyChord prefix = new(new KeyStroke(KeyCode.K, KeyModifiers.Control));

		Assert.AreEqual(KeyBindingOutcome.PrefixConflict, service.Validate(TestCommand.Build, [prefix]));

		// Replace resolves competing claims, not prefix relationships: the shorter chord would always
		// resolve first, so taking the other chord over cannot make both reachable.
		Assert.AreEqual(KeyBindingOutcome.PrefixConflict, service.Validate(TestCommand.Build, [prefix], KeyBindingConflictPolicy.Replace));
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
					new KeyChord(new KeyStroke(KeyCode.F8, KeyModifiers.None)),
					new KeyChord(new KeyStroke(KeyCode.F8, KeyModifiers.None), new KeyStroke(KeyCode.F9, KeyModifiers.None)),
				]
			)
		);
	}

	/// <summary>
	/// Pins the documented failure precedence: when one call breaks more than one rule at once, the outcome is the highest-precedence failure
	/// (UnknownCommand, HostReserved, HostManaged, DuplicateInCommand, PrefixConflict, then Conflict).
	/// </summary>
	[TestMethod]
	public void Validate_MultipleFaults_ReportsTheHighestPrecedenceFailure()
	{
		KeyBindingService<TestCommand> service = CreateService();
		KeyBindingService<TestCommand> chordService = CreateChordService();

		var free = new KeyStroke(KeyCode.F5, KeyModifiers.None);
		var owned = new KeyStroke(KeyCode.Z, KeyModifiers.Control);

		KeyChord prefix = new(new KeyStroke(KeyCode.K, KeyModifiers.Control));

		// UnknownCommand outranks a repeated chord.
		Assert.AreEqual(KeyBindingOutcome.UnknownCommand, service.Validate(TestCommand.None, [free, free]));

		// HostReserved outranks a repeated chord.
		Assert.AreEqual(KeyBindingOutcome.HostReserved, service.Validate(TestCommand.Exit, [free, free]));

		// HostManaged outranks a repeated chord and a conflict with another command.
		Assert.AreEqual(KeyBindingOutcome.HostManaged, service.Validate(TestCommand.Redo, [free, free]));
		Assert.AreEqual(KeyBindingOutcome.HostManaged, service.Validate(TestCommand.Redo, [owned]));

		// DuplicateInCommand outranks a prefix conflict and a conflict with another command.
		Assert.AreEqual(KeyBindingOutcome.DuplicateInCommand, chordService.Validate(TestCommand.Build, [prefix, prefix]));
		Assert.AreEqual(KeyBindingOutcome.DuplicateInCommand, service.Validate(TestCommand.Save, [owned, owned]));

		// PrefixConflict outranks an exact conflict with another command.
		Assert.AreEqual(KeyBindingOutcome.PrefixConflict, chordService.Validate(TestCommand.Build, [prefix, owned]));
	}

	/// <summary>Pins the enum contract: <see cref="KeyBindingOutcome.Unspecified"/> is the zero value, and no operation returns it.</summary>
	[TestMethod]
	public void Operations_NeverReturnUnspecified()
	{
		KeyBindingService<TestCommand> service = CreateService(CreateOverrides(nameof(TestCommand.Save), ("X", (int)KeyModifiers.Control)));
		KeyBindingService<TestCommand> failingStore = CreateService(null, _ => false);

		var stroke = new KeyStroke(KeyCode.F5, KeyModifiers.None);

		List<KeyBindingOutcome> outcomes =
		[
			service.Validate(TestCommand.Save, [stroke]),
			service.Apply(TestCommand.Save, [stroke]),
			service.Reset(TestCommand.Save),
			service.ResetAll(),
			service.Validate(TestCommand.None, [stroke]),
			service.Apply(TestCommand.None, [stroke]),
			service.Validate(TestCommand.Exit, [stroke]),
			service.Apply(TestCommand.Exit, [stroke]),
			service.Validate(TestCommand.Redo, [stroke]),
			service.Apply(TestCommand.Redo, [stroke]),
			service.Validate(TestCommand.Save, [stroke, stroke]),
			service.Apply(TestCommand.Save, [new KeyStroke(KeyCode.Z, KeyModifiers.Control)]),
			CreateChordService().Apply(TestCommand.Build, [new KeyStroke(KeyCode.K, KeyModifiers.Control)]),
			failingStore.Apply(TestCommand.Save, [stroke]),
		];

		// Pinning the enum's zero value is part of the public contract, so the constant comparison is the
		// point rather than an accident MSTEST0032 should flag.
#pragma warning disable MSTEST0032
		Assert.AreEqual(KeyBindingOutcome.Unspecified, default);
#pragma warning restore MSTEST0032
		Assert.DoesNotContain(KeyBindingOutcome.Unspecified, outcomes);

		foreach (KeyBindingOutcome expected in Enum.GetValues<KeyBindingOutcome>())
		{
			if (expected != KeyBindingOutcome.Unspecified)
				Assert.Contains(expected, outcomes, $"No operation in the battery reported {expected}.");
		}
	}
}
