namespace Nickelony.KeyBindings;

/// <summary>Carries the data of the <see cref="IKeyBindingService{TCommandId}.BindingsChanged"/> notification.</summary>
/// <typeparam name="TCommandId">The command identity type.</typeparam>
/// <remarks>
/// A change either names one command or reports that other commands
/// could have changed too, as a <see cref="IKeyBindingService{TCommandId}.ResetAll"/> does, and as an <see cref="IKeyBindingService{TCommandId}.Apply"/>
/// or a <see cref="IKeyBindingService{TCommandId}.Reset"/> does when another command dispatches
/// different bindings afterwards. <see cref="AffectsAllCommands"/> distinguishes the two, so a host never infers it from <see cref="Command"/> -
/// which cannot be <see langword="null"/> when the identity is a value type.
/// </remarks>
public sealed class BindingsChangedEventArgs<TCommandId> : EventArgs
	where TCommandId : notnull
{
	/// <param name="command">
	/// The command whose bindings changed, or the identity's <see langword="default"/> value when <paramref name="affectsAllCommands"/>
	/// is <see langword="true"/>.
	/// </param>
	/// <param name="affectsAllCommands">
	/// <see langword="true"/> when the change could affect commands beyond one named command; otherwise, <see langword="false"/>.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="command"/> is <see langword="null"/> while <paramref name="affectsAllCommands"/> is <see langword="false"/>;
	/// a single-command change must name its command. With a value-type identity the guard can never trigger.
	/// </exception>
	public BindingsChangedEventArgs(TCommandId? command, bool affectsAllCommands = false)
	{
		if (!affectsAllCommands && command is null)
			throw new ArgumentNullException(nameof(command), "A single-command change must name its command.");

		Command = command;
		AffectsAllCommands = affectsAllCommands;
	}

	/// <summary>The command whose bindings changed.</summary>
	/// <remarks>
	/// Meaningful only when <see cref="AffectsAllCommands"/> is <see langword="false"/>; otherwise, it is the identity's <see langword="default"/>
	/// value and carries no information.
	/// </remarks>
	public TCommandId? Command { get; }

	/// <summary>Indicates whether commands beyond one named command could have changed.</summary>
	/// <remarks>
	/// <see langword="true"/> for <see cref="IKeyBindingService{TCommandId}.ResetAll"/>, which can change
	/// every cataloged command, and for an <see cref="IKeyBindingService{TCommandId}.Apply"/> or a <see cref="IKeyBindingService{TCommandId}.Reset"/>
	/// after which another command dispatches different bindings, such as one that took a chord under <see cref="KeyBindingConflictPolicy.Replace"/>
	/// or regained a default that the change stopped shadowing; a host refreshes every binding it renders in that case.
	/// </remarks>
	public bool AffectsAllCommands { get; }
}
