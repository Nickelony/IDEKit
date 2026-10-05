using Microsoft.Extensions.Logging;
using Nickelony.KeyBindings.OverrideEntries;
using Nickelony.KeyBindings.Testing;
using Nickelony.Testing;
using static Nickelony.KeyBindings.Testing.KeyBindingTestHelpers;

namespace Nickelony.KeyBindings.Tests;

/// <summary>
/// Covers the state a mutation produces rather than the proposal it was given: an operation that would make another command's binding unreachable,
/// or that would write a document a later load rejects, is reported as an outcome instead of throwing.
/// </summary>
[TestClass]
public sealed class KeyBindingStateScenarioTests
{
	private static KeyStroke Ctrl(KeyCode key) => new(key, KeyModifiers.Control);

	/// <summary>
	/// Builds a catalog in which Undo owns the single stroke <c>Ctrl+K</c> and Find owns <c>Ctrl+F</c>, so a two-stroke chord that starts
	/// with <c>Ctrl+K</c> can be claimed once Undo is rebound away.
	/// </summary>
	private static CommandCatalog<TestCommand> CreatePrefixCatalog()
	{
		return new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.Undo, nameof(TestCommand.Undo), CommandRemappingPolicy.Remappable, Ctrl(KeyCode.K)),
			new CommandDescriptor<TestCommand>(TestCommand.Find, nameof(TestCommand.Find), CommandRemappingPolicy.Remappable, Ctrl(KeyCode.F)),

