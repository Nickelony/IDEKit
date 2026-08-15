using Nickelony.KeyBindings.Testing;
using static Nickelony.KeyBindings.Testing.KeyBindingTestFixtures;

namespace Nickelony.KeyBindings.Tests;

/// <summary>
/// Covers the read surface: resolving a combo or chord to its owning command, listing a command's
/// bindings, rendering display text (including chord strokes), and reporting which combos are unbound
/// chord prefixes.
/// </summary>
public partial class KeyBindingServiceTests
{
	[TestMethod]
	public void TryGetCommand_KnownCombos_ReturnsCommands()
	{
		KeyBindingService<TestCommand> service = CreateService();

		Assert.IsTrue(service.TryGetCommand(new KeyCombo(KeyCode.S, KeyModifierSet.Control), null, out TestCommand saveCommand));
		Assert.AreEqual(TestCommand.Save, saveCommand);

		Assert.IsTrue(service.TryGetCommand(new KeyCombo(KeyCode.F9, KeyModifierSet.None), null, out TestCommand buildCommand));
		Assert.AreEqual(TestCommand.Build, buildCommand);

		Assert.IsTrue(service.TryGetCommand(new KeyCombo(KeyCode.F4, KeyModifierSet.Alt), null, out TestCommand exitCommand));
		Assert.AreEqual(TestCommand.Exit, exitCommand);
	}

	[TestMethod]
	public void TryGetCommand_DifferentModifiers_ReturnsFalse()
	{
		KeyBindingService<TestCommand> service = CreateService();

		Assert.IsFalse(service.TryGetCommand(new KeyCombo(KeyCode.S, KeyModifierSet.None), null, out _));
		Assert.IsFalse(service.TryGetCommand(new KeyCombo(KeyCode.S, KeyModifierSet.Control | KeyModifierSet.Alt), null, out _));
		Assert.IsFalse(service.TryGetCommand(new KeyCombo(KeyCode.A, KeyModifierSet.None), null, out _));
	}

	[TestMethod]
	public void TryGetCommand_SecondaryBinding_Resolves()
	{
		KeyBindingService<TestCommand> service = CreateService();

		Assert.IsTrue(service.TryGetCommand(new KeyCombo(KeyCode.H, KeyModifierSet.Control), null, out TestCommand findCommand));
		Assert.AreEqual(TestCommand.Find, findCommand);
	}

	[TestMethod]
	public void GetBindings_UnboundOrUncatalogedCommand_ReturnsEmpty()
	{
		KeyBindingService<TestCommand> service = CreateService(CreateOverrides(nameof(TestCommand.Save)));

		Assert.IsEmpty(service.GetBindings(TestCommand.Save));
		Assert.IsEmpty(service.GetBindings(TestCommand.None));
	}

	[TestMethod]
	public void GetDisplayText_ReturnsExactTexts()
	{
		KeyBindingService<TestCommand> service = CreateService(CreateOverrides(nameof(TestCommand.Save)));

		Assert.AreEqual("Control+Z", service.GetDisplayText(TestCommand.Undo));
		Assert.AreEqual("Control+F / Control+H", service.GetDisplayText(TestCommand.Find));
		Assert.AreEqual("Fallback", service.GetDisplayText(TestCommand.Save, "Fallback"));
		Assert.AreEqual("Fallback", service.GetDisplayText(TestCommand.None, "Fallback"));
		Assert.AreEqual(string.Empty, service.GetDisplayText(TestCommand.None));
	}

	[TestMethod]
	public void GetDisplayText_UsesConfiguredFormatter()
	{
		KeyBindingService<TestCommand> service = CreateService(displayTextFormatter: new BracketFormatter());

		Assert.AreEqual("[F] / [H]", service.GetDisplayText(TestCommand.Find));
	}

