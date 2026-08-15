using Nickelony.KeyBindings.Testing;
using static Nickelony.KeyBindings.Testing.KeyBindingTestFixtures;

namespace Nickelony.KeyBindings.Tests;

/// <summary>
/// Covers mutation: persisting and rebinding a proposal, the conflict policies (including Replace
/// rewriting owner entries), persistence failures, the save-then-throw window, explicit unbind,
/// preserved entries, chord application, and the mutation-changed event.
/// </summary>
public partial class KeyBindingServiceTests
{
	[TestMethod]
	public void Apply_FreeCombo_StoresOverridePersistsAndRebinds()
	{
		var overrides = new KeyBindingOverrides();
		KeyBindingOverrides? saved = null;
		int saveCount = 0;
		int fired = 0;
		KeyBindingService<TestCommand> service = CreateService(overrides, snapshot => { saveCount++; saved = snapshot; return true; });
		service.BindingsChanged += (_, _) => fired++;

		KeyBindingOutcome outcome = service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.X, KeyModifierSet.Control)]);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, outcome);

		// A successful mutation persists exactly one snapshot, so the document never trails the runtime
		// state.
		Assert.AreEqual(1, saveCount);
		Assert.AreEqual(1, fired);
		Assert.AreEqual(new KeyCombo(KeyCode.X, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);
		Assert.IsFalse(service.TryGetCommand(new KeyCombo(KeyCode.S, KeyModifierSet.Control), null, out _));
		Assert.IsNotNull(saved);
		Assert.AreEqual(KeyBindingOverrides.CurrentVersion, saved.Version);
		Assert.HasCount(1, saved.Entries);
		Assert.AreEqual(nameof(TestCommand.Save), saved.Entries[0].SerializedId);
		Assert.AreEqual("X", saved.Entries[0].Bindings[0].Strokes[0].KeyName);
		Assert.AreEqual((int)KeyModifierSet.Control, saved.Entries[0].Bindings[0].Strokes[0].Modifiers);

		// The store document is owned by the host and is never mutated by the service.
		Assert.HasCount(0, overrides.Entries);
	}

	[TestMethod]
	public void Apply_PersistenceFailure_ReturnsPersistenceFailedAndKeepsState()
	{
		var overrides = new KeyBindingOverrides();
		int fired = 0;
		KeyBindingService<TestCommand> service = CreateService(overrides, _ => false);
		service.BindingsChanged += (_, _) => fired++;

		KeyBindingOutcome outcome = service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.X, KeyModifierSet.Control)]);

		Assert.AreEqual(KeyBindingOutcome.PersistenceFailed, outcome);
		Assert.AreEqual(0, fired);
		Assert.AreEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);
		Assert.IsEmpty(overrides.Entries);
	}

	/// <summary>
	/// Pins the documented throw channel of the mutation APIs: an exception from the overrides store is
	/// the host's to handle, and the runtime state is left untouched.
	/// </summary>
	[TestMethod]
	public void Apply_StoreThrows_PropagatesAndKeepsState()
	{
		int fired = 0;
		KeyBindingService<TestCommand> service = CreateService(null, _ => throw new InvalidOperationException("The store failed."));
		service.BindingsChanged += (_, _) => fired++;

		Assert.ThrowsExactly<InvalidOperationException>(
			() => service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.X, KeyModifierSet.Control)]));

		Assert.AreEqual(0, fired);
		Assert.IsTrue(service.TryGetCommand(new KeyCombo(KeyCode.S, KeyModifierSet.Control), null, out TestCommand command));
		Assert.AreEqual(TestCommand.Save, command);
	}

	/// <summary>
	/// Pins the documented save-then-throw divergence window: the service persists before it publishes, so
	/// a store that writes the snapshot and then throws leaves the document ahead of the runtime state
	/// instead of rolling either of them back.
	/// </summary>
	[TestMethod]
	public void Apply_StorePersistsThenThrows_LeavesTheDocumentAheadOfTheRuntimeState()
	{
		KeyBindingOverrides? persisted = null;

		KeyBindingService<TestCommand> service = CreateService(
			null,
			snapshot =>
			{
				persisted = snapshot;
				throw new InvalidOperationException("The store failed after writing.");
			});

		Assert.ThrowsExactly<InvalidOperationException>(
			() => service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.X, KeyModifierSet.Control)]));

		Assert.IsNotNull(persisted);
		Assert.AreEqual("X", persisted.Entries[0].Bindings[0].Strokes[0].KeyName);
		Assert.AreEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);
	}

	[TestMethod]
	public void Apply_IsolatedFromSavedSnapshot()
	{
		KeyBindingOverrides? saved = null;
		KeyBindingService<TestCommand> service = CreateService(null, snapshot => { saved = snapshot; return true; });

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.X, KeyModifierSet.Control)]));

		Assert.IsNotNull(saved);
		saved.Entries[0].Bindings.Clear();
		saved.Entries[0].Bindings.Add(SingleStroke("Q", KeyModifierSet.None));

		Assert.AreEqual(new KeyCombo(KeyCode.X, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);
	}

	/// <summary>
	/// Pins the commit ordering: the display text formatter runs before anything is committed, so a
	/// formatter that throws cannot leave the document, the applied overrides, and the published maps
	/// disagreeing.
	/// </summary>
	[TestMethod]
	public void Apply_ThrowingFormatter_DoesNotPersistOrPublish()
	{
		int fired = 0;
		int saveCount = 0;

		KeyBindingService<TestCommand> service = CreateService(
			null,
			snapshot =>
			{
				saveCount++;
				return true;
			},
			displayTextFormatter: new ThrowingFormatter(KeyCode.X));
		service.BindingsChanged += (_, _) => fired++;

		Assert.ThrowsExactly<InvalidOperationException>(
			() => service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.X, KeyModifierSet.Control)]));

		Assert.AreEqual(0, saveCount);
		Assert.AreEqual(0, fired);
		Assert.AreEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);

		// A later mutation works normally, so the failed apply left no half-applied state behind.
		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.W, KeyModifierSet.Control)]));
		Assert.AreEqual(1, saveCount);
		Assert.AreEqual(1, fired);
		Assert.AreEqual(new KeyCombo(KeyCode.W, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);
	}

	/// <summary>
	/// Pins that the overrides store receives its own copy of the candidate: a store that mutates the
	/// snapshot it is handed during <c>Save</c> cannot reach the state the service applies.
	/// </summary>
	[TestMethod]
	public void Apply_StoreMutatesTheSnapshotDuringSave_DoesNotAffectTheService()
	{
		KeyBindingService<TestCommand> service = CreateService(
			null,
			snapshot =>
			{
				snapshot.Entries.Clear();

				return true;
			});

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.X, KeyModifierSet.Control)]));
		Assert.AreEqual(new KeyCombo(KeyCode.X, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);

		// The applied override state must be the candidate too: a state the store had cleared would make
		// the reset a no-op and leave the applied chord bound.
		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Reset(TestCommand.Save));
		Assert.AreEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);
	}

	[TestMethod]
	public void Apply_EmptySet_StoresExplicitUnbind()
	{
		KeyBindingOverrides? saved = null;
		KeyBindingService<TestCommand> service = CreateService(null, snapshot => { saved = snapshot; return true; });

		KeyBindingOutcome outcome = service.Apply(TestCommand.Save, []);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, outcome);
		Assert.IsEmpty(service.GetBindings(TestCommand.Save));
		Assert.IsFalse(service.TryGetCommand(new KeyCombo(KeyCode.S, KeyModifierSet.Control), null, out _));
		Assert.IsNotNull(saved);
		Assert.HasCount(1, saved.Entries);
		Assert.IsEmpty(saved.Entries[0].Bindings);
	}

	[TestMethod]
	public void Apply_ReplaceConflict_ShadowsOwnerDefaultWithoutRewritingIt()
	{
		KeyBindingOverrides? saved = null;
		KeyBindingService<TestCommand> service = CreateReplacementService(saveOverrides: snapshot => { saved = snapshot; return true; });

		KeyBindingOutcome outcome = service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.Z, KeyModifierSet.Control)], KeyBindingConflictPolicy.Replace);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, outcome);
		Assert.IsTrue(service.TryGetCommand(new KeyCombo(KeyCode.Z, KeyModifierSet.Control), null, out TestCommand ownerCommand));
		Assert.AreEqual(TestCommand.Save, ownerCommand);
		Assert.AreEqual(new KeyCombo(KeyCode.U, KeyModifierSet.Control), service.GetBindings(TestCommand.Undo)[0]);

		// Only the target command is persisted; the owner's catalog default is shadowed, not rewritten.
		Assert.IsNotNull(saved);
		Assert.HasCount(1, saved.Entries);
		Assert.AreEqual(nameof(TestCommand.Save), saved.Entries[0].SerializedId);
	}

	[TestMethod]
	public void Apply_ReplaceConflict_WithOverrideBackedOwner_RewritesOwnerEntry()
	{
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.Undo), ("Z", (int)KeyModifierSet.Control), ("U", (int)KeyModifierSet.Control));
		KeyBindingOverrides? saved = null;
		KeyBindingService<TestCommand> service = CreateReplacementService(overrides, snapshot => { saved = snapshot; return true; });

		KeyBindingOutcome outcome = service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.Z, KeyModifierSet.Control)], KeyBindingConflictPolicy.Replace);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, outcome);
		Assert.IsNotNull(saved);
		Assert.HasCount(2, saved.Entries);
		KeyBindingOverrideEntry undoEntry = saved.Entries.Single(entry => entry.SerializedId == nameof(TestCommand.Undo));
		Assert.AreEqual("U", undoEntry.Bindings[0].Strokes[0].KeyName);
	}

	[TestMethod]
	public void Apply_ReplaceConflict_WithSingleOwnerBinding_RemovesOwnerEntry()
	{
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.Undo), ("Z", (int)KeyModifierSet.Control));
		KeyBindingOverrides? saved = null;
		KeyBindingService<TestCommand> service = CreateReplacementService(overrides, snapshot => { saved = snapshot; return true; });

		KeyBindingOutcome outcome = service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.Z, KeyModifierSet.Control)], KeyBindingConflictPolicy.Replace);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, outcome);
		Assert.IsNotNull(saved);
		Assert.HasCount(1, saved.Entries);
		Assert.AreEqual(nameof(TestCommand.Save), saved.Entries[0].SerializedId);

		// The owner falls back to its catalog defaults, whose conflicting combo stays shadowed.
		Assert.AreEqual(new KeyCombo(KeyCode.U, KeyModifierSet.Control), service.GetBindings(TestCommand.Undo)[0]);
	}

	[TestMethod]
	public void Apply_ReplaceConflict_WithMultipleOwners_RewritesEveryOwner()
	{
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.Undo), ("Z", (int)KeyModifierSet.Control));
		overrides.Entries.Add(new KeyBindingOverrideEntry
		{
			SerializedId = nameof(TestCommand.Redo),
			Bindings = [SingleStroke("Y", KeyModifierSet.Control)]
		});
		KeyBindingService<TestCommand> service = CreateReplacementService(overrides);

		KeyBindingOutcome outcome = service.Apply(
			TestCommand.Save,
			[new KeyCombo(KeyCode.Z, KeyModifierSet.Control), new KeyCombo(KeyCode.Y, KeyModifierSet.Control)],
			KeyBindingConflictPolicy.Replace);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, outcome);
		Assert.IsTrue(service.TryGetCommand(new KeyCombo(KeyCode.Z, KeyModifierSet.Control), null, out TestCommand ownerCommand));
		Assert.AreEqual(TestCommand.Save, ownerCommand);
		Assert.IsTrue(service.TryGetCommand(new KeyCombo(KeyCode.Y, KeyModifierSet.Control), null, out ownerCommand));
		Assert.AreEqual(TestCommand.Save, ownerCommand);

		// Both owners fall back to their catalog defaults; only the shadowed combos are omitted.
		Assert.AreEqual(new KeyCombo(KeyCode.U, KeyModifierSet.Control), service.GetBindings(TestCommand.Undo)[0]);
		Assert.IsEmpty(service.GetBindings(TestCommand.Redo));
	}

	[TestMethod]
	public void Apply_ReplaceConflict_WithHostManagedOwner_ReturnsConflict()
	{
		KeyBindingService<TestCommand> service = CreateService();

		KeyBindingOutcome outcome = service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.Y, KeyModifierSet.Control)], KeyBindingConflictPolicy.Replace);

		Assert.AreEqual(KeyBindingOutcome.Conflict, outcome);
		Assert.AreEqual(new KeyCombo(KeyCode.Y, KeyModifierSet.Control), service.GetBindings(TestCommand.Redo)[0]);
	}

	[TestMethod]
	public void Apply_ReplaceConflict_WithHostReservedOwner_ReturnsConflict()
	{
		KeyBindingService<TestCommand> service = CreateService();

		KeyBindingOutcome outcome = service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.F4, KeyModifierSet.Alt)], KeyBindingConflictPolicy.Replace);

		Assert.AreEqual(KeyBindingOutcome.Conflict, outcome);
		Assert.AreEqual(new KeyCombo(KeyCode.F4, KeyModifierSet.Alt), service.GetBindings(TestCommand.Exit)[0]);
	}

	[TestMethod]
	public void Apply_DefaultConflictPolicy_RejectsConflicts()
	{
		KeyBindingOverrides? saved = null;
		KeyBindingService<TestCommand> service = CreateService(null, snapshot => { saved = snapshot; return true; });

		KeyBindingOutcome outcome = service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.Z, KeyModifierSet.Control)]);

		Assert.AreEqual(KeyBindingOutcome.Conflict, outcome);
		Assert.IsNull(saved);
		Assert.AreEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);
	}

	[TestMethod]
	public void Apply_PolicyOutcomes_AreReportedPerCommand()
	{
		KeyBindingService<TestCommand> service = CreateService();

		Assert.AreEqual(KeyBindingOutcome.Reserved,
			service.Apply(TestCommand.Exit, [new KeyCombo(KeyCode.F5, KeyModifierSet.None)]));
		Assert.AreEqual(KeyBindingOutcome.NotRemappable,
			service.Apply(TestCommand.Redo, [new KeyCombo(KeyCode.F5, KeyModifierSet.None)]));
		Assert.AreEqual(KeyBindingOutcome.UnknownCommand,
			service.Apply(TestCommand.None, [new KeyCombo(KeyCode.F5, KeyModifierSet.None)]));
		Assert.AreEqual(KeyBindingOutcome.DuplicateInCommand,
			service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.F5, KeyModifierSet.None), new KeyCombo(KeyCode.F5, KeyModifierSet.None)]));
	}

	[TestMethod]
	public void Apply_UninitializedCombo_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => CreateService().Apply(TestCommand.Save, [default]));

	[TestMethod]
	public void Apply_UndefinedConflictPolicy_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() =>
			CreateService().Apply(TestCommand.Save, [new KeyCombo(KeyCode.F5, KeyModifierSet.None)], (KeyBindingConflictPolicy)7));

	[TestMethod]
	public void Apply_AfterFailedReplace_StillPublishesLaterChanges()
	{
		KeyBindingOverrides? persisted = null;
		KeyBindingService<TestCommand> service = CreateService(null, snapshot => { persisted = snapshot; return true; });

		Assert.AreEqual(KeyBindingOutcome.Conflict,
			service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.Y, KeyModifierSet.Control)], KeyBindingConflictPolicy.Replace));

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.X, KeyModifierSet.Control)]));

		Assert.AreEqual(new KeyCombo(KeyCode.X, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);
		Assert.IsTrue(service.TryGetCommand(new KeyCombo(KeyCode.X, KeyModifierSet.Control), null, out TestCommand ownerCommand));
		Assert.AreEqual(TestCommand.Save, ownerCommand);
		Assert.IsNotNull(persisted);
	}

	[TestMethod]
	public void Apply_ComboClaimedByAPreservedEntry_IsRejectedForBothPolicies()
	{
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.None), ("X", (int)KeyModifierSet.Control));
		KeyBindingOverrides? saved = null;
		int fired = 0;
		KeyBindingService<TestCommand> service = CreateService(overrides, snapshot => { saved = snapshot; return true; });
		service.BindingsChanged += (_, _) => fired++;

		Assert.AreEqual(
			KeyBindingOutcome.Conflict,
			service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.X, KeyModifierSet.Control)]));
		Assert.AreEqual(
			KeyBindingOutcome.Conflict,
			service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.X, KeyModifierSet.Control)], KeyBindingConflictPolicy.Replace));

		// The rejected proposal is never persisted, so a preserved entry can never share a combo with a
		// cataloged override - the document that would fail to load on a later run is never written.
		Assert.IsNull(saved);
		Assert.AreEqual(0, fired);
		Assert.AreEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);
	}

	[TestMethod]
	public void Apply_FreeComboWithAPreservedEntryPresent_Succeeds()
	{
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.None), ("X", (int)KeyModifierSet.Control));
		KeyBindingService<TestCommand> service = CreateService(overrides);

		Assert.AreEqual(
			KeyBindingOutcome.Succeeded,
			service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.F5, KeyModifierSet.None)]));
	}

	[TestMethod]
	public void BindingsChanged_FiresOncePerMutationAndNotForReads()
	{
		KeyBindingService<TestCommand> service = CreateService();
		int fired = 0;
		KeyBindingsChangedEventArgs<TestCommand>? last = null;
		service.BindingsChanged += (_, args) =>
		{
			fired++;
			last = args;
		};

		service.GetBindings(TestCommand.Save);
		service.GetDisplayText(TestCommand.Save);
		service.Validate(TestCommand.Save, [new KeyCombo(KeyCode.F5, KeyModifierSet.None)]);
		Assert.AreEqual(0, fired);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.X, KeyModifierSet.Control)]));
		Assert.AreEqual(1, fired);

		// The payload names the mutated command and marks the change as per-command, so a host refreshes
		// only that command's presentation.
		Assert.AreEqual(TestCommand.Save, last!.Command);
		Assert.IsFalse(last.AllCommands);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Reset(TestCommand.Save));
		Assert.AreEqual(2, fired);
		Assert.AreEqual(TestCommand.Save, last!.Command);

		// Re-add an override so the next ResetAll has state to clear and actually notifies.
		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.X, KeyModifierSet.Control)]));
		Assert.AreEqual(3, fired);

		// A ResetAll that clears overrides reports the change through AllCommands; the command value is the
		// identity's default (TestCommand.None here) and is not meant to be read.
		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.ResetAll());
		Assert.AreEqual(4, fired);
		Assert.IsTrue(last!.AllCommands);
		Assert.AreEqual(TestCommand.None, last.Command);

		// A ResetAll with nothing left to clear reports success without a notification.
		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.ResetAll());
		Assert.AreEqual(4, fired);
	}

	[TestMethod]
	public void Apply_PrefixConflict_IsRejectedForBothPoliciesAndNeverPersisted()
	{
		KeyBindingOverrides? saved = null;
		KeyBindingService<TestCommand> service = CreateChordService(saveOverrides: snapshot => { saved = snapshot; return true; });
		KeyChord prefix = new(new KeyCombo(KeyCode.K, KeyModifierSet.Control));

		Assert.AreEqual(KeyBindingOutcome.PrefixConflict, service.Apply(TestCommand.Build, [prefix]));
		Assert.AreEqual(KeyBindingOutcome.PrefixConflict, service.Apply(TestCommand.Build, [prefix], KeyBindingConflictPolicy.Replace));

		Assert.IsNull(saved);
		Assert.AreEqual(new KeyCombo(KeyCode.F9, KeyModifierSet.None), service.GetBindings(TestCommand.Build)[0]);
	}

	[TestMethod]
	public void Apply_ChordForAnUnboundPrefix_SucceedsAndBinds()
	{
		KeyBindingService<TestCommand> service = CreateChordService();
		var chord = new KeyChord(new KeyCombo(KeyCode.K, KeyModifierSet.Control), new KeyCombo(KeyCode.Z, KeyModifierSet.Control));

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Apply(TestCommand.Undo, [chord]));

		Assert.IsTrue(service.TryGetCommand(chord, null, out TestCommand undo));
		Assert.AreEqual(TestCommand.Undo, undo);
		Assert.IsTrue(service.IsChordPrefix(new KeyCombo(KeyCode.K, KeyModifierSet.Control), null));
	}

	[TestMethod]
	public void Apply_ChordReplacingItsOwnDefault_RemovesThePlainBinding()
	{
		KeyBindingService<TestCommand> service = CreateChordService();

		Assert.AreEqual(
			KeyBindingOutcome.Succeeded,
			service.Apply(
				TestCommand.Build,
				[new KeyChord(new KeyCombo(KeyCode.F9, KeyModifierSet.None), new KeyCombo(KeyCode.F10, KeyModifierSet.None))]));

		// The command's own old default disappears with the proposal, so no prefix conflict is raised and
		// the old stroke is only a prefix now.
		Assert.IsFalse(service.TryGetCommand(new KeyCombo(KeyCode.F9, KeyModifierSet.None), null, out _));
		Assert.IsTrue(service.IsChordPrefix(new KeyCombo(KeyCode.F9, KeyModifierSet.None), null));
	}

	[TestMethod]
	public void Apply_ChordOverride_RoundTripsThroughTheSnapshot()
	{
		KeyBindingOverrides? saved = null;
		KeyBindingService<TestCommand> service = CreateChordService(saveOverrides: snapshot => { saved = snapshot; return true; });
		var chord = new KeyChord(
			new KeyCombo(KeyCode.K, KeyModifierSet.Control),
			new KeyCombo(KeyCode.Z, KeyModifierSet.Control),
			new KeyCombo(KeyCode.F9, KeyModifierSet.None));

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Apply(TestCommand.Undo, [chord]));

		Assert.IsNotNull(saved);
		Assert.HasCount(1, saved.Entries);
		Assert.HasCount(1, saved.Entries[0].Bindings);
		Assert.HasCount(3, saved.Entries[0].Bindings[0].Strokes);
		Assert.AreEqual("K", saved.Entries[0].Bindings[0].Strokes[0].KeyName);
		Assert.AreEqual((int)KeyModifierSet.Control, saved.Entries[0].Bindings[0].Strokes[0].Modifiers);
		Assert.AreEqual("F9", saved.Entries[0].Bindings[0].Strokes[2].KeyName);
		Assert.AreEqual((int)KeyModifierSet.None, saved.Entries[0].Bindings[0].Strokes[2].Modifiers);

		// The persisted document loads into a fresh service and resolves the same chord.
		KeyBindingService<TestCommand> reloaded = CreateChordService(saved.Clone());

		Assert.IsTrue(reloaded.TryGetCommand(chord, null, out TestCommand undo));
		Assert.AreEqual(TestCommand.Undo, undo);
	}

	/// <summary>
	/// A formatter that throws for one key, so the commit path can be exercised with a formatter failure.
	/// </summary>
	private sealed class ThrowingFormatter : IKeyDisplayTextFormatter
	{
		private readonly KeyCode _throwingKey;

		public ThrowingFormatter(KeyCode throwingKey) => _throwingKey = throwingKey;

		public string GetDisplayText(KeyCombo keyCombo)
			=> keyCombo.Key == _throwingKey ? throw new InvalidOperationException("The formatter failed.") : keyCombo.ToString();

		public string GetDisplayText(KeyChord keyChord)
		{
			var parts = new List<string>(keyChord.StrokeCount);

			foreach (KeyCombo stroke in keyChord.Strokes)
				parts.Add(GetDisplayText(stroke));

			return string.Join(", ", parts);
		}
	}
}
