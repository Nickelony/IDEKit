using Nickelony.KeyBindings.Testing;
using static Nickelony.KeyBindings.Testing.KeyBindingTestFixtures;

namespace Nickelony.KeyBindings.Tests;

/// <summary>
/// Holds the shared catalog, service, and single-stroke fixtures for the key binding service tests; the
/// lookup, validation, apply, reset, reset-persistence, and override-loading concerns live in the
/// <c>KeyBindingServiceTests.Lookup.cs</c>, <c>KeyBindingServiceTests.Validation.cs</c>,
/// <c>KeyBindingServiceTests.Apply.cs</c>, <c>KeyBindingServiceTests.Reset.cs</c>,
/// <c>KeyBindingServiceTests.ResetPersistence.cs</c>, and <c>KeyBindingServiceTests.Loading.cs</c>
/// partials.
/// </summary>
[TestClass]
public partial class KeyBindingServiceTests
{
	private static CommandCatalog<TestCommand> CreateReplacementCatalog()
	{
		return new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.Save, nameof(TestCommand.Save), CommandRemappingPolicy.Remappable,
				new KeyCombo(KeyCode.S, KeyModifierSet.Control)),
			new CommandDescriptor<TestCommand>(TestCommand.Undo, nameof(TestCommand.Undo), CommandRemappingPolicy.Remappable,
				new KeyCombo(KeyCode.Z, KeyModifierSet.Control),
				new KeyCombo(KeyCode.U, KeyModifierSet.Control)),
			new CommandDescriptor<TestCommand>(TestCommand.Redo, nameof(TestCommand.Redo), CommandRemappingPolicy.Remappable,
				new KeyCombo(KeyCode.Y, KeyModifierSet.Control))
		]);
	}

	private static KeyBindingService<TestCommand> CreateReplacementService(
		KeyBindingOverrides? overrides = null,
		Func<KeyBindingOverrides, bool>? saveOverrides = null)
		=> CreateService(overrides, saveOverrides, catalog: CreateReplacementCatalog());

	private static KeyBindingOverrideBinding SingleStroke(string keyName, KeyModifierSet modifiers)
		=> new()
		{
			Strokes = [new KeyBindingOverrideStroke { KeyName = keyName, Modifiers = (int)modifiers }]
		};

	private sealed class BracketFormatter : IKeyDisplayTextFormatter
	{
		public string GetDisplayText(KeyCombo keyCombo)
			=> $"[{keyCombo.Key}]";

		public string GetDisplayText(KeyChord keyChord)
			=> string.Join(' ', keyChord.Strokes.ToArray().Select(GetDisplayText));
	}
}