	/// <summary>
	/// Pins that the precomputed display text is rebuilt by every mutation: a text that only reflected
	/// the construction-time bindings would keep showing the old chord after an <c>Apply</c> and would
	/// not return to the default after a <c>Reset</c>.
	/// </summary>
	[TestMethod]
	public void GetDisplayText_AfterApplyAndReset_TracksTheCurrentBindings()
	{
		KeyBindingService<TestCommand> service = CreateService();

		Assert.AreEqual("Control+S", service.GetDisplayText(TestCommand.Save));

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Apply(TestCommand.Save, [new KeyCombo(KeyCode.X, KeyModifierSet.Control)]));
		Assert.AreEqual("Control+X", service.GetDisplayText(TestCommand.Save));

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Reset(TestCommand.Save));
		Assert.AreEqual("Control+S", service.GetDisplayText(TestCommand.Save));
	}

	[TestMethod]
	public void TryGetCommand_ByChord_ReturnsTheOwningCommand()
	{
		KeyBindingService<TestCommand> service = CreateChordService();

		Assert.IsTrue(service.TryGetCommand(
			new KeyChord(new KeyCombo(KeyCode.K, KeyModifierSet.Control), new KeyCombo(KeyCode.S, KeyModifierSet.Control)),
			null,
			out TestCommand saveAll));
		Assert.AreEqual(TestCommand.SaveAll, saveAll);

		// A single keystroke is a one-stroke chord.
		Assert.IsTrue(service.TryGetCommand(new KeyCombo(KeyCode.S, KeyModifierSet.Control), null, out TestCommand save));
		Assert.AreEqual(TestCommand.Save, save);

		// The prefix both chords share is bound to nothing.
		Assert.IsFalse(service.TryGetCommand(new KeyCombo(KeyCode.K, KeyModifierSet.Control), null, out _));
	}

	[TestMethod]
	public void IsChordPrefix_ReportsUnboundPrefixesOnly()
	{
		KeyBindingService<TestCommand> service = CreateChordService();

		Assert.IsTrue(service.IsChordPrefix(new KeyCombo(KeyCode.K, KeyModifierSet.Control), null));
		Assert.IsFalse(service.IsChordPrefix(
			new KeyChord(new KeyCombo(KeyCode.K, KeyModifierSet.Control), new KeyCombo(KeyCode.S, KeyModifierSet.Control)),
			null));
		Assert.IsFalse(service.IsChordPrefix(new KeyCombo(KeyCode.S, KeyModifierSet.Control), null));
	}

	[TestMethod]
	public void GetBindings_ChordCatalog_ReturnsTheChord()
		=> Assert.AreEqual(
			new KeyChord(new KeyCombo(KeyCode.K, KeyModifierSet.Control), new KeyCombo(KeyCode.S, KeyModifierSet.Control)),
			CreateChordService().GetBindings(TestCommand.SaveAll)[0]);

	[TestMethod]
	public void GetDisplayText_RendersChordStrokesWithTheFormatter()
	{
		KeyBindingService<TestCommand> service = CreateChordService();

		Assert.AreEqual("Control+K, Control+S", service.GetDisplayText(TestCommand.SaveAll));
		Assert.AreEqual("Control+K, Control+F", service.GetDisplayText(TestCommand.Find));
	}

	/// <summary>
	/// Pins the blank-token contract: an empty or whitespace-only token is not a token the catalog
	/// declares, so it resolves only the always-active bindings, exactly like any other undeclared token.
	/// </summary>
	[TestMethod]
	public void TryGetCommand_BlankOrUndeclaredToken_ResolvesOnlyAlwaysActiveBindings()
	{
		KeyBindingService<TestCommand> service = CreateService();

		Assert.IsTrue(service.TryGetCommand(new KeyCombo(KeyCode.S, KeyModifierSet.Control), "", out TestCommand emptyCommand));
		Assert.AreEqual(TestCommand.Save, emptyCommand);

		Assert.IsTrue(service.TryGetCommand(new KeyCombo(KeyCode.S, KeyModifierSet.Control), "   ", out TestCommand whitespaceCommand));
		Assert.AreEqual(TestCommand.Save, whitespaceCommand);

		Assert.IsTrue(service.TryGetCommand(new KeyCombo(KeyCode.S, KeyModifierSet.Control), "Gemoetry", out TestCommand typoCommand));
		Assert.AreEqual(TestCommand.Save, typoCommand);
	}

	/// <summary>
	/// Pins that a blank or undeclared token reports only the always-active prefix relationships, never a
	/// context-scoped one.
	/// </summary>
	[TestMethod]
	public void IsChordPrefix_BlankOrUndeclaredToken_ReportsOnlyAlwaysActivePrefixes()
	{
		KeyBindingService<TestCommand> service = CreateChordService();

		Assert.IsTrue(service.IsChordPrefix(new KeyCombo(KeyCode.K, KeyModifierSet.Control), ""));
		Assert.IsTrue(service.IsChordPrefix(new KeyCombo(KeyCode.K, KeyModifierSet.Control), "   "));
		Assert.IsTrue(service.IsChordPrefix(new KeyCombo(KeyCode.K, KeyModifierSet.Control), "Gemoetry"));
	}

	/// <summary>
	/// Pins that a command whose every binding is shadowed by another command's override has no dispatching
	/// binding, so its display text falls back.
	/// </summary>
	[TestMethod]
	public void GetDisplayText_EveryBindingShadowed_ReturnsFallback()
	{
		// Save's override claims both of Find's defaults, so Find has no dispatching binding left.
		KeyBindingService<TestCommand> service = CreateService(
			CreateOverrides(nameof(TestCommand.Save), ("F", (int)KeyModifierSet.Control), ("H", (int)KeyModifierSet.Control)));

		Assert.IsEmpty(service.GetBindings(TestCommand.Find));
		Assert.AreEqual("Fallback", service.GetDisplayText(TestCommand.Find, "Fallback"));
	}
}
