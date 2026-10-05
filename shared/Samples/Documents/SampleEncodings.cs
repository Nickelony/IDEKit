using System.Text;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.Samples;

/// <summary>
/// The text encodings of the sample: the ones a file is opened in, and the ones the status bar offers to reopen or save a file with.
/// </summary>
internal static class SampleEncodings
{
	private static IReadOnlyList<TextFileFormat>? s_formats;

	/// <summary>
	/// The encodings a file without a byte-order mark is tried in, in this order: UTF-8, and Windows-1252 for a file that is not valid UTF-8.
	/// </summary>
	/// <remarks>
	/// A single-byte code page accepts nearly every byte sequence, so Windows-1252 is last, and a file in another code page opens as Windows-1252
	/// until the user reopens it with that code page.
	/// </remarks>
	internal static IReadOnlyList<TextEncoding> Candidates { get; } = [TextEncoding.Utf8, TextEncoding.Windows1252];

	/// <summary>The open options of every document of the sample.</summary>
	/// <remarks>
	/// A new file is UTF-8 without a byte-order mark and has no line terminator yet, so the editor inserts its platform's for the first new line.
	/// </remarks>
	internal static WorkspaceDocumentOpenOptions OpenOptions { get; } =
		new(Candidates, new TextFileFormat(TextEncoding.Utf8, HasBom: false, LineTerminatorStyle.None));

	/// <summary>
	/// The formats that the status bar lists, which carry the encoding and the byte-order mark and leave the line terminator style open.
	/// </summary>
	/// <remarks>
	/// A byte-order mark decides the encoding of a file that has one, so the reopen list ignores the mark. UTF-16 is saved with its mark,
	/// as the editors of Windows save it.
	/// </remarks>
	internal static IReadOnlyList<TextFileFormat> Formats =>
		s_formats ??= [
			Format(TextEncoding.Utf8, hasBom: false),
			Format(TextEncoding.Utf8, hasBom: true),
			Format(TextEncoding.Utf16LittleEndian, hasBom: true),
			Format(TextEncoding.Utf16BigEndian, hasBom: true),
			Format(TextEncoding.Windows1252, hasBom: false),
			Format(TextEncoding.FromCodePage(1250), hasBom: false),
			Format(TextEncoding.FromCodePage(1251), hasBom: false),
			Format(TextEncoding.FromCodePage(932), hasBom: false),
		];

	/// <summary>Lets the process resolve the code pages that the runtime does not build in, such as Windows-1252.</summary>
	/// <remarks>
	/// The libraries never register the provider, because it is process-wide state; the host does, once, during startup.
	/// </remarks>
	internal static void RegisterCodePages() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

	private static TextFileFormat Format(TextEncoding encoding, bool hasBom) => new(encoding, hasBom, LineTerminatorStyle.None);
}
