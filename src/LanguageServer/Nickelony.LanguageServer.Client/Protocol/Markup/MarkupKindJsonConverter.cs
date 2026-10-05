using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Reads and writes <see cref="MarkupKind"/> as the protocol's <c>plaintext</c> and <c>markdown</c> strings.</summary>
/// <remarks>
/// Reading never fails: a string other than <c>markdown</c> (compared without regard to case), and a value that is not a string,
/// read as <see cref="MarkupKind.PlainText"/>.
/// </remarks>
internal sealed class MarkupKindJsonConverter : JsonConverter<MarkupKind>
{
	/// <summary>Maps a protocol <c>kind</c> string to a markup kind.</summary>
	/// <param name="wireName">The protocol value, or <see langword="null"/> when none was sent.</param>
	/// <returns><see cref="MarkupKind.Markdown"/> for <c>markdown</c> in any casing; otherwise, <see cref="MarkupKind.PlainText"/>.</returns>
	internal static MarkupKind FromWireName(string? wireName) =>
		string.Equals(wireName, "markdown", StringComparison.OrdinalIgnoreCase) ? MarkupKind.Markdown : MarkupKind.PlainText;

	/// <summary>Maps a markup kind to its protocol <c>kind</c> string.</summary>
	/// <param name="kind">The markup kind to map.</param>
	/// <returns><c>markdown</c> for <see cref="MarkupKind.Markdown"/>; otherwise, <c>plaintext</c>.</returns>
	internal static string ToWireName(MarkupKind kind) => kind == MarkupKind.Markdown ? "markdown" : "plaintext";

	/// <inheritdoc/>
	public override MarkupKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.String)
			return FromWireName(reader.GetString());

		reader.Skip();
		return MarkupKind.PlainText;
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, MarkupKind value, JsonSerializerOptions options) => writer.WriteStringValue(ToWireName(value));
}
