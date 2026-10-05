using Avalonia.Input;
using AvaloniaKeyModifiers = Avalonia.Input.KeyModifiers;

namespace Nickelony.KeyBindings.Avalonia.Tests;

[TestClass]
public sealed class KeyEventArgsExtensionsTests
{
	[TestMethod]
	public void ToKeyStroke_Modifiers_ReturnsStroke()
	{
		var stroke = TestKeyEvents.CreateKeyDown(Key.S, AvaloniaKeyModifiers.Control | AvaloniaKeyModifiers.Shift).ToKeyStroke();

		Assert.IsNotNull(stroke);
		Assert.AreEqual(KeyCode.S, stroke.Value.Key);
		Assert.AreEqual(KeyModifiers.Control | KeyModifiers.Shift, stroke.Value.Modifiers);
	}

	[TestMethod]
	public void ToKeyStroke_AllModifiers_AreRecognized()
	{
		var stroke = TestKeyEvents
			.CreateKeyDown(Key.F9, AvaloniaKeyModifiers.Control | AvaloniaKeyModifiers.Shift | AvaloniaKeyModifiers.Alt | AvaloniaKeyModifiers.Meta)
			.ToKeyStroke();

		Assert.IsNotNull(stroke);
		Assert.AreEqual(KeyCode.F9, stroke.Value.Key);
		Assert.AreEqual(KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta, stroke.Value.Modifiers);
	}

	[TestMethod]
	public void ToKeyStroke_WithoutModifiers_ReturnsStroke()
	{
		var stroke = TestKeyEvents.CreateKeyDown(Key.F9).ToKeyStroke();

		Assert.IsNotNull(stroke);
		Assert.AreEqual(KeyCode.F9, stroke.Value.Key);
		Assert.AreEqual(KeyModifiers.None, stroke.Value.Modifiers);
	}

	[TestMethod]
	[DataRow(Key.None)]
	[DataRow(Key.LeftCtrl)]
	[DataRow(Key.System)]
	[DataRow(Key.ImeProcessed)]
	[DataRow(Key.DeadCharProcessed)]
	[DataRow(Key.LineFeed)]
	public void ToKeyStroke_UnbindableKeys_ReturnNull(Key key) => Assert.IsNull(TestKeyEvents.CreateKeyDown(key).ToKeyStroke());

	[TestMethod]
	public void ToKeyStroke_NullEvent_Throws()
	{
		KeyEventArgs? args = null;
		Assert.ThrowsExactly<ArgumentNullException>(() => args!.ToKeyStroke());
	}
}
