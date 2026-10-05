namespace Nickelony.KeyBindings;

/// <summary>Classifies what <see cref="KeyBindingDispatcher{TCommandId}.Dispatch(KeyStroke, string?)"/> did with a stroke.</summary>
public enum KeyStrokeDispatchOutcome
{
	/// <summary>No command was invoked, and the stroke neither started nor continued a chord.</summary>
	/// <remarks>
	/// A priority chain may offer the stroke to another dispatcher.
	/// </remarks>
	NotHandled,

	/// <summary>The stroke started or continued a chord, which the dispatcher now owns.</summary>
	/// <remarks>
	/// The stroke is consumed: a priority chain must not offer it to another dispatcher.
	/// </remarks>
	Pending,

	/// <summary>The stroke completed a bound chord and the command was invoked.</summary>
	Executed,
}
