using Microsoft.Extensions.Logging;

namespace Nickelony.KeyBindings.Testing;

/// <summary>
/// Command identifiers used by the key binding test suites.
/// </summary>
internal enum TestCommand
{
	None,

	NewFile,
	Save,
	SaveAll,
	Build,
	Exit,
	Undo,
	Redo,
	Find,
	GoToDefinition
}

/// <summary>
/// In-memory overrides store that records the snapshots it is asked to persist.
/// </summary>
internal sealed class TestOverridesStore : IKeyBindingOverridesStore
{
	private readonly KeyBindingOverrides _loaded;
	private readonly Func<KeyBindingOverrides, bool> _save;

	internal TestOverridesStore(KeyBindingOverrides? overrides = null, Func<KeyBindingOverrides, bool>? save = null)
	{
		_loaded = overrides ?? new KeyBindingOverrides();
		_save = save ?? (_ => true);
	}

	/// <summary>
	/// Gets the number of snapshots the store was asked to persist.
	/// </summary>
	internal int SaveCount { get; private set; }

	public KeyBindingOverrides Load() => _loaded;

	public bool Save(KeyBindingOverrides snapshot)
	{
		SaveCount++;
		return _save(snapshot);
	}
}

/// <summary>
/// Builds the catalogs, overrides, services, and dispatchers shared by the key binding test suites.
/// </summary>
internal static class KeyBindingTestFixtures
{
	/// <summary>The context token the fixtures scope geometry-mode commands to.</summary>
	internal const string GeometryContext = "geometry";

	/// <summary>The context token the fixtures scope texture-mode commands to.</summary>
	internal const string TextureContext = "texture";

