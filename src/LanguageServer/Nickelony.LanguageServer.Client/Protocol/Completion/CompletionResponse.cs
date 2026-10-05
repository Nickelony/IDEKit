using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents a completion response that may arrive either as an item array or as an LSP completion list.</summary>
/// <remarks>
/// The wire distinctions are preserved: an absent or JSON <see langword="null"/> <c>items</c> property leaves
/// <see cref="Items"/> as <see langword="null"/>, an empty list yields an empty collection, and a JSON <see langword="null"/> response deserializes
/// to a <see langword="null"/> response.
/// </remarks>
[JsonConverter(typeof(CompletionResponseJsonConverter))]
public sealed class CompletionResponse
{
	/// <param name="items">The completion items returned by the server, when any were provided.</param>
	/// <param name="isIncomplete">Whether the server marked the completion list as incomplete.</param>
	public CompletionResponse(IReadOnlyList<CompletionItemPayload>? items, bool isIncomplete = false)
	{
		Items = items is null ? null : Array.AsReadOnly([.. items]);
		IsIncomplete = isIncomplete;
	}

	/// <summary>The completion items returned by the server, or <see langword="null"/> when the response carried no items array.</summary>
	/// <remarks>
	/// The returned list is a defensive read-only snapshot.
	/// </remarks>
	public IReadOnlyList<CompletionItemPayload>? Items { get; }

	public bool IsIncomplete { get; }
}
