using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Testing;
using Nickelony.KeyBindings.Testing;
using static Nickelony.KeyBindings.Testing.KeyBindingTestFixtures;

namespace Nickelony.KeyBindings.Tests;

/// <summary>
/// Covers construction from an overrides document: honoring and ignoring host policies, invalid
/// keys, modifiers, and strokes, empty stroke lists, repeated strokes, deduplication, schema
/// versions, unknown ids, and the loading-time throws.
/// </summary>
public partial class KeyBindingServiceTests
{
	[TestMethod]
	public void Constructor_OverrideForHostManagedCommand_IsHonored()
	{
		KeyBindingService<TestCommand> service = CreateService(CreateOverrides(nameof(TestCommand.Redo), ("U", (int)KeyModifierSet.Control)));

		Assert.AreEqual(new KeyCombo(KeyCode.U, KeyModifierSet.Control), service.GetBindings(TestCommand.Redo)[0]);
	}

	/// <summary>
	/// Pins the counterpart of the host-reserved case: an empty override binding list is an explicit
	/// unbind even for a host-managed command, because the host owns those bindings but asked for none.
	/// </summary>
	[TestMethod]
	public void Constructor_EmptyOverrideForHostManagedCommand_UnbindsIt()
	{
		KeyBindingService<TestCommand> service = CreateService(CreateOverrides(nameof(TestCommand.Redo)));

		Assert.IsEmpty(service.GetBindings(TestCommand.Redo));
	}

	[TestMethod]
	[DataRow(true, DisplayName = "With bindings")]
	[DataRow(false, DisplayName = "With empty binding list")]
	public void Constructor_OverrideForHostReservedCommand_IsIgnored(bool hasBindings)
	{
		var logger = new CapturingLogger();
		(string KeyName, int Modifiers)[] bindings = hasBindings ? [("X", (int)KeyModifierSet.Control)] : [];

		KeyBindingService<TestCommand> service = CreateService(CreateOverrides(nameof(TestCommand.Exit), bindings), logger: logger);

		Assert.AreEqual(new KeyCombo(KeyCode.F4, KeyModifierSet.Alt), service.GetBindings(TestCommand.Exit)[0]);
		Assert.AreEqual(1, logger.Entries.Count(entry => entry.EventId.Id == 4000 && entry.Level == LogLevel.Warning));
	}

