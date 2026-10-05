using Nickelony.IDEKit.LanguageFeatures.CodeActions;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;

/// <summary>Groups the required host hooks used by the <see cref="CodeActionController"/>.</summary>
/// <param name="BuildRequest">
/// Builds the provider request for an editor context; it returns <see langword="null"/> to veto the request for that context.
/// The hook runs on the controller's dispatcher thread for every settled context (and for <see cref="CodeActionController.RequestAsync"/>).
/// Returning <see langword="null"/> means the context should not be requested - for example a document the host does not serve, or a caret position
/// the host does not ask about - and clears the published actions until the next context change.
/// </param>
/// <param name="ExecuteActionAsync">
/// Applies the invoked action's edit. The hook runs on the controller's dispatcher thread after the user invoked the action, and it receives
/// the action after the provider resolved it when it offered the action without an edit. The controller has refused an action when the editor's
/// text differs from the action's, so the hook needs no staleness check of its own, and a host that applies the edit asynchronously still applies
/// it through a version-validated edit pipeline for text that changes after the hook starts. The hook applies the edit only: the provider runs
/// the action's command after the hook returns, and not when the hook throws. The token is canceled when the controller is disposed, so a host
/// can stop work that would otherwise outlive the controller. A failure is contained and logged.
/// </param>
/// <remarks>
/// Host hooks should not throw:
/// a failure inside <see cref="ExecuteActionAsync"/> is contained and logged, and so is a failure of the provider call; a failure that escapes
/// <see cref="BuildRequest"/> is contained, logged, and clears the published actions for the current context. The controller rejects
/// a hook that is <see langword="null"/> with an <see cref="ArgumentNullException"/>; <see langword="default"/>, an explicit <see langword="null"/>,
/// and an object initializer that omits a hook all produce one. A <c>BuildRequest</c> that captures a document identifier serves that one document,
/// as the
/// <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/EditorLanguageFeaturesGuide.md#one-controller-per-document">document rule</see>
/// says.
/// </remarks>
public readonly record struct CodeActionControllerHooks(
	Func<CodeActionSelection, CodeActionRequest?> BuildRequest,
	Func<CodeAction, CancellationToken, Task> ExecuteActionAsync
);