	internal static CommandCatalog<TestCommand> CreateCatalog()
	{
		return new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.NewFile, nameof(TestCommand.NewFile), CommandRemappingPolicy.Remappable,
				new KeyCombo(KeyCode.N, KeyModifierSet.Control)),
			new CommandDescriptor<TestCommand>(TestCommand.Save, nameof(TestCommand.Save), CommandRemappingPolicy.Remappable,
				new KeyCombo(KeyCode.S, KeyModifierSet.Control)),
			new CommandDescriptor<TestCommand>(TestCommand.SaveAll, nameof(TestCommand.SaveAll), CommandRemappingPolicy.Remappable,
				new KeyCombo(KeyCode.S, KeyModifierSet.Control | KeyModifierSet.Shift)),
			new CommandDescriptor<TestCommand>(TestCommand.Build, nameof(TestCommand.Build), CommandRemappingPolicy.Remappable,
				new KeyCombo(KeyCode.F9, KeyModifierSet.None)),
			new CommandDescriptor<TestCommand>(TestCommand.Undo, nameof(TestCommand.Undo), CommandRemappingPolicy.Remappable,
				new KeyCombo(KeyCode.Z, KeyModifierSet.Control)),
			new CommandDescriptor<TestCommand>(TestCommand.Redo, nameof(TestCommand.Redo), CommandRemappingPolicy.HostManaged,
				new KeyCombo(KeyCode.Y, KeyModifierSet.Control)),
			new CommandDescriptor<TestCommand>(TestCommand.Exit, nameof(TestCommand.Exit), CommandRemappingPolicy.HostReserved,
				new KeyCombo(KeyCode.F4, KeyModifierSet.Alt)),
			new CommandDescriptor<TestCommand>(TestCommand.Find, nameof(TestCommand.Find), CommandRemappingPolicy.Remappable,
				new KeyCombo(KeyCode.F, KeyModifierSet.Control),
				new KeyCombo(KeyCode.H, KeyModifierSet.Control)),
			new CommandDescriptor<TestCommand>(TestCommand.GoToDefinition, nameof(TestCommand.GoToDefinition), CommandRemappingPolicy.Remappable,
				new KeyCombo(KeyCode.F12, KeyModifierSet.None))
		]);
	}

	internal static KeyBindingOverrides CreateOverrides(string serializedId, params (string KeyName, int Modifiers)[] bindings)
	{
		var overrides = new KeyBindingOverrides();

		overrides.Entries.Add(new KeyBindingOverrideEntry
		{
			SerializedId = serializedId,
			Bindings = [.. bindings.Select(binding => CreateBinding([(binding.KeyName, binding.Modifiers)]))]
		});

		return overrides;
	}

	/// <summary>
	/// Builds an overrides document whose entry declares the supplied chords, each one an ordered stroke
	/// sequence of <c>(KeyName, Modifiers)</c> pairs.
	/// </summary>
	internal static KeyBindingOverrides CreateChordOverrides(string serializedId, params (string KeyName, int Modifiers)[][] chords)
	{
		var overrides = new KeyBindingOverrides();

		overrides.Entries.Add(new KeyBindingOverrideEntry
		{
			SerializedId = serializedId,
			Bindings = [.. chords.Select(CreateBinding)]
		});

		return overrides;
	}

	private static KeyBindingOverrideBinding CreateBinding((string KeyName, int Modifiers)[] strokes)
		=> new()
		{
			Strokes =
			[
				.. strokes.Select(stroke => new KeyBindingOverrideStroke { KeyName = stroke.KeyName, Modifiers = stroke.Modifiers })
			]
		};

	internal static KeyBindingServiceOptions CreateOptions(
		ILogger? logger = null,
		IKeyDisplayTextFormatter? displayTextFormatter = null)
	{
		KeyBindingServiceOptions options = new() { Logger = logger };

		return displayTextFormatter is null ? options : options with { DisplayTextFormatter = displayTextFormatter };
	}

	internal static KeyBindingService<TestCommand> CreateService(
		KeyBindingOverrides? overrides = null,
		Func<KeyBindingOverrides, bool>? saveOverrides = null,
		ILogger? logger = null,
		IKeyDisplayTextFormatter? displayTextFormatter = null,
		CommandCatalog<TestCommand>? catalog = null)
		=> new(
			catalog ?? CreateCatalog(),
			new TestOverridesStore(overrides, saveOverrides),
			CreateOptions(logger, displayTextFormatter));

	/// <summary>
	/// Builds a catalog whose bindings include two chords that share the <c>Control+K</c> prefix, which no
	/// other catalog binds.
	/// </summary>
	internal static CommandCatalog<TestCommand> CreateChordCatalog()
	{
		return new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.Save, nameof(TestCommand.Save), CommandRemappingPolicy.Remappable,
				new KeyCombo(KeyCode.S, KeyModifierSet.Control)),
			new CommandDescriptor<TestCommand>(TestCommand.SaveAll, nameof(TestCommand.SaveAll), CommandRemappingPolicy.Remappable,
				new KeyChord(new KeyCombo(KeyCode.K, KeyModifierSet.Control), new KeyCombo(KeyCode.S, KeyModifierSet.Control))),
			new CommandDescriptor<TestCommand>(TestCommand.Find, nameof(TestCommand.Find), CommandRemappingPolicy.Remappable,
				new KeyChord(new KeyCombo(KeyCode.K, KeyModifierSet.Control), new KeyCombo(KeyCode.F, KeyModifierSet.Control))),
			new CommandDescriptor<TestCommand>(TestCommand.Undo, nameof(TestCommand.Undo), CommandRemappingPolicy.Remappable,
				new KeyCombo(KeyCode.Z, KeyModifierSet.Control)),
			new CommandDescriptor<TestCommand>(TestCommand.Build, nameof(TestCommand.Build), CommandRemappingPolicy.Remappable,
				new KeyCombo(KeyCode.F9, KeyModifierSet.None))
		]);
	}

	internal static KeyBindingService<TestCommand> CreateChordService(
		KeyBindingOverrides? overrides = null,
		Func<KeyBindingOverrides, bool>? saveOverrides = null,
		ILogger? logger = null,
		IKeyDisplayTextFormatter? displayTextFormatter = null)
		=> CreateService(overrides, saveOverrides, logger, displayTextFormatter, CreateChordCatalog());

	/// <summary>
	/// Builds a catalog whose bindings are scoped to host contexts: <c>E</c> builds in the
	/// <c>geometry</c> context and finds in the <c>texture</c> context, while <c>Control+S</c> and
	/// <c>Control+Z</c> are always active.
	/// </summary>
	internal static CommandCatalog<TestCommand> CreateContextCatalog()
	{
		return new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.Build, nameof(TestCommand.Build), CommandRemappingPolicy.Remappable,
				new KeyCombo(KeyCode.E, KeyModifierSet.None)) { Context = GeometryContext },
			new CommandDescriptor<TestCommand>(TestCommand.Find, nameof(TestCommand.Find), CommandRemappingPolicy.Remappable,
				new KeyCombo(KeyCode.E, KeyModifierSet.None)) { Context = TextureContext },
			new CommandDescriptor<TestCommand>(TestCommand.Save, nameof(TestCommand.Save), CommandRemappingPolicy.Remappable,
				new KeyCombo(KeyCode.S, KeyModifierSet.Control)),
			new CommandDescriptor<TestCommand>(TestCommand.Undo, nameof(TestCommand.Undo), CommandRemappingPolicy.Remappable,
				new KeyCombo(KeyCode.Z, KeyModifierSet.Control))
		]);
	}

	internal static KeyBindingService<TestCommand> CreateContextService(
		KeyBindingOverrides? overrides = null,
		Func<KeyBindingOverrides, bool>? saveOverrides = null,
		ILogger? logger = null,
		IKeyDisplayTextFormatter? displayTextFormatter = null)
		=> CreateService(overrides, saveOverrides, logger, displayTextFormatter, CreateContextCatalog());

	/// <summary>
	/// Builds a service that binds the same chord to different commands in two contexts, so a chord
	/// started in one context cannot silently complete in the other, and binds <c>F9</c> in the
	/// <c>geometry</c> context alone.
	/// </summary>
	internal static KeyBindingService<TestCommand> CreateModeChordService()
	{
		var chord = new KeyChord(new KeyCombo(KeyCode.K, KeyModifierSet.Control), new KeyCombo(KeyCode.S, KeyModifierSet.Control));

		return CreateService(catalog: new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.SaveAll, nameof(TestCommand.SaveAll), CommandRemappingPolicy.Remappable, chord)
			{
				Context = GeometryContext
			},
			new CommandDescriptor<TestCommand>(TestCommand.Find, nameof(TestCommand.Find), CommandRemappingPolicy.Remappable, chord)
			{
				Context = TextureContext
			},
			new CommandDescriptor<TestCommand>(TestCommand.Build, nameof(TestCommand.Build), CommandRemappingPolicy.Remappable,
				new KeyCombo(KeyCode.F9, KeyModifierSet.None)) { Context = GeometryContext }
		]));
	}

	/// <summary>
	/// Builds a service that binds a single <c>Control</c>-modified stroke to <see cref="TestCommand.Build"/>
	/// in one context, so a context that must not steal a chord continuation has something of its own to
	/// run.
	/// </summary>
	internal static KeyBindingService<TestCommand> CreateContextSingleStrokeService(KeyCode key, string context)
	{
		var catalog = new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.Build, nameof(TestCommand.Build), CommandRemappingPolicy.Remappable,
				new KeyCombo(key, KeyModifierSet.Control)) { Context = context }
		]);

		return CreateService(catalog: catalog);
	}

	internal static (KeyBindingDispatcher<TestCommand> Dispatcher, List<TestCommand> Executed, List<TestCommand> Checked) CreateDispatcher(
		KeyBindingService<TestCommand>? service = null,
		Func<TestCommand, bool>? canExecute = null,
		Action<TestCommand>? execute = null)
	{
		service ??= CreateService();

		var executed = new List<TestCommand>();
		var checkedCommands = new List<TestCommand>();

		var dispatcher = new KeyBindingDispatcher<TestCommand>(
			service,
			new KeyBindingDispatcherHooks<TestCommand>(
				command =>
				{
					checkedCommands.Add(command);
					return canExecute?.Invoke(command) ?? true;
				},
				execute ?? executed.Add));

		return (dispatcher, executed, checkedCommands);
	}
}
