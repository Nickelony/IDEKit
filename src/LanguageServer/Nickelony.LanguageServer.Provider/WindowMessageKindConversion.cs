using Lsp = Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Maps the protocol message type onto the shared <see cref="WindowMessageKind"/>.</summary>
/// <remarks>
/// The mapping is explicit instead of a cast, so a value the protocol does not define cannot leak an undefined enum member. The protocol defines
/// no default type, so an omitted or unknown type reads as <see cref="WindowMessageKind.Log"/>, the least intrusive kind.
/// </remarks>
internal static class WindowMessageKindConversion
{
	/// <summary>Resolves a protocol message type to the corresponding kind.</summary>
	/// <param name="type">The protocol message type, or <see langword="null"/> when the server omitted it.</param>
	/// <returns>The matching kind, or <see cref="WindowMessageKind.Log"/> for an omitted or unknown type.</returns>
	internal static WindowMessageKind FromProtocol(Lsp.MessageType? type)
	{
		return type switch
		{
			Lsp.MessageType.Error => WindowMessageKind.Error,
			Lsp.MessageType.Warning => WindowMessageKind.Warning,
			Lsp.MessageType.Information => WindowMessageKind.Information,
			_ => WindowMessageKind.Log,
		};
	}
}
