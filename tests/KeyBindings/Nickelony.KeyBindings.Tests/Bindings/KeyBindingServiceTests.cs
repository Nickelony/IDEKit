using Nickelony.KeyBindings.OverrideEntries;
using Nickelony.KeyBindings.Testing;
using static Nickelony.KeyBindings.Testing.KeyBindingTestHelpers;

namespace Nickelony.KeyBindings.Tests;

/// <summary>Holds the shared catalog, service, and single-stroke fixtures for the KeyBindingService test partials.</summary>
[TestClass]
public sealed partial class KeyBindingServiceTests
{
	private static CommandCatalog<TestCommand> CreateReplacementCatalog()
	{
		return new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(
				TestCommand.Save,
				nameof(TestCommand.Save),
				CommandRemappingPolicy.Remappable,
				new KeyStroke(KeyCode.S, KeyModifiers.Control)
			),

			new CommandDescriptor<TestCommand>(
				TestCommand.Undo,
				nameof(TestCommand.Undo),
				CommandRemappingPolicy.Remappable,
				new KeyStroke(KeyCode.Z, KeyModifiers.Control),
				new KeyStroke(KeyCode.U, KeyModifiers.Control)
			),

			new CommandDescriptor<TestCommand>(
				TestCommand.Redo,
				nameof(TestCommand.Redo),
				CommandRemappingPolicy.Remappable,
				new KeyStroke(KeyCode.Y, KeyModifiers.Control)
			),
		]);
	}

	private static KeyBindingService<TestCommand> CreateReplacementService(
		KeyBindingOverrides? overrides = null,
		Func<KeyBindingOverrides, bool>? saveOverrides = null
	) => CreateService(overrides, saveOverrides, catalog: CreateReplacementCatalog());

	private static KeyBindingOverrideChord SingleStroke(string keyName, KeyModifiers modifiers) =>
		new() { Strokes = [new KeyBindingOverrideStroke { KeyName = keyName, Modifiers = (int)modifiers }] };

	private sealed class BracketFormatter : IKeyDisplayTextFormatter
	{
		public string GetDisplayText(KeyStroke keyStroke) => $"[{keyStroke.Key}]";
		public string GetDisplayText(KeyChord keyChord) => string.Join(' ', keyChord.Strokes.ToArray().Select(GetDisplayText));
	}
}
