namespace Nickelony.KeyBindings;

/// <summary>Groups the command callbacks a <see cref="KeyBindingDispatcher{TCommandId}"/> invokes for a resolved chord.</summary>
/// <typeparam name="TCommandId">The command identity type.</typeparam>
/// <param name="CanExecuteCommand">
/// Determines whether a command may currently execute. A command that cannot execute does not swallow its stroke, and neither does the first
/// stroke of a chord that only such commands can complete.
/// </param>
/// <param name="ExecuteCommand">Invokes the resolved command once a chord resolves to an executable command.</param>
/// <remarks>
/// <para>
/// Bundling the callbacks prevents the dispatcher constructor from swapping them by position. The dispatcher constructor rejects a bundle
/// whose callback is <see langword="null"/>, including a <see langword="default"/> instance.
/// </para>
/// <para>
/// The dispatcher calls <paramref name="CanExecuteCommand"/> synchronously from <c>Dispatch</c>, for the command a completed chord resolves to and,
/// when a stroke starts or continues a longer chord, for the commands that chord can still complete, until one returns <see langword="true"/>.
/// A stroke can therefore reach it for several commands that do not execute, so it should stay cheap and free of side effects.
/// </para>
/// </remarks>
public readonly record struct KeyBindingDispatcherHooks<TCommandId>(Func<TCommandId, bool> CanExecuteCommand, Action<TCommandId> ExecuteCommand)
	where TCommandId : notnull;
