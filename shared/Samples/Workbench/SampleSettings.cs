using System.Text.Json;

namespace Nickelony.Samples;

/// <summary>The values of the settings file: the editor look, the hover delay and the command the Run button starts.</summary>
/// <param name="Theme">The name of the theme in <see cref="SampleThemes.Catalog"/>.</param>
/// <param name="FontSize">The editor font size in points.</param>
/// <param name="TabSize">
/// The number of spaces one indentation level inserts, or the width of a tab, in a file that does not show its own size.
/// </param>
/// <param name="HoverDelay">How long the pointer rests over a name before its hover shows.</param>
/// <param name="TrimTrailingWhitespace">Whether saving removes the trailing whitespace of every line.</param>
/// <param name="RunCommand">The command line the Run button starts.</param>
internal sealed record SampleSettings(string Theme, double FontSize, int TabSize, TimeSpan HoverDelay, bool TrimTrailingWhitespace, string RunCommand)
{
	internal static SampleSettings Default { get; } =
		new(
			Theme: "Light",
			FontSize: 14,
			TabSize: 4,
			HoverDelay: TimeSpan.FromMilliseconds(400),
			TrimTrailingWhitespace: true,
			RunCommand: "dotnet --info"
		);

	/// <summary>
	/// Reads the settings from JSON text, keeping the default for a missing, mistyped or out-of-range member, or returns <see langword="null"/>
	/// for text that is not a JSON object.
	/// </summary>
	internal static SampleSettings? FromJson(string json)
	{
		try
		{
			using var document = JsonDocument.Parse(json);

			if (document.RootElement.ValueKind != JsonValueKind.Object)
				return null;

			JsonElement root = document.RootElement;

			return new SampleSettings(
				Theme: ReadString(root, "theme") ?? Default.Theme,

				FontSize: root.TryGetProperty("fontSize", out JsonElement fontSize)
				&& fontSize.TryGetDouble(out double size)
				&& size is >= 6 and <= 96
					? size
					: Default.FontSize,

				TabSize: root.TryGetProperty("tabSize", out JsonElement tabSize) && tabSize.TryGetInt32(out int tabs) && tabs is >= 1 and <= 16
					? tabs
					: Default.TabSize,

				HoverDelay: root.TryGetProperty("hoverDelay", out JsonElement hoverDelay)
				&& hoverDelay.TryGetInt32(out int delay)
				&& delay is >= 0 and <= 5000
					? TimeSpan.FromMilliseconds(delay)
					: Default.HoverDelay,

				TrimTrailingWhitespace: root.TryGetProperty("trimTrailingWhitespace", out JsonElement trim)
				&& trim.ValueKind is JsonValueKind.True or JsonValueKind.False
					? trim.GetBoolean()
					: Default.TrimTrailingWhitespace,

				RunCommand: ReadString(root, "runCommand") ?? Default.RunCommand
			);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static string? ReadString(JsonElement root, string name) =>
		root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
