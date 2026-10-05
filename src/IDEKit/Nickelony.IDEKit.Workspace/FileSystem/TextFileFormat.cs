using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Workspace.FileSystem;

/// <summary>Describes the encoding, byte-order mark, and line terminator style of a workspace file.</summary>
/// <remarks>
/// The store detects the style on read, and the caller supplies it on open defaults and replacements; a text view supplies the style of the text
/// it publishes.
/// </remarks>
public readonly record struct TextFileFormat(TextEncoding Encoding, bool HasBom, LineTerminatorStyle LineTerminatorStyle)
{
	/// <summary>The name of the encoding as a status bar shows it.</summary>
	/// <remarks>
	/// UTF-8 behind a byte-order mark is "UTF-8 with BOM". Any other encoding is its <see cref="TextEncoding.DisplayName"/>, so a UTF-16 file reads
	/// "UTF-16 LE" or "UTF-16 BE" with or without its mark.
	/// </remarks>
	public string EncodingDisplayName => HasBom && Encoding == TextEncoding.Utf8 ? "UTF-8 with BOM" : Encoding.DisplayName;
}
