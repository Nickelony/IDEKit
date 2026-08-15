using Avalonia.Input;
using AvaloniaKeyModifiers = Avalonia.Input.KeyModifiers;

namespace Nickelony.KeyBindings.Avalonia.Tests;

[TestClass]
public class KeyEventArgsExtensionsTests
{
	[TestMethod]
	public void ToKeyCombo_Modifiers_ReturnsCombo()
	{
		KeyCombo? combo = TestKeyEvents.CreateKeyDown(Key.S, AvaloniaKeyModifiers.Control | AvaloniaKeyModifiers.Shift).ToKeyCombo();

		Assert.IsNotNull(combo);
		Assert.AreEqual(KeyCode.S, combo.Value.Key);
		Assert.AreEqual(KeyModifierSet.Control | KeyModifierSet.Shift, combo.Value.Modifiers);
	}

	[TestMethod]
	public void ToKeyCombo_AllModifiers_AreRecognized()
	{
		KeyCombo? combo = TestKeyEvents
			.CreateKeyDown(Key.F9, AvaloniaKeyModifiers.Control | AvaloniaKeyModifiers.Shift | AvaloniaKeyModifiers.Alt | AvaloniaKeyModifiers.Meta)
			.ToKeyCombo();

		Assert.IsNotNull(combo);
		Assert.AreEqual(KeyCode.F9, combo.Value.Key);
		Assert.AreEqual(KeyModifierSet.Control | KeyModifierSet.Shift | KeyModifierSet.Alt | KeyModifierSet.Meta, combo.Value.Modifiers);
	}

	[TestMethod]
	public void ToKeyCombo_WithoutModifiers_ReturnsCombo()
	{
		KeyCombo? combo = TestKeyEvents.CreateKeyDown(Key.F9).ToKeyCombo();

		Assert.IsNotNull(combo);
		Assert.AreEqual(KeyCode.F9, combo.Value.Key);
		Assert.AreEqual(KeyModifierSet.None, combo.Value.Modifiers);
	}

	[TestMethod]
	[DataRow(Key.None)]
	[DataRow(Key.LeftCtrl)]
	[DataRow(Key.System)]
	[DataRow(Key.ImeProcessed)]
	[DataRow(Key.DeadCharProcessed)]
	[DataRow(Key.LineFeed)]
	public void ToKeyCombo_UnbindableKeys_ReturnNull(Key key)
		=> Assert.IsNull(TestKeyEvents.CreateKeyDown(key).ToKeyCombo());

	[TestMethod]
	public void ToKeyCombo_NullEvent_Throws()
	{
		KeyEventArgs? args = null;

		Assert.ThrowsExactly<ArgumentNullException>(() => args!.ToKeyCombo());
	}
}
