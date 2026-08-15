namespace Nickelony.KeyBindings;

/// <summary>
/// Carries the data of the <see cref="IKeyBindingService{TCommandId}.BindingsChanged"/> notification.
/// </summary>
/// <typeparam name="TCommandId">The command identity type.</typeparam>
/// <remarks>
/// A change either names one command or reports that every command could have changed (a
/// <see cref="IKeyBindingService{TCommandId}.ResetAll"/>). <see cref="AllCommands"/> distinguishes the two,
/// so a consumer never infers it from <see cref="Command"/> - which cannot be <see langword="null"/> when
/// the identity is a value type.
/// </remarks>
public sealed class KeyBindingsChangedEventArgs<TCommandId> : EventArgs
	where TCommandId : notnull
{
	/// <summary>
	/// Initializes a new instance of the <see cref="KeyBindingsChangedEventArgs{TCommandId}"/> class.
	/// </summary>
	/// <param name="command">
	/// The command whose bindings changed, or the identity's <see langword="default"/> value when
	/// <paramref name="allCommands"/> is <see langword="true"/>.
	/// </param>
	/// <param name="allCommands">
	/// <see langword="true"/> when the change could affect every command (a
	/// <see cref="IKeyBindingService{TCommandId}.ResetAll"/>); otherwise, <see langword="false"/>.
	/// </param>
	public KeyBindingsChangedEventArgs(TCommandId? command, bool allCommands = false)
	{
		Command = command;
		AllCommands = allCommands;
	}

	/// <summary>
	/// Gets the command whose bindings changed.
	/// </summary>
	/// <remarks>
	/// Meaningful only when <see cref="AllCommands"/> is <see langword="false"/>; otherwise it is the
	/// identity's <see langword="default"/> value and carries no information.
	/// </remarks>
	public TCommandId? Command { get; }

	/// <summary>
	/// Gets a value indicating whether every command could have changed.
	/// </summary>
	/// <remarks>
	/// <see langword="true"/> for <see cref="IKeyBindingService{TCommandId}.ResetAll"/>, which can change
	/// every cataloged command; a consumer refreshes every binding it renders in that case.
	/// </remarks>
	public bool AllCommands { get; }
}