	[TestMethod]
	[DataRow("62")]
	[DataRow("s")]
	[DataRow("Oem1")]
	[DataRow("OemQuestion")]
	[DataRow("99999")]
	[DataRow("S ")]
	public void Constructor_InvalidKeyName_FallsBackToDefaults(string keyName)
	{
		var logger = new CapturingLogger();
		KeyBindingService<TestCommand> service = CreateService(
			CreateOverrides(nameof(TestCommand.Save), (keyName, (int)KeyModifierSet.Control)),
			logger: logger);

		Assert.AreEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);
		Assert.AreEqual(1, logger.Entries.Count(entry => entry.EventId.Id == 4003 && entry.Level == LogLevel.Warning));
		Assert.AreEqual(1, logger.Entries.Count(entry => entry.EventId.Id == 4001 && entry.Level == LogLevel.Warning));
	}

	[TestMethod]
	public void Constructor_EmptyKeyName_FallsBackToDefaults()
	{
		var logger = new CapturingLogger();
		KeyBindingService<TestCommand> service = CreateService(
			CreateOverrides(nameof(TestCommand.Save), (string.Empty, (int)KeyModifierSet.Control)),
			logger: logger);

		Assert.AreEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);
		Assert.AreEqual(1, logger.Entries.Count(entry => entry.EventId.Id == 4002 && entry.Level == LogLevel.Warning));
	}

	[TestMethod]
	public void Constructor_UnknownModifierBits_FallsBackToDefaults()
	{
		var logger = new CapturingLogger();
		KeyBindingService<TestCommand> service = CreateService(
			CreateOverrides(nameof(TestCommand.Save), ("S", (int)KeyModifierSet.Control | 1024)),
			logger: logger);

		Assert.AreEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);
		Assert.AreEqual(1, logger.Entries.Count(entry => entry.EventId.Id == 4004 && entry.Level == LogLevel.Warning));
	}

	[TestMethod]
	public void Constructor_PartiallyInvalidOverride_KeepsValidBindings()
	{
		var logger = new CapturingLogger();
		KeyBindingService<TestCommand> service = CreateService(
			CreateOverrides(nameof(TestCommand.Save), ("S", (int)KeyModifierSet.Control), ("nope", 0)),
			logger: logger);

		Assert.AreEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);
		Assert.AreEqual(1, logger.Entries.Count(entry => entry.EventId.Id == 4003 && entry.Level == LogLevel.Warning));
		Assert.AreEqual(0, logger.Entries.Count(entry => entry.EventId.Id == 4001 && entry.Level == LogLevel.Warning));
	}

	/// <summary>
	/// Pins the two opposite shapes of an "empty" override entry: a declared binding with no strokes is
	/// not a chord, so the entry falls back to the catalog defaults with a diagnostic, while an entry
	/// whose binding list is empty explicitly unbinds the command.
	/// </summary>
	[TestMethod]
	public void Constructor_BindingWithNoStrokes_LogsAndFallsBackToDefaults()
	{
		var logger = new CapturingLogger();
		var overrides = new KeyBindingOverrides();
		overrides.Entries.Add(new KeyBindingOverrideEntry
		{
			SerializedId = nameof(TestCommand.Save),
			Bindings = [new KeyBindingOverrideBinding()]
		});

		KeyBindingService<TestCommand> service = CreateService(overrides, logger: logger);

		Assert.AreEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);
		Assert.AreEqual(1, logger.Entries.Count(entry => entry.EventId.Id == 4001 && entry.Level == LogLevel.Warning));
	}

	[TestMethod]
	public void Constructor_EmptyBindingListForARemappableCommand_UnbindsIt()
	{
		var logger = new CapturingLogger();
		KeyBindingService<TestCommand> service = CreateService(CreateOverrides(nameof(TestCommand.Save)), logger: logger);

		Assert.IsEmpty(service.GetBindings(TestCommand.Save));

		// An explicitly empty list is a meaningful unbind, not an invalid entry, so nothing is logged.
		Assert.AreEqual(0, logger.Entries.Count(entry => entry.EventId.Id == 4001));
	}

	[TestMethod]
	public void Constructor_DuplicateBindingsInOverride_AreDeduplicated()
	{
		KeyBindingService<TestCommand> service = CreateService(
			CreateOverrides(nameof(TestCommand.Save), ("S", (int)KeyModifierSet.Control), ("S", (int)KeyModifierSet.Control)));

		Assert.HasCount(1, service.GetBindings(TestCommand.Save));
	}

	[TestMethod]
	public void Constructor_OverrideShadowingOtherDefault_LoadsAndShadows()
	{
		KeyBindingService<TestCommand> service = CreateService(CreateOverrides(nameof(TestCommand.Save), ("Z", (int)KeyModifierSet.Control)));

		Assert.IsTrue(service.TryGetCommand(new KeyCombo(KeyCode.Z, KeyModifierSet.Control), null, out TestCommand ownerCommand));
		Assert.AreEqual(TestCommand.Save, ownerCommand);
		Assert.IsEmpty(service.GetBindings(TestCommand.Undo));
	}

	[TestMethod]
	public void Constructor_DuplicateCommandEntries_Throws()
	{
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.Save), ("X", (int)KeyModifierSet.Control));
		overrides.Entries.Add(new KeyBindingOverrideEntry
		{
			SerializedId = nameof(TestCommand.Save),
			Bindings = [SingleStroke("Q", KeyModifierSet.Control)]
		});

		Assert.ThrowsExactly<InvalidOperationException>(() => CreateService(overrides));
	}

	[TestMethod]
	public void Constructor_OverlappingOverrideClaims_Throws()
	{
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.Save), ("X", (int)KeyModifierSet.Control));
		overrides.Entries.Add(new KeyBindingOverrideEntry
		{
			SerializedId = nameof(TestCommand.Undo),
			Bindings = [SingleStroke("X", KeyModifierSet.Control)]
		});

		Assert.ThrowsExactly<InvalidOperationException>(() => CreateService(overrides));
	}

	[TestMethod]
	public void Constructor_UnknownSerializedId_LogsAndPreservesTheEntry()
	{
		var logger = new CapturingLogger();
		KeyBindingOverrides overrides = CreateOverrides("editor.unknown", ("X", (int)KeyModifierSet.Control));
		KeyBindingOverrides? saved = null;
		KeyBindingService<TestCommand> service = CreateService(overrides, snapshot => { saved = snapshot; return true; }, logger: logger);

		Assert.AreEqual(1, logger.Entries.Count(entry => entry.EventId.Id == 4005 && entry.Level == LogLevel.Warning));

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.F5, KeyModifierSet.None)]));

		Assert.IsNotNull(saved);
		Assert.HasCount(2, saved.Entries);
		Assert.IsTrue(saved.Entries.Any(entry => entry.SerializedId == "editor.unknown"));
	}

	[TestMethod]
	public void Constructor_UnsupportedSchemaVersion_Throws()
	{
		Assert.ThrowsExactly<NotSupportedException>(() => CreateService(new KeyBindingOverrides { Version = 0 }));
		Assert.ThrowsExactly<NotSupportedException>(() => CreateService(new KeyBindingOverrides { Version = 1 }));
		Assert.ThrowsExactly<NotSupportedException>(() => CreateService(new KeyBindingOverrides { Version = KeyBindingOverrides.CurrentVersion + 1 }));
		Assert.ThrowsExactly<NotSupportedException>(() => CreateService(new KeyBindingOverrides { Version = KeyBindingOverrides.CurrentVersion - 1 }));
	}

	[TestMethod]
	public void Constructor_CurrentSchemaVersion_Loads()
	{
		var overrides = new KeyBindingOverrides { Version = KeyBindingOverrides.CurrentVersion };

		Assert.IsNotNull(CreateService(overrides));
	}

	[TestMethod]
	public void Constructor_NullOverrideGraph_Throws()
	{
		var nullList = new KeyBindingOverrides { Entries = null! };
		var nullEntry = new KeyBindingOverrides();
		nullEntry.Entries.Add(null!);
		var nullBindings = new KeyBindingOverrides();
		nullBindings.Entries.Add(new KeyBindingOverrideEntry { SerializedId = "Save", Bindings = null! });
		var nullBindingEntry = new KeyBindingOverrides();
		nullBindingEntry.Entries.Add(new KeyBindingOverrideEntry { SerializedId = "Save", Bindings = [null!] });
		var nullSerializedId = new KeyBindingOverrides();
		nullSerializedId.Entries.Add(new KeyBindingOverrideEntry { SerializedId = null!, Bindings = [] });

		Assert.ThrowsExactly<ArgumentException>(() => CreateService(nullList));
		Assert.ThrowsExactly<ArgumentException>(() => CreateService(nullEntry));
		Assert.ThrowsExactly<ArgumentException>(() => CreateService(nullBindings));
		Assert.ThrowsExactly<ArgumentException>(() => CreateService(nullBindingEntry));
		Assert.ThrowsExactly<ArgumentException>(() => CreateService(nullSerializedId));
	}

	[TestMethod]
	public void Constructor_OverridesBindingAChordAndItsPrefix_Throws()
	{
		KeyBindingOverrides overrides = CreateChordOverrides(
			nameof(TestCommand.Save),
			[("K", (int)KeyModifierSet.Control)],
			[("K", (int)KeyModifierSet.Control), ("S", (int)KeyModifierSet.Control)]);

		Assert.ThrowsExactly<InvalidOperationException>(() => CreateChordService(overrides));
	}

	[TestMethod]
	public void Constructor_OverrideWithAnUnparsableStroke_SkipsTheWholeBinding()
	{
		// A chord with a missing stroke is not the declared chord, so the binding is dropped as a whole
		// and the command falls back to its catalog defaults.
		KeyBindingOverrides overrides = CreateChordOverrides(
			nameof(TestCommand.Save),
			[("nope", (int)KeyModifierSet.Control), ("S", (int)KeyModifierSet.Control)]);

		KeyBindingService<TestCommand> service = CreateService(overrides);

		Assert.AreEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);
	}

	/// <summary>
	/// Pins that a binding which repeats a stroke is dropped as a whole: a chord never repeats a stroke,
	/// so the binding is not de-duplicated into a one-stroke chord that would silently rebind the
	/// command.
	/// </summary>
	[TestMethod]
	public void Constructor_RepeatedStrokeInsideOneBinding_RejectsTheWholeBinding()
	{
		KeyBindingOverrides overrides = CreateChordOverrides(
			nameof(TestCommand.Save),
			[("K", (int)KeyModifierSet.Control), ("K", (int)KeyModifierSet.Control)]);

		KeyBindingService<TestCommand> service = CreateService(overrides);

		Assert.HasCount(1, service.GetBindings(TestCommand.Save));
		Assert.AreEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);
	}
}
