namespace Nickelony.KeyBindings.Tests;

[TestClass]
public class KeyCodeTests
{
	/// <summary>
	/// Pins the canonical serialized names and their values, so a rename or a renumbering fails
	/// loudly instead of silently changing the persisted override format.
	/// </summary>
	[TestMethod]
	[DataRow(KeyCode.Backspace, 2)]
	[DataRow(KeyCode.Enter, 5)]
	[DataRow(KeyCode.PageUp, 17)]
	[DataRow(KeyCode.PageDown, 18)]
	[DataRow(KeyCode.PrintScreen, 28)]
	[DataRow(KeyCode.F12, 97)]
	[DataRow(KeyCode.Semicolon, 128)]
	[DataRow(KeyCode.Equals, 129)]
	[DataRow(KeyCode.Comma, 130)]
	[DataRow(KeyCode.Minus, 131)]
	[DataRow(KeyCode.Period, 132)]
	[DataRow(KeyCode.Slash, 133)]
	[DataRow(KeyCode.Grave, 134)]
	[DataRow(KeyCode.LeftBracket, 137)]
	[DataRow(KeyCode.Backslash, 138)]
	[DataRow(KeyCode.RightBracket, 139)]
	[DataRow(KeyCode.Apostrophe, 140)]
	[DataRow(KeyCode.Oem8, 141)]
	[DataRow(KeyCode.IntlBackslash, 142)]
	public void CanonicalMembers_KeepTheirSerializedNameAndValue(KeyCode keyCode, int expectedValue)
		=> Assert.AreEqual(expectedValue, (int)keyCode);

	[TestMethod]
	public void ExcludedValues_AreNotDefined()
	{
		Assert.IsFalse(Enum.IsDefined((KeyCode)0), "The default value must stay undefined.");
		Assert.IsFalse(Enum.IsDefined((KeyCode)143), "A value past the dense member range must stay undefined.");
		Assert.IsFalse(Enum.IsDefined((KeyCode)255), "A value outside the dense member range must stay undefined.");
		Assert.IsFalse(Enum.IsDefined((KeyCode)1000), "A value outside the dense member range must stay undefined.");
	}

	/// <summary>
	/// Pins the serialized-name contract for every member neutrally, without a framework adapter: the
	/// name of a defined value parses back to that same value, so a persisted override never resolves to
	/// a different key after a rename.
	/// </summary>
	[TestMethod]
	public void EveryDefinedMember_RoundTripsThroughItsSerializedName()
	{
		foreach (KeyCode keyCode in Enum.GetValues<KeyCode>())
		{
			string? name = Enum.GetName(keyCode);

			Assert.IsNotNull(name, $"KeyCode.{keyCode} has no name.");
			Assert.IsTrue((int)keyCode > 0, $"KeyCode.{name} must be positive to stay a distinct value.");

			Assert.IsTrue(Enum.TryParse(name, out KeyCode parsed), $"'{name}' does not parse back to a KeyCode.");
			Assert.AreEqual(keyCode, parsed, $"'{name}' does not round-trip to its own member.");
			Assert.IsTrue(Enum.IsDefined(parsed), $"'{name}' parses to a value that is not a defined member.");
		}
	}

	/// <summary>
	/// Pins that no two members share a value or a name, so a serialized name identifies exactly one key
	/// and <see cref="Enum.GetName"/> can never be ambiguous.
	/// </summary>
	[TestMethod]
	public void Members_HaveDistinctNamesAndValues()
	{
		KeyCode[] values = Enum.GetValues<KeyCode>();
		string[] names = Enum.GetNames<KeyCode>();

		Assert.HasCount(names.Length, values);
		Assert.HasCount(values.Length, new HashSet<KeyCode>(values), "Two members share a value.");
		Assert.HasCount(names.Length, new HashSet<string>(names, StringComparer.Ordinal), "Two members share a name.");
	}
}
