namespace Nickelony.KeyBindings;

/// <summary>
/// The modifier sets the key model defines, shared by the model, the binding service, and the toolkit
/// adapters so the mask has a single declaration.
/// </summary>
public static class KeyModifierMasks
{
	/// <summary>
	/// Every <see cref="KeyModifierSet"/> flag the key model defines. A combination outside this mask
	/// carries an unknown bit.
	/// </summary>
	public const KeyModifierSet Defined = KeyModifierSet.Alt | KeyModifierSet.Control | KeyModifierSet.Shift | KeyModifierSet.Meta;
}
