using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a completion response that may arrive either as an item array or as an LSP completion list.
/// </summary>
/// <remarks>
/// The wire distinctions are preserved: an absent or JSON <see langword="null"/> <c>items</c> property leaves <see cref="Items"/> as
/// <see langword="null"/>, an empty list yields an empty collection, and a JSON <see langword="null"/> response deserializes to a
/// <see langword="null"/> response.
/// </remarks>
[JsonConverter(typeof(CompletionResponseJsonConverter))]
public sealed class CompletionResponse
{
	/// <summary>
	/// Initializes a new instance of the <see cref="CompletionResponse"/> class.
	/// </summary>
	/// <param name="items">The completion items returned by the server, when any were provided.</param>
	/// <param name="isIncomplete">Whether the server marked the completion list as incomplete.</param>
	public CompletionResponse(IReadOnlyList<CompletionItemPayload>? items, bool isIncomplete = false)
	{
		Items = items is null ? null : Array.AsReadOnly([.. items]);
		IsIncomplete = isIncomplete;
	}

	/// <summary>
	/// Gets the completion items returned by the server, when any were provided.
	/// The returned list is a defensive read-only snapshot.
	/// </summary>
	public IReadOnlyList<CompletionItemPayload>? Items { get; }

	/// <summary>
	/// Gets a value indicating whether the server marked the completion list as incomplete.
	/// </summary>
	public bool IsIncomplete { get; }
}
