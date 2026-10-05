using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.KeyBindings.Testing;

namespace Nickelony.KeyBindings.Tests;

[TestClass]
public sealed class PublishedSnapshotTests
{
	[TestMethod]
	public void WithDisplayText_ReturnsACopyThatSharesTheMapsAndLeavesTheSourceWithoutText()
	{
		PublishedSnapshot<TestCommand> source = BuildMaps();

		PublishedSnapshot<TestCommand> copy = source.WithDisplayText(KeyDisplayTextFormatter.Default);

		Assert.AreNotSame(source, copy);
		Assert.IsEmpty(source.DisplayTextByCommand);
		Assert.AreSame(source.BindingsByCommand, copy.BindingsByCommand);
		Assert.AreSame(source.CommandsByChord, copy.CommandsByChord);
		Assert.IsNotEmpty(source.CommandsByChordPrefix);
		Assert.AreSame(source.CommandsByChordPrefix, copy.CommandsByChordPrefix);
		Assert.AreSame(source.ContextScopedChords, copy.ContextScopedChords);
	}

	[TestMethod]
	public void WithDisplayText_JoinsTheBindingsOfACommandAndSkipsACommandWithoutADispatchingBinding()
	{
		PublishedSnapshot<TestCommand> source = BuildMaps();
		IKeyDisplayTextFormatter formatter = KeyDisplayTextFormatter.Default;

		PublishedSnapshot<TestCommand> copy = source.WithDisplayText(formatter);

		foreach ((TestCommand command, ImmutableArray<KeyChord> bindings) in copy.BindingsByCommand)
		{
			if (bindings.Length == 0)
			{
				Assert.IsFalse(copy.DisplayTextByCommand.ContainsKey(command));
			}
			else
			{
				Assert.AreEqual(
					string.Join(" / ", bindings.Select(binding => formatter.GetDisplayText(binding))),
					copy.DisplayTextByCommand[command]
				);
			}
		}

		Assert.IsFalse(copy.BindingsByCommand.TryGetValue(TestCommand.Undo, out ImmutableArray<KeyChord> undo) && !undo.IsEmpty);
		Assert.IsFalse(copy.DisplayTextByCommand.ContainsKey(TestCommand.Undo));
		Assert.IsNotEmpty(copy.DisplayTextByCommand);
	}

	[TestMethod]
	public void DiffersForCommandsOtherThan_IdenticalSnapshots_ReturnsFalse()
	{
		PublishedSnapshot<TestCommand> first = BuildMaps();
		PublishedSnapshot<TestCommand> second = BuildMaps();

		Assert.IsFalse(first.DiffersForCommandsOtherThan(second, TestCommand.Save));
	}

	[TestMethod]
	public void DiffersForCommandsOtherThan_OnlyOneCommandDiffers_ReturnsTrueUnlessThatCommandIsExcluded()
	{
		PublishedSnapshot<TestCommand> before = BuildMaps(new KeyBindingOverrides());
		PublishedSnapshot<TestCommand> after = BuildMaps(OverrideOf(TestCommand.Undo, Ctrl(KeyCode.U)));

		Assert.IsTrue(before.DiffersForCommandsOtherThan(after, TestCommand.Save));
		Assert.IsFalse(before.DiffersForCommandsOtherThan(after, TestCommand.Undo));
	}

	[TestMethod]
	public void DiffersForCommandsOtherThan_ACommandKeepsItsChordsInAnotherOrder_ReturnsTrue()
	{
		PublishedSnapshot<TestCommand> before = BuildMaps(OverrideOf(TestCommand.Undo, Ctrl(KeyCode.U), Ctrl(KeyCode.I)));
		PublishedSnapshot<TestCommand> after = BuildMaps(OverrideOf(TestCommand.Undo, Ctrl(KeyCode.I), Ctrl(KeyCode.U)));

		Assert.IsTrue(before.DiffersForCommandsOtherThan(after, TestCommand.Save));
		Assert.IsFalse(before.DiffersForCommandsOtherThan(after, TestCommand.Undo));
	}

	private static KeyChord Ctrl(KeyCode key) => new(new KeyStroke(key, KeyModifiers.Control));

	private static KeyBindingOverrides OverrideOf(TestCommand command, params KeyChord[] chords)
	{
		var overrides = new KeyBindingOverrides();

		overrides.Entries.Add(KeyBindingOverrideCodec.CreateEntry(command.ToString(), chords));

		return overrides;
	}

	private static PublishedSnapshot<TestCommand> BuildMaps()
	{
		// Save binds a two-stroke chord, so the prefix set is not empty; Undo is unbound, so it has no dispatching binding.
		var overrides = new KeyBindingOverrides();

		overrides.Entries.Add(
			KeyBindingOverrideCodec.CreateEntry(
				nameof(TestCommand.Save),
				[new KeyChord(new KeyStroke(KeyCode.Q, KeyModifiers.Control), new KeyStroke(KeyCode.W, KeyModifiers.Control))]
			)
		);

		overrides.Entries.Add(KeyBindingOverrideCodec.CreateEntry(nameof(TestCommand.Undo), []));

		return BuildMaps(overrides);
	}

	private static PublishedSnapshot<TestCommand> BuildMaps(KeyBindingOverrides overrides)
	{
		var builder = new KeyBindingMapBuilder<TestCommand>(
			KeyBindingTestHelpers.CreateCatalog(),
			NullLogger.Instance,
			KeyDisplayTextFormatter.Default
		);

		KeyBindingMapBuilder<TestCommand>.MapBuildResult result = builder.Build(overrides, logDiagnostics: false);

		Assert.IsTrue(result.Succeeded, result.Reason);
		Assert.IsNotNull(result.Maps);

		return result.Maps;
	}
}
