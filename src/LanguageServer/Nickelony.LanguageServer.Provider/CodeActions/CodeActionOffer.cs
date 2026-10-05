using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Remembers what the provider needs to resolve a code action it offered: the action as the server sent it, and the document it was offered for.
/// </summary>
/// <param name="Payload">The action exactly as the server sent it, which <c>codeAction/resolve</c> takes back unchanged.</param>
/// <param name="DocumentId">The identifier of the document the action was offered for, as the code-action request named it.</param>
/// <param name="DocumentText">The text of that document at the request, which the action's positions and the server's state refer to.</param>
/// <remarks>
/// The offer travels as the opaque <see cref="Nickelony.IDEKit.LanguageFeatures.CodeActions.CodeAction.Data"/> of the action, so a host hands
/// it back without reading it. The resolution compares <paramref name="DocumentText"/> with the text the provider tracks, because a server resolves
/// an action against the text it holds and not against the text the host showed the user.
/// </remarks>
internal sealed record CodeActionOffer(CodeActionPayload Payload, string DocumentId, string DocumentText);
