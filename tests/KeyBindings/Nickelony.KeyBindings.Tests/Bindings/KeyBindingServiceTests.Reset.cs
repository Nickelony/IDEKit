using Nickelony.KeyBindings.Testing;
using static Nickelony.KeyBindings.Testing.KeyBindingTestFixtures;

namespace Nickelony.KeyBindings.Tests;

/// <summary>
/// Covers <c>Reset</c> and <c>ResetAll</c>: restoring defaults and shadowed defaults, preserving other
/// and uncataloged entries, preserved-entry handling, persistence failures, chord defaults, and the
/// state-consistency regressions a reset can produce.
/// </summary>
public partial class KeyBindingServiceTests
{
	[TestMethod]
	public void Reset_AfterMaskedDefaultIsRestored_KeepsPersistedStateAndRuntimeConsistent()
	{
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.Undo), ("U", (int)KeyModifierSet.Control));
		KeyBindingOverrides? persisted = null;
		KeyBindingService<TestCommand> service = CreateService(overrides, snapshot => { persisted = snapshot; return true; });

		// The override masks Ctrl+Z as Undo's default, so Save may claim the combo.
		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.Z, KeyModifierSet.Control)]));

		KeyBindingOutcome outcome = service.Reset(TestCommand.Undo);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, outcome);
		Assert.IsTrue(service.TryGetCommand(new KeyCombo(KeyCode.Z, KeyModifierSet.Control), null, out TestCommand ownerCommand));
		Assert.AreEqual(TestCommand.Save, ownerCommand);
		Assert.IsEmpty(service.GetBindings(TestCommand.Undo));

		Assert.IsNotNull(persisted);
		Assert.HasCount(1, persisted.Entries);
		Assert.AreEqual(nameof(TestCommand.Save), persisted.Entries[0].SerializedId);

		// The persisted state must load into a fresh service without a collision.
		KeyBindingService<TestCommand> reloaded = CreateService(persisted.Clone(), catalog: CreateCatalog());
		Assert.IsTrue(reloaded.TryGetCommand(new KeyCombo(KeyCode.Z, KeyModifierSet.Control), null, out ownerCommand));
		Assert.AreEqual(TestCommand.Save, ownerCommand);
	}

	[TestMethod]
	public void Reset_AfterReplaceShrankOwnerOverride_KeepsStateConsistent()
	{
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.Undo), ("Z", (int)KeyModifierSet.Control), ("U", (int)KeyModifierSet.Control));
		KeyBindingOverrides? persisted = null;
		KeyBindingService<TestCommand> service = CreateReplacementService(overrides, snapshot => { persisted = snapshot; return true; });

		Assert.AreEqual(KeyBindingOutcome.Succeeded,
			service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.Z, KeyModifierSet.Control)], KeyBindingConflictPolicy.Replace));
		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Reset(TestCommand.Undo));

		Assert.IsTrue(service.TryGetCommand(new KeyCombo(KeyCode.Z, KeyModifierSet.Control), null, out TestCommand ownerCommand));
		Assert.AreEqual(TestCommand.Save, ownerCommand);
		Assert.AreEqual(new KeyCombo(KeyCode.U, KeyModifierSet.Control), service.GetBindings(TestCommand.Undo)[0]);

		Assert.IsNotNull(persisted);
		KeyBindingService<TestCommand> reloaded = CreateService(persisted.Clone(), catalog: CreateReplacementCatalog());
		Assert.IsTrue(reloaded.TryGetCommand(new KeyCombo(KeyCode.Z, KeyModifierSet.Control), null, out ownerCommand));
		Assert.AreEqual(TestCommand.Save, ownerCommand);
	}

	[TestMethod]
	public void Reset_RemovesOverrideAndRestoresDefault()
	{
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.Save), ("X", (int)KeyModifierSet.Control));
		KeyBindingOverrides? saved = null;
		int fired = 0;
		KeyBindingService<TestCommand> service = CreateService(overrides, snapshot => { saved = snapshot; return true; });
		service.BindingsChanged += (_, _) => fired++;

		Assert.AreEqual(new KeyCombo(KeyCode.X, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);

		KeyBindingOutcome outcome = service.Reset(TestCommand.Save);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, outcome);
		Assert.AreEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);
		Assert.AreEqual(1, fired);
		Assert.IsNotNull(saved);
		Assert.IsEmpty(saved.Entries);

		// The store document is owned by the host and is never mutated by the service.
		Assert.HasCount(1, overrides.Entries);
	}

	[TestMethod]
	public void Reset_RemovesShadowingOverrideAndRestoresShadowedDefault()
	{
		KeyBindingService<TestCommand> service = CreateService(CreateOverrides(nameof(TestCommand.Save), ("Z", (int)KeyModifierSet.Control)));

		Assert.IsEmpty(service.GetBindings(TestCommand.Undo));

		KeyBindingOutcome outcome = service.Reset(TestCommand.Save);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, outcome);
		Assert.IsTrue(service.TryGetCommand(new KeyCombo(KeyCode.Z, KeyModifierSet.Control), null, out TestCommand ownerCommand));
		Assert.AreEqual(TestCommand.Undo, ownerCommand);
		Assert.AreEqual(new KeyCombo(KeyCode.Z, KeyModifierSet.Control), service.GetBindings(TestCommand.Undo)[0]);
	}

	[TestMethod]
	public void Reset_PreservesOtherCommandOverrides()
	{
		var overrides = new KeyBindingOverrides();
		overrides.Entries.Add(new KeyBindingOverrideEntry
		{
			SerializedId = nameof(TestCommand.Save),
			Bindings = [SingleStroke("X", KeyModifierSet.Control)]
		});
		overrides.Entries.Add(new KeyBindingOverrideEntry
		{
			SerializedId = nameof(TestCommand.Undo),
			Bindings = [SingleStroke("U", KeyModifierSet.Control)]
		});

		KeyBindingOverrides? saved = null;
		KeyBindingService<TestCommand> service = CreateService(overrides, snapshot => { saved = snapshot; return true; });

		KeyBindingOutcome outcome = service.Reset(TestCommand.Save);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, outcome);
		Assert.AreEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);
		Assert.AreEqual(new KeyCombo(KeyCode.U, KeyModifierSet.Control), service.GetBindings(TestCommand.Undo)[0]);
		Assert.IsNotNull(saved);
		Assert.HasCount(1, saved.Entries);
		Assert.AreEqual(nameof(TestCommand.Undo), saved.Entries[0].SerializedId);
		Assert.AreEqual("U", saved.Entries[0].Bindings[0].Strokes[0].KeyName);

		// The store document is owned by the host and is never mutated by the service.
		Assert.HasCount(2, overrides.Entries);
	}

	[TestMethod]
	public void Reset_CommandWithoutOverride_IsNoOp()
	{
		int callbackCount = 0;
		int fired = 0;
		KeyBindingService<TestCommand> service = CreateService(null, _ => { callbackCount++; return true; });
		service.BindingsChanged += (_, _) => fired++;

		KeyBindingOutcome outcome = service.Reset(TestCommand.Save);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, outcome);
		Assert.AreEqual(0, callbackCount);
		Assert.AreEqual(0, fired);
	}

	[TestMethod]
	public void Reset_UncatalogedCommand_ReturnsUnknownCommandWithoutTouchingOverrides()
	{
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.None), ("X", (int)KeyModifierSet.Control));
		int callbackCount = 0;
		KeyBindingService<TestCommand> service = CreateService(overrides, _ => { callbackCount++; return true; });

		KeyBindingOutcome outcome = service.Reset(TestCommand.None);

		Assert.AreEqual(KeyBindingOutcome.UnknownCommand, outcome);
		Assert.AreEqual(0, callbackCount);
		Assert.HasCount(1, overrides.Entries);
		Assert.AreEqual(nameof(TestCommand.None), overrides.Entries[0].SerializedId);
	}

	[TestMethod]
	public void ResetAll_RemovesEverythingAndKeepsVersion()
	{
		var overrides = new KeyBindingOverrides { Version = KeyBindingOverrides.CurrentVersion };
		overrides.Entries.Add(new KeyBindingOverrideEntry
		{
			SerializedId = nameof(TestCommand.Save),
			Bindings = [SingleStroke("X", KeyModifierSet.Control)]
		});
		overrides.Entries.Add(new KeyBindingOverrideEntry
		{
			SerializedId = nameof(TestCommand.Undo),
			Bindings = [SingleStroke("U", KeyModifierSet.Control)]
		});

		KeyBindingOverrides? saved = null;
		int fired = 0;
		KeyBindingService<TestCommand> service = CreateService(overrides, snapshot => { saved = snapshot; return true; });
		service.BindingsChanged += (_, _) => fired++;

		KeyBindingOutcome outcome = service.ResetAll();

		Assert.AreEqual(KeyBindingOutcome.Succeeded, outcome);
		Assert.AreEqual(1, fired);
		Assert.AreEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);
		Assert.AreEqual(new KeyCombo(KeyCode.Z, KeyModifierSet.Control), service.GetBindings(TestCommand.Undo)[0]);
		Assert.IsNotNull(saved);
		Assert.IsEmpty(saved.Entries);
		Assert.AreEqual(KeyBindingOverrides.CurrentVersion, saved.Version);

		// The store document is owned by the host and is never mutated by the service.
		Assert.HasCount(2, overrides.Entries);
		Assert.AreEqual(KeyBindingOverrides.CurrentVersion, overrides.Version);
	}

	[TestMethod]
	public void ResetAll_WithoutOverrides_IsNoOp()
	{
		int callbackCount = 0;
		int fired = 0;
		KeyBindingService<TestCommand> service = CreateService(null, _ => { callbackCount++; return true; });
		service.BindingsChanged += (_, _) => fired++;

		KeyBindingOutcome outcome = service.ResetAll();

		Assert.AreEqual(KeyBindingOutcome.Succeeded, outcome);
		Assert.AreEqual(0, callbackCount);
		Assert.AreEqual(0, fired);
	}

	[TestMethod]
	public void ResetAll_PreservesUncatalogedEntries()
	{
		var overrides = new KeyBindingOverrides();
		overrides.Entries.Add(new KeyBindingOverrideEntry
		{
			SerializedId = nameof(TestCommand.Save),
			Bindings = [SingleStroke("X", KeyModifierSet.Control)]
		});
		overrides.Entries.Add(new KeyBindingOverrideEntry
		{
			SerializedId = nameof(TestCommand.None),
			Bindings = [SingleStroke("U", KeyModifierSet.Control)]
		});

		KeyBindingOverrides? saved = null;
		int fired = 0;
		KeyBindingService<TestCommand> service = CreateService(overrides, snapshot => { saved = snapshot; return true; });
		service.BindingsChanged += (_, _) => fired++;

		KeyBindingOutcome outcome = service.ResetAll();

		Assert.AreEqual(KeyBindingOutcome.Succeeded, outcome);
		Assert.AreEqual(1, fired);
		Assert.AreEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);

		// Only the cataloged override is cleared; the entry for the uncataloged command is preserved in the
		// written snapshot, matching the documented preservation guarantee.
		Assert.IsNotNull(saved);
		Assert.HasCount(1, saved.Entries);
		Assert.AreEqual(nameof(TestCommand.None), saved.Entries[0].SerializedId);
		Assert.AreEqual("U", saved.Entries[0].Bindings[0].Strokes[0].KeyName);

		// The store document is owned by the host and is never mutated by the service.
		Assert.HasCount(2, overrides.Entries);
	}

	[TestMethod]
	public void ResetAll_WithOnlyPreservedEntries_IsNoOp()
	{
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.None), ("X", (int)KeyModifierSet.Control));
		int callbackCount = 0;
		int fired = 0;
		KeyBindingService<TestCommand> service = CreateService(overrides, _ => { callbackCount++; return true; });
		service.BindingsChanged += (_, _) => fired++;

		KeyBindingOutcome outcome = service.ResetAll();

		Assert.AreEqual(KeyBindingOutcome.Succeeded, outcome);
		Assert.AreEqual(0, callbackCount);
		Assert.AreEqual(0, fired);
	}

	[TestMethod]
	public void Reset_DifferingSerializedId_RemovesOverride()
	{
		var catalog = new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.Save, "editor.save", CommandRemappingPolicy.Remappable,
				new KeyCombo(KeyCode.S, KeyModifierSet.Control))
		]);
		var overrides = new KeyBindingOverrides();
		overrides.Entries.Add(new KeyBindingOverrideEntry
		{
			SerializedId = "editor.save",
			Bindings = [SingleStroke("X", KeyModifierSet.Control)]
		});
		KeyBindingOverrides? saved = null;
		KeyBindingService<TestCommand> service = CreateService(overrides, snapshot => { saved = snapshot; return true; }, catalog: catalog);

		Assert.AreEqual(new KeyCombo(KeyCode.X, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);

		KeyBindingOutcome outcome = service.Reset(TestCommand.Save);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, outcome);
		Assert.AreEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);
		Assert.IsNotNull(saved);
		Assert.IsEmpty(saved.Entries);
	}

	[TestMethod]
	public void Reset_PersistenceFailure_ReturnsPersistenceFailedAndKeepsState()
	{
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.Save), ("X", (int)KeyModifierSet.Control));
		int fired = 0;
		KeyBindingService<TestCommand> service = CreateService(overrides, _ => false);
		service.BindingsChanged += (_, _) => fired++;

		KeyBindingOutcome outcome = service.Reset(TestCommand.Save);

		Assert.AreEqual(KeyBindingOutcome.PersistenceFailed, outcome);
		Assert.AreEqual(0, fired);
		Assert.AreEqual(new KeyCombo(KeyCode.X, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);
		Assert.HasCount(1, overrides.Entries);
	}

	[TestMethod]
	public void ResetAll_PersistenceFailure_ReturnsPersistenceFailedAndKeepsState()
	{
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.Save), ("X", (int)KeyModifierSet.Control));
		int fired = 0;
		KeyBindingService<TestCommand> service = CreateService(overrides, _ => false);
		service.BindingsChanged += (_, _) => fired++;

		KeyBindingOutcome outcome = service.ResetAll();

		Assert.AreEqual(KeyBindingOutcome.PersistenceFailed, outcome);
		Assert.AreEqual(0, fired);
		Assert.AreEqual(new KeyCombo(KeyCode.X, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);
		Assert.HasCount(1, overrides.Entries);
	}

	[TestMethod]
	public void Reset_RestoresTheChordDefault()
	{
		KeyBindingService<TestCommand> service = CreateChordService(
			CreateOverrides(nameof(TestCommand.SaveAll), ("X", (int)KeyModifierSet.Control)));

		Assert.AreEqual(new KeyCombo(KeyCode.X, KeyModifierSet.Control), service.GetBindings(TestCommand.SaveAll)[0]);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Reset(TestCommand.SaveAll));

		Assert.AreEqual(
			new KeyChord(new KeyCombo(KeyCode.K, KeyModifierSet.Control), new KeyCombo(KeyCode.S, KeyModifierSet.Control)),
			service.GetBindings(TestCommand.SaveAll)[0]);
	}
}
