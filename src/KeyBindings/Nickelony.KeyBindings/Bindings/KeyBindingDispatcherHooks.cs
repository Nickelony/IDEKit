namespace Nickelony.KeyBindings;

/// <summary>
/// Groups the command callbacks a <see cref="KeyBindingDispatcher{TCommandId}"/> invokes for a
/// resolved chord, so the dispatcher constructor stays readable and positional mistakes between the
/// delegates are impossible.
/// </summary>
/// <remarks>
/// The type is a plain holder, not a value: it groups two delegates for one dispatcher and has no
/// meaningful equality or ordering of its own, which is why it is a class rather than a record.
/// </remarks>
/// <typeparam name="TCommandId">The command identity type.</typeparam>
public sealed class KeyBindingDispatcherHooks<TCommandId>
	where TCommandId : notnull
{
	/// <summary>
	/// Initializes a new instance of the <see cref="KeyBindingDispatcherHooks{TCommandId}"/> class.
	/// </summary>
	/// <param name="canExecuteCommand">
	/// Determines whether a resolved command may currently execute. A command that cannot execute does not
	/// swallow its stroke.
	/// </param>
	/// <param name="executeCommand">Invokes the resolved command.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="canExecuteCommand"/> or <paramref name="executeCommand"/> is <see langword="null"/>.
	/// </exception>
	public KeyBindingDispatcherHooks(
		Func<TCommandId, bool> canExecuteCommand,
		Action<TCommandId> executeCommand)
	{
		ArgumentNullException.ThrowIfNull(canExecuteCommand);
		ArgumentNullException.ThrowIfNull(executeCommand);

		CanExecuteCommand = canExecuteCommand;
		ExecuteCommand = executeCommand;
	}

	/// <summary>
	/// Gets the callback that determines whether a resolved command may currently execute. A command
	/// that cannot execute does not swallow its stroke.
	/// </summary>
	public Func<TCommandId, bool> CanExecuteCommand { get; }

	/// <summary>
	/// Gets the callback that invokes the resolved command.
	/// </summary>
	public Action<TCommandId> ExecuteCommand { get; }
}