			new CommandDescriptor<TestCommand>(
				TestCommand.Build,
				nameof(TestCommand.Build),
				CommandRemappingPolicy.Remappable,
				new KeyStroke(KeyCode.F9, KeyModifiers.None)
			),
		]);
	}

	private static KeyChord PrefixChord => new(Ctrl(KeyCode.K), Ctrl(KeyCode.S));

	// ---- The state a mutation produces ----

	[TestMethod]
	public void Reset_RestoringADefaultThatBecomesAPrefix_ReturnsPrefixConflictAndKeepsState()
	{
		KeyBindingOverrides? saved = null;

		KeyBindingService<TestCommand> service = CreateService(
			null,

			snapshot =>
			{
				saved = snapshot;
				return true;
			},

			catalog: CreatePrefixCatalog()
		);

		// Undo leaves Ctrl+K unclaimed, so Find may take the two-stroke chord that starts with it.
		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Apply(TestCommand.Undo, [new KeyStroke(KeyCode.U, KeyModifiers.Control)]));
		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Apply(TestCommand.Find, [PrefixChord]));

		saved = null;

		KeyBindingOutcome outcome = service.Reset(TestCommand.Undo);

		// Restoring Ctrl+K would make Find's longer chord unreachable, so the operation is refused instead of
		// throwing, and it changes no state.
		Assert.AreEqual(KeyBindingOutcome.PrefixConflict, outcome);
		Assert.IsNull(saved);
		Assert.AreEqual(new KeyStroke(KeyCode.U, KeyModifiers.Control), service.GetBindings(TestCommand.Undo)[0]);
		Assert.IsTrue(service.TryGetCommand(PrefixChord, null, out TestCommand owner));
		Assert.AreEqual(TestCommand.Find, owner);
	}

	[TestMethod]
	public void Apply_Replace_WhenTheOwnersRestoredDefaultBecomesAPrefix_ReturnsPrefixConflictAndKeepsState()
	{
		KeyBindingOverrides? saved = null;

		KeyBindingService<TestCommand> service = CreateService(
			null,

			snapshot =>
			{
				saved = snapshot;
				return true;
			},

			catalog: CreatePrefixCatalog()
		);

		// Undo replaces its own default with the chord that starts with it, which leaves Ctrl+K unclaimed.
		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Apply(TestCommand.Undo, [PrefixChord], KeyBindingConflictPolicy.Replace));

		saved = null;

		KeyBindingOutcome outcome = service.Apply(TestCommand.Find, [PrefixChord], KeyBindingConflictPolicy.Replace);

		// The proposal is accepted, but applying it removes Undo's entry and restores Undo's default Ctrl+K
		// as a strict prefix of the very chord Find takes over, so the produced state is refused rather than
		// written. A dry run reports the same outcome.
		Assert.AreEqual(KeyBindingOutcome.PrefixConflict, outcome);
		Assert.AreEqual(KeyBindingOutcome.PrefixConflict, service.Validate(TestCommand.Find, [PrefixChord], KeyBindingConflictPolicy.Replace));
		Assert.IsNull(saved);
		Assert.AreEqual(PrefixChord, service.GetBindings(TestCommand.Undo)[0]);
		Assert.IsTrue(service.TryGetCommand(PrefixChord, null, out TestCommand owner));
		Assert.AreEqual(TestCommand.Undo, owner);
	}

	[TestMethod]
	public void Reset_RestoringAShadowedDefault_ReportsSucceededAndLeavesTheCommandUnbound()
	{
		KeyBindingService<TestCommand> service = CreateService();

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Apply(TestCommand.Undo, [new KeyStroke(KeyCode.U, KeyModifiers.Control)]));
		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Apply(TestCommand.Find, [Ctrl(KeyCode.Z)]));

		// Undo's default is restored but stays shadowed by Find's override, so Undo dispatches nothing while
		// that override exists. Reset removed Undo's own override, so the operation reports success.
		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Reset(TestCommand.Undo));
		Assert.IsEmpty(service.GetBindings(TestCommand.Undo));
		Assert.IsTrue(service.TryGetCommand(Ctrl(KeyCode.Z), null, out TestCommand owner));
		Assert.AreEqual(TestCommand.Find, owner);
	}

	[TestMethod]
	public void Reset_RestoringADefaultAPreservedEntryClaims_ReturnsConflictWithoutWriting()
	{
		var catalog = new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(
				TestCommand.Save,
				nameof(TestCommand.Save),
				CommandRemappingPolicy.Remappable,
				new KeyChord(Ctrl(KeyCode.K), Ctrl(KeyCode.S))
			),

			new CommandDescriptor<TestCommand>(TestCommand.Find, nameof(TestCommand.Find), CommandRemappingPolicy.Remappable, Ctrl(KeyCode.F)),
		]);

		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.Save), ("U", (int)KeyModifiers.Control));

		// The preserved entry claims Ctrl+K, a strict prefix of Save's default. Restoring that default would
		// produce a document this service rejects once the preserved command is cataloged.
		overrides.Entries.Add(
			new KeyBindingOverrideEntry
			{
				SerializedId = "editor.unknown",

				Bindings =
				[
					new KeyBindingOverrideChord { Strokes = [new KeyBindingOverrideStroke { KeyName = "K", Modifiers = (int)KeyModifiers.Control }] },
				],
			}
		);

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

		KeyBindingOutcome outcome = service.Reset(TestCommand.Save);

		Assert.AreEqual(KeyBindingOutcome.Conflict, outcome);
		Assert.IsNull(saved);
		Assert.AreEqual(new KeyStroke(KeyCode.U, KeyModifiers.Control), service.GetBindings(TestCommand.Save)[0]);
	}

	/// <summary>
	/// Pins the <c>ResetAll</c> variant of the restore-collision rule: the preserved entry survives the reset, so restoring a catalog default
	/// that collides with it is refused and nothing is persisted.
	/// </summary>
	[TestMethod]
	public void ResetAll_RestoringADefaultAPreservedEntryClaims_ReturnsConflictWithoutWriting()
	{
		var catalog = new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(
				TestCommand.Save,
				nameof(TestCommand.Save),
				CommandRemappingPolicy.Remappable,
				new KeyChord(Ctrl(KeyCode.K), Ctrl(KeyCode.S))
			),

			new CommandDescriptor<TestCommand>(TestCommand.Find, nameof(TestCommand.Find), CommandRemappingPolicy.Remappable, Ctrl(KeyCode.F)),
		]);

		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.Save), ("U", (int)KeyModifiers.Control));

		overrides.Entries.Add(
			new KeyBindingOverrideEntry
			{
				SerializedId = "editor.unknown",

				Bindings =
				[
					new KeyBindingOverrideChord { Strokes = [new KeyBindingOverrideStroke { KeyName = "K", Modifiers = (int)KeyModifiers.Control }] },
				],
			}
		);

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

		KeyBindingOutcome outcome = service.ResetAll();

		Assert.AreEqual(KeyBindingOutcome.Conflict, outcome);
		Assert.IsNull(saved);
		Assert.AreEqual(new KeyStroke(KeyCode.U, KeyModifiers.Control), service.GetBindings(TestCommand.Save)[0]);
	}

	/// <summary>
	/// Pins the conservative half of the preserved-entry rule: a collision the loaded document already carries does not block an unrelated mutation.
	/// </summary>
	[TestMethod]
	public void Apply_UnrelatedMutation_IsNotBlockedByAnAlreadyLoadedPreservedEntryCollision()
	{
		var catalog = new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(
				TestCommand.Save,
				nameof(TestCommand.Save),
				CommandRemappingPolicy.Remappable,
				new KeyChord(Ctrl(KeyCode.K), Ctrl(KeyCode.S))
			),

			new CommandDescriptor<TestCommand>(TestCommand.Find, nameof(TestCommand.Find), CommandRemappingPolicy.Remappable, Ctrl(KeyCode.F)),
		]);

		// The preserved entry already claims Ctrl+K, a prefix of Save's default, and the document loaded.
		var overrides = new KeyBindingOverrides();

		overrides.Entries.Add(
			new KeyBindingOverrideEntry
			{
				SerializedId = "editor.unknown",

				Bindings =
				[
					new KeyBindingOverrideChord { Strokes = [new KeyBindingOverrideStroke { KeyName = "K", Modifiers = (int)KeyModifiers.Control }] },
				],
			}
		);

		KeyBindingService<TestCommand> service = CreateService(overrides, catalog: catalog);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Apply(TestCommand.Find, [Ctrl(KeyCode.G)]));
		Assert.AreEqual(Ctrl(KeyCode.G), service.GetBindings(TestCommand.Find)[0]);
	}

	// ---- A document cannot win a chord the API refuses ----

	[TestMethod]
	public void Constructor_OverrideShadowingAHostReservedDefault_Throws()
	{
		var catalog = new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.Save, nameof(TestCommand.Save), CommandRemappingPolicy.Remappable, Ctrl(KeyCode.S)),

			new CommandDescriptor<TestCommand>(
				TestCommand.Exit,
				nameof(TestCommand.Exit),
				CommandRemappingPolicy.HostReserved,
				new KeyStroke(KeyCode.F4, KeyModifiers.Alt)
			),
		]);

		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.Save), ("F4", (int)KeyModifiers.Alt));

		// A host-reserved command keeps its chord against a loaded document, exactly as Apply refuses to hand
		// the chord over.
		Assert.ThrowsExactly<InvalidOperationException>(() => CreateService(overrides, catalog: catalog));
	}

	[TestMethod]
	public void Constructor_OverrideShadowingAHostManagedDefault_Throws()
	{
		var catalog = new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.Save, nameof(TestCommand.Save), CommandRemappingPolicy.Remappable, Ctrl(KeyCode.S)),
			new CommandDescriptor<TestCommand>(TestCommand.Redo, nameof(TestCommand.Redo), CommandRemappingPolicy.HostManaged, Ctrl(KeyCode.Y)),
		]);

		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.Save), ("Y", (int)KeyModifiers.Control));

		Assert.ThrowsExactly<InvalidOperationException>(() => CreateService(overrides, catalog: catalog));
	}

	[TestMethod]
	public void Constructor_AlwaysActiveOverrideDisplacingAScopedDefault_Throws()
	{
		var catalog = new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.Save, nameof(TestCommand.Save), CommandRemappingPolicy.Remappable, Ctrl(KeyCode.S)),

			new CommandDescriptor<TestCommand>(TestCommand.Build, nameof(TestCommand.Build), CommandRemappingPolicy.Remappable, Ctrl(KeyCode.K))
			{
				Context = GeometryContext,
			},
		]);

		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.Save), ("K", (int)KeyModifiers.Control));

		// The always-active override would leave Build unreachable in its own context, which is the proposal
		// Apply rejects under both conflict policies.
		Assert.ThrowsExactly<InvalidOperationException>(() => CreateService(overrides, catalog: catalog));
	}

	[TestMethod]
	public void Constructor_OverrideShadowingARemappableDefault_LoadsAndShadows()
	{
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.Save), ("Z", (int)KeyModifiers.Control));

		// The shadow is the documented way Replace takes a chord over, so a remappable default stays
		// takeable by a document.
		KeyBindingService<TestCommand> service = CreateService(overrides);

		Assert.IsTrue(service.TryGetCommand(Ctrl(KeyCode.Z), null, out TestCommand owner));
		Assert.AreEqual(TestCommand.Save, owner);
		Assert.IsEmpty(service.GetBindings(TestCommand.Undo));
	}

	// ---- Diagnostics ----

	[TestMethod]
	public void Apply_MutationOfAnotherCommand_DoesNotRepeatTheLoadDiagnostics()
	{
		var logger = new RecordingLogger();

		KeyBindingService<TestCommand> service = CreateService(
			CreateOverrides(nameof(TestCommand.Save), ("nope", (int)KeyModifiers.Control)),
			logger: logger
		);

		// The invalid key name is reported with the command and the name, and the entry that none of its bindings survived falls back to the defaults.
		WarningLogTestHelpers.AssertSingleWarning(logger, 4003, nameof(TestCommand.Save), "nope");
		WarningLogTestHelpers.AssertSingleWarning(logger, 4001, nameof(TestCommand.Save));

		string[] warningsAtLoad = WarningMessages(logger);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Apply(TestCommand.Undo, [new KeyStroke(KeyCode.U, KeyModifiers.Control)]));

		// The invalid entry was reported when it was loaded; rebuilding the maps for a mutation of another
		// command must not repeat its diagnostics.
		Assert.AreSequenceEqual(warningsAtLoad, WarningMessages(logger));
	}

	private static string[] WarningMessages(RecordingLogger logger) =>
		[.. logger.Entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message)];
}
