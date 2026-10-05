namespace Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;

/// <summary>
/// Classifies how <see cref="CodeActionController.ExecuteActionAsync"/> ended, so a presenter can tell the user why nothing changed.
/// </summary>
public enum CodeActionExecutionOutcome
{
	/// <summary>The execute hook ran, and so did the action's command when it has one.</summary>
	Completed,

	/// <summary>
	/// The editor's text differs from the text the action was requested for, or the action is not one of the published actions, so nothing ran.
	/// </summary>
	Stale,

	/// <summary>The provider could not resolve the action into an edit or a command, so nothing ran.</summary>
	NotResolved,

	/// <summary>The first execution of the same action was still running, so this call did nothing.</summary>
	AlreadyRunning,

	/// <summary>The execute hook failed, so the command did not run.</summary>
	HookFailed,

	/// <summary>The edit was applied, but the provider did not run the action's command or failed to.</summary>
	CommandFailed,

	/// <summary>The controller was disposed before the action finished.</summary>
	Disposed,
}
