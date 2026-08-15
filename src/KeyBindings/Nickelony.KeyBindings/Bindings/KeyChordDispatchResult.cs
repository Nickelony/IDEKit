namespace Nickelony.KeyBindings;

/// <summary>
/// Describes the result of <see cref="KeyBindingDispatcher{TCommandId}.Dispatch(KeyCombo, string?)"/>.
/// </summary>
public enum KeyChordDispatchResult
{
	/// <summary>
	/// No command was invoked and no chord is pending. The stroke is unbound and may be offered to
	/// another dispatcher in a priority chain.
	/// </summary>
	NotHandled,

	/// <summary>
	/// The stroke continued or started a chord and the dispatcher now owns a pending chord. The stroke
	/// is consumed: another dispatcher in a priority chain must not see it.
	/// </summary>
	Pending,

	/// <summary>The stroke completed a bound chord and the command was invoked.</summary>
	Executed
}
