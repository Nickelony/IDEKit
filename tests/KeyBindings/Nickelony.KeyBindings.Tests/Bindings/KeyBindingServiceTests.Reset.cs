using Nickelony.KeyBindings.OverrideEntries;
using Nickelony.KeyBindings.Testing;
using static Nickelony.KeyBindings.Testing.KeyBindingTestHelpers;

namespace Nickelony.KeyBindings.Tests;

/// <summary>
/// Covers <c>Reset</c> and <c>ResetAll</c>: restoring defaults, preserved-entry handling, persistence failures, and the state a reset produces.
/// </summary>
public partial class KeyBindingServiceTests
{
	[TestMethod]
	public void Reset_AfterMaskedDefaultIsRestored_KeepsPersistedStateAndRuntimeConsistent()
	{
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.Undo), ("U", (int)KeyModifiers.Control));
		KeyBindingOverrides? persisted = null;

		KeyBindingService<TestCommand> service = CreateService(
			overrides,

			snapshot =>
			{
				persisted = snapshot;
				return true;
			}
		);

		// The override masks Ctrl+Z as Undo's default, so Save may claim the stroke.
		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Apply(TestCommand.Save, [new KeyStroke(KeyCode.Z, KeyModifiers.Control)]));

		KeyBindingOutcome outcome = service.Reset(TestCommand.Undo);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, outcome);
		Assert.IsTrue(service.TryGetCommand(new KeyStroke(KeyCode.Z, KeyModifiers.Control), null, out TestCommand ownerCommand));
		Assert.AreEqual(TestCommand.Save, ownerCommand);
		Assert.IsEmpty(service.GetBindings(TestCommand.Undo));

		Assert.IsNotNull(persisted);
		Assert.HasCount(1, persisted.Entries);
		Assert.AreEqual(nameof(TestCommand.Save), persisted.Entries[0].SerializedId);

		// The persisted state must load into a fresh service without a collision.
		KeyBindingService<TestCommand> reloaded = CreateService(persisted.Clone(), catalog: CreateCatalog());

		Assert.IsTrue(reloaded.TryGetCommand(new KeyStroke(KeyCode.Z, KeyModifiers.Control), null, out ownerCommand));
		Assert.AreEqual(TestCommand.Save, ownerCommand);
	}

	[TestMethod]
	public void Reset_AfterReplaceShrankOwnerOverride_KeepsStateConsistent()
	{
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.Undo), ("Z", (int)KeyModifiers.Control), ("U", (int)KeyModifiers.Control));

		KeyBindingOverrides? persisted = null;

		KeyBindingService<TestCommand> service = CreateReplacementService(
			overrides,

			snapshot =>
			{
				persisted = snapshot;
				return true;
			}
		);

		Assert.AreEqual(
			KeyBindingOutcome.Succeeded,
			service.Apply(TestCommand.Save, [new KeyStroke(KeyCode.Z, KeyModifiers.Control)], KeyBindingConflictPolicy.Replace)
		);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Reset(TestCommand.Undo));

		Assert.IsTrue(service.TryGetCommand(new KeyStroke(KeyCode.Z, KeyModifiers.Control), null, out TestCommand ownerCommand));
		Assert.AreEqual(TestCommand.Save, ownerCommand);
		Assert.AreEqual(new KeyStroke(KeyCode.U, KeyModifiers.Control), service.GetBindings(TestCommand.Undo)[0]);

		Assert.IsNotNull(persisted);

		KeyBindingService<TestCommand> reloaded = CreateService(persisted.Clone(), catalog: CreateReplacementCatalog());

		Assert.IsTrue(reloaded.TryGetCommand(new KeyStroke(KeyCode.Z, KeyModifiers.Control), null, out ownerCommand));
		Assert.AreEqual(TestCommand.Save, ownerCommand);
	}

	[TestMethod]
	public void Reset_RemovesOverrideAndRestoresDefault()
	{
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.Save), ("X", (int)KeyModifiers.Control));
		KeyBindingOverrides? saved = null;
		int fired = 0;

		KeyBindingService<TestCommand> service = CreateService(
			overrides,

			snapshot =>
			{
				saved = snapshot;
				return true;
			}
		);

		service.BindingsChanged += (_, _) => fired++;

		Assert.AreEqual(new KeyStroke(KeyCode.X, KeyModifiers.Control), service.GetBindings(TestCommand.Save)[0]);

		KeyBindingOutcome outcome = service.Reset(TestCommand.Save);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, outcome);
		Assert.AreEqual(new KeyStroke(KeyCode.S, KeyModifiers.Control), service.GetBindings(TestCommand.Save)[0]);
		Assert.AreEqual(1, fired);
		Assert.IsNotNull(saved);
		Assert.IsEmpty(saved.Entries);

		// The store document is owned by the host and is never mutated by the service.
		Assert.HasCount(1, overrides.Entries);
	}

	[TestMethod]
	public void Reset_RemovesShadowingOverrideAndRestoresShadowedDefault()
	{
		KeyBindingService<TestCommand> service = CreateService(CreateOverrides(nameof(TestCommand.Save), ("Z", (int)KeyModifiers.Control)));

		Assert.IsEmpty(service.GetBindings(TestCommand.Undo));

		KeyBindingOutcome outcome = service.Reset(TestCommand.Save);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, outcome);
		Assert.IsTrue(service.TryGetCommand(new KeyStroke(KeyCode.Z, KeyModifiers.Control), null, out TestCommand ownerCommand));
		Assert.AreEqual(TestCommand.Undo, ownerCommand);
		Assert.AreEqual(new KeyStroke(KeyCode.Z, KeyModifiers.Control), service.GetBindings(TestCommand.Undo)[0]);
	}

	[TestMethod]
	public void Reset_ARestoredDefaultOfAnotherCommand_ReportsTheChangeAsReachingOtherCommands()
	{
		KeyBindingService<TestCommand> service = CreateService(CreateOverrides(nameof(TestCommand.Save), ("Z", (int)KeyModifiers.Control)));
		BindingsChangedEventArgs<TestCommand>? last = null;

		service.BindingsChanged += (_, args) => last = args;

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Reset(TestCommand.Save));

		// Undo regains the Ctrl+Z that Save's override shadowed, so the event cannot name only Save.
		Assert.IsNotNull(last);
		Assert.IsTrue(last.AffectsAllCommands);
		Assert.AreEqual(TestCommand.None, last.Command);
	}

	[TestMethod]
	public void Reset_NoOtherCommandChanges_NamesOnlyTheResetCommand()
	{
		KeyBindingService<TestCommand> service = CreateService(CreateOverrides(nameof(TestCommand.Save), ("X", (int)KeyModifiers.Control)));
		BindingsChangedEventArgs<TestCommand>? last = null;

		service.BindingsChanged += (_, args) => last = args;

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Reset(TestCommand.Save));

		Assert.IsNotNull(last);
		Assert.IsFalse(last.AffectsAllCommands);
		Assert.AreEqual(TestCommand.Save, last.Command);
	}

	[TestMethod]
	public void Reset_PreservesOtherCommandOverrides()
	{
		var overrides = new KeyBindingOverrides();

		overrides.Entries.Add(
			new KeyBindingOverrideEntry { SerializedId = nameof(TestCommand.Save), Bindings = [SingleStroke("X", KeyModifiers.Control)] }
		);

		overrides.Entries.Add(
			new KeyBindingOverrideEntry { SerializedId = nameof(TestCommand.Undo), Bindings = [SingleStroke("U", KeyModifiers.Control)] }
		);

		KeyBindingOverrides? saved = null;

		KeyBindingService<TestCommand> service = CreateService(
			overrides,

			snapshot =>
			{
				saved = snapshot;
				return true;
			}
		);

		KeyBindingOutcome outcome = service.Reset(TestCommand.Save);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, outcome);
		Assert.AreEqual(new KeyStroke(KeyCode.S, KeyModifiers.Control), service.GetBindings(TestCommand.Save)[0]);
		Assert.AreEqual(new KeyStroke(KeyCode.U, KeyModifiers.Control), service.GetBindings(TestCommand.Undo)[0]);
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

		KeyBindingService<TestCommand> service = CreateService(
			null,

			_ =>
			{
				callbackCount++;
				return true;
			}
		);

		service.BindingsChanged += (_, _) => fired++;

		KeyBindingOutcome outcome = service.Reset(TestCommand.Save);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, outcome);
		Assert.AreEqual(0, callbackCount);
		Assert.AreEqual(0, fired);
	}

	[TestMethod]
	public void Reset_UncatalogedCommand_ReturnsUnknownCommandWithoutTouchingOverrides()
	{
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.None), ("X", (int)KeyModifiers.Control));
		int callbackCount = 0;

		KeyBindingService<TestCommand> service = CreateService(
			overrides,

			_ =>
			{
				callbackCount++;
				return true;
			}
		);

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

		overrides.Entries.Add(
			new KeyBindingOverrideEntry { SerializedId = nameof(TestCommand.Save), Bindings = [SingleStroke("X", KeyModifiers.Control)] }
		);

		overrides.Entries.Add(
			new KeyBindingOverrideEntry { SerializedId = nameof(TestCommand.Undo), Bindings = [SingleStroke("U", KeyModifiers.Control)] }
		);

		KeyBindingOverrides? saved = null;
		int fired = 0;

		KeyBindingService<TestCommand> service = CreateService(
			overrides,

			snapshot =>
			{
				saved = snapshot;
				return true;
			}
		);

		service.BindingsChanged += (_, _) => fired++;

		KeyBindingOutcome outcome = service.ResetAll();

		Assert.AreEqual(KeyBindingOutcome.Succeeded, outcome);
		Assert.AreEqual(1, fired);
		Assert.AreEqual(new KeyStroke(KeyCode.S, KeyModifiers.Control), service.GetBindings(TestCommand.Save)[0]);
		Assert.AreEqual(new KeyStroke(KeyCode.Z, KeyModifiers.Control), service.GetBindings(TestCommand.Undo)[0]);
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

		KeyBindingService<TestCommand> service = CreateService(
			null,

			_ =>
			{
				callbackCount++;
				return true;
			}
		);

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

		overrides.Entries.Add(
			new KeyBindingOverrideEntry { SerializedId = nameof(TestCommand.Save), Bindings = [SingleStroke("X", KeyModifiers.Control)] }
		);

		overrides.Entries.Add(
			new KeyBindingOverrideEntry { SerializedId = nameof(TestCommand.None), Bindings = [SingleStroke("U", KeyModifiers.Control)] }
		);

		KeyBindingOverrides? saved = null;
		int fired = 0;

		KeyBindingService<TestCommand> service = CreateService(
			overrides,

			snapshot =>
			{
				saved = snapshot;
				return true;
			}
		);

		service.BindingsChanged += (_, _) => fired++;

		KeyBindingOutcome outcome = service.ResetAll();

		Assert.AreEqual(KeyBindingOutcome.Succeeded, outcome);
		Assert.AreEqual(1, fired);
		Assert.AreEqual(new KeyStroke(KeyCode.S, KeyModifiers.Control), service.GetBindings(TestCommand.Save)[0]);

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
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.None), ("X", (int)KeyModifiers.Control));

		int callbackCount = 0;
		int fired = 0;

		KeyBindingService<TestCommand> service = CreateService(
			overrides,

			_ =>
			{
				callbackCount++;
				return true;
			}
		);

		service.BindingsChanged += (_, _) => fired++;

		KeyBindingOutcome outcome = service.ResetAll();

		Assert.AreEqual(KeyBindingOutcome.Succeeded, outcome);
		Assert.AreEqual(0, callbackCount);
		Assert.AreEqual(0, fired);
	}

	[TestMethod]
	public void ResetAll_ACatalogedEntryAfterPreservedEntries_ClearsItAndKeepsThePreservedOnes()
	{
		var overrides = new KeyBindingOverrides();

		overrides.Entries.Add(
			new KeyBindingOverrideEntry { SerializedId = nameof(TestCommand.None), Bindings = [SingleStroke("U", KeyModifiers.Control)] }
		);

		overrides.Entries.Add(new KeyBindingOverrideEntry { SerializedId = "Vendor.Other", Bindings = [SingleStroke("I", KeyModifiers.Control)] });

		overrides.Entries.Add(
			new KeyBindingOverrideEntry { SerializedId = nameof(TestCommand.Save), Bindings = [SingleStroke("X", KeyModifiers.Control)] }
		);

		KeyBindingOverrides? saved = null;
		int fired = 0;

		KeyBindingService<TestCommand> service = CreateService(
			overrides,

			snapshot =>
			{
				saved = snapshot;
				return true;
			}
		);

		service.BindingsChanged += (_, _) => fired++;

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.ResetAll());

		Assert.AreEqual(1, fired);
		Assert.IsNotNull(saved);
		Assert.AreSequenceEqual([nameof(TestCommand.None), "Vendor.Other"], saved.Entries.Select(entry => entry.SerializedId));
		Assert.AreEqual(new KeyStroke(KeyCode.S, KeyModifiers.Control), service.GetBindings(TestCommand.Save)[0]);
	}

	[TestMethod]
	public void Reset_DifferingSerializedId_RemovesOverride()
	{
		var catalog = new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(
				TestCommand.Save,
				"editor.save",
				CommandRemappingPolicy.Remappable,
				new KeyStroke(KeyCode.S, KeyModifiers.Control)
			),
		]);

		var overrides = new KeyBindingOverrides();

		overrides.Entries.Add(new KeyBindingOverrideEntry { SerializedId = "editor.save", Bindings = [SingleStroke("X", KeyModifiers.Control)] });

		KeyBindingOverrides? saved = null;

		KeyBindingService<TestCommand> service = CreateService(
			overrides,

			snapshot =>
			{
				saved = snapshot;
				return true;
			},

			catalog: catalog
		);

		Assert.AreEqual(new KeyStroke(KeyCode.X, KeyModifiers.Control), service.GetBindings(TestCommand.Save)[0]);

		KeyBindingOutcome outcome = service.Reset(TestCommand.Save);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, outcome);
		Assert.AreEqual(new KeyStroke(KeyCode.S, KeyModifiers.Control), service.GetBindings(TestCommand.Save)[0]);
		Assert.IsNotNull(saved);
		Assert.IsEmpty(saved.Entries);
	}

	[TestMethod]
	public void Reset_PersistenceFailure_ReturnsPersistenceFailedAndKeepsState()
	{
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.Save), ("X", (int)KeyModifiers.Control));
		int fired = 0;
		KeyBindingService<TestCommand> service = CreateService(overrides, _ => false);

		service.BindingsChanged += (_, _) => fired++;

		KeyBindingOutcome outcome = service.Reset(TestCommand.Save);

		Assert.AreEqual(KeyBindingOutcome.PersistenceFailed, outcome);
		Assert.AreEqual(0, fired);
		Assert.AreEqual(new KeyStroke(KeyCode.X, KeyModifiers.Control), service.GetBindings(TestCommand.Save)[0]);
		Assert.HasCount(1, overrides.Entries);
	}

	[TestMethod]
	public void ResetAll_PersistenceFailure_ReturnsPersistenceFailedAndKeepsState()
	{
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.Save), ("X", (int)KeyModifiers.Control));
		int fired = 0;
		KeyBindingService<TestCommand> service = CreateService(overrides, _ => false);

		service.BindingsChanged += (_, _) => fired++;

		KeyBindingOutcome outcome = service.ResetAll();

		Assert.AreEqual(KeyBindingOutcome.PersistenceFailed, outcome);
		Assert.AreEqual(0, fired);
		Assert.AreEqual(new KeyStroke(KeyCode.X, KeyModifiers.Control), service.GetBindings(TestCommand.Save)[0]);
		Assert.HasCount(1, overrides.Entries);
	}

	[TestMethod]
	public void Reset_RestoresTheChordDefault()
	{
		KeyBindingService<TestCommand> service = CreateChordService(CreateOverrides(nameof(TestCommand.SaveAll), ("X", (int)KeyModifiers.Control)));

		Assert.AreEqual(new KeyStroke(KeyCode.X, KeyModifiers.Control), service.GetBindings(TestCommand.SaveAll)[0]);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Reset(TestCommand.SaveAll));

		Assert.AreEqual(
			new KeyChord(new KeyStroke(KeyCode.K, KeyModifiers.Control), new KeyStroke(KeyCode.S, KeyModifiers.Control)),
			service.GetBindings(TestCommand.SaveAll)[0]
		);
	}
}
