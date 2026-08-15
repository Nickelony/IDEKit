using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;
using System.Text;

namespace Nickelony.IDEKit.Workspace.Tests;

[TestClass]
public sealed class WorkspaceTextCodecTests
{
	[TestMethod]
	public async Task WorkspaceTextCodec_DecodesByteOrderMarksAndFallbackEncodingAndRecordsNewlines()
	{
		using var temp = new TemporaryDirectory();
		string directory = temp.Path;
		string classicScriptPath = Path.Combine(directory, "strings.txt");
		File.WriteAllBytes(classicScriptPath, [0x63, 0x61, 0x66, 0xE9, 0x0D, 0x0A]);
		string utf8Path = Path.Combine(directory, "utf8.txt");
		File.WriteAllBytes(utf8Path, WorkspaceTextCodec.Encode(
			"one\r\ntwo",
			new TextFileFormat(TextEncodingKind.Utf8, true, TextNewlineStyle.CrLf)));
		string utf16LittleEndianPath = Path.Combine(directory, "utf16-le.txt");
		File.WriteAllBytes(utf16LittleEndianPath, WorkspaceTextCodec.Encode(
			"little",
			new TextFileFormat(TextEncodingKind.Utf16LittleEndian, true, TextNewlineStyle.None)));
		string utf16BigEndianPath = Path.Combine(directory, "utf16-be.txt");
		File.WriteAllBytes(utf16BigEndianPath, WorkspaceTextCodec.Encode(
			"big",
			new TextFileFormat(TextEncodingKind.Utf16BigEndian, true, TextNewlineStyle.None)));
		var fileSystem = new LocalWorkspaceFileSystem();

		WorkspaceFileReadResult classicScriptBytes = await fileSystem.ReadAsync(
			classicScriptPath,
			CancellationToken.None);
		WorkspaceFileReadResult utf8Bytes = await fileSystem.ReadAsync(
			utf8Path,
			CancellationToken.None);
		WorkspaceFileReadResult utf16LittleEndianBytes = await fileSystem.ReadAsync(
			utf16LittleEndianPath,
			CancellationToken.None);
		WorkspaceFileReadResult utf16BigEndianBytes = await fileSystem.ReadAsync(
			utf16BigEndianPath,
			CancellationToken.None);
		TextFileFormat classicScriptFormat = new();
		TextFileFormat utf8FormatWithBom = new();
		TextFileFormat utf16LittleEndianFormat = new();
		TextFileFormat utf16BigEndianFormat = new();
		string classicScriptContent = WorkspaceTextCodec.Decode(
			classicScriptBytes.RawBytes!.Value.Span,
			TextEncodingKind.Windows1252,
			out classicScriptFormat);
		string utf8Content = WorkspaceTextCodec.Decode(
			utf8Bytes.RawBytes!.Value.Span,
			TextEncodingKind.Windows1252,
			out utf8FormatWithBom);
		string utf16LittleEndianContent = WorkspaceTextCodec.Decode(
			utf16LittleEndianBytes.RawBytes!.Value.Span,
			TextEncodingKind.Windows1252,
			out utf16LittleEndianFormat);
		string utf16BigEndianContent = WorkspaceTextCodec.Decode(
			utf16BigEndianBytes.RawBytes!.Value.Span,
			TextEncodingKind.Windows1252,
			out utf16BigEndianFormat);

		Assert.AreEqual("café\r\n", classicScriptContent);
		Assert.AreEqual(TextEncodingKind.Windows1252, classicScriptFormat.Encoding);
		Assert.IsFalse(classicScriptFormat.HasBom);
		Assert.AreEqual(TextNewlineStyle.CrLf, classicScriptFormat.NewlineStyle);
		Assert.AreEqual("one\r\ntwo", utf8Content);
		Assert.AreEqual(TextEncodingKind.Utf8, utf8FormatWithBom.Encoding);
		Assert.IsTrue(utf8FormatWithBom.HasBom);
		Assert.AreEqual(TextNewlineStyle.CrLf, utf8FormatWithBom.NewlineStyle);
		Assert.AreEqual("little", utf16LittleEndianContent);
		Assert.AreEqual(TextEncodingKind.Utf16LittleEndian, utf16LittleEndianFormat.Encoding);
		Assert.IsTrue(utf16LittleEndianFormat.HasBom);
		Assert.AreEqual("big", utf16BigEndianContent);
		Assert.AreEqual(TextEncodingKind.Utf16BigEndian, utf16BigEndianFormat.Encoding);
		Assert.IsTrue(utf16BigEndianFormat.HasBom);
		Assert.AreEqual(TextNewlineStyle.Cr, WorkspaceTextCodec.DetectNewlineStyle("one\r"));
		Assert.AreEqual(TextNewlineStyle.Lf, WorkspaceTextCodec.DetectNewlineStyle("one\ntwo"));
		Assert.AreEqual(TextNewlineStyle.Mixed, WorkspaceTextCodec.DetectNewlineStyle("one\r\ntwo\nthree\r"));
		Assert.AreEqual(TextNewlineStyle.None, WorkspaceTextCodec.DetectNewlineStyle("one"));
		Assert.IsTrue(classicScriptBytes.OnDiskStamp.Exists);
		Assert.IsFalse(string.IsNullOrEmpty(classicScriptBytes.OnDiskStamp.ContentHash));
	}

	[TestMethod]
	public void WorkspaceTextCodec_Encode_RejectsWindows1252WithByteOrderMark()
	{
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() => WorkspaceTextCodec.Encode(
			"content",
			new TextFileFormat(TextEncodingKind.Windows1252, true, TextNewlineStyle.None)));

		Assert.AreEqual("fileFormat", exception.ParamName);
	}

	[TestMethod]
	public void WorkspaceTextCodec_Encode_RejectsUndefinedEncoding()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WorkspaceTextCodec.Encode(
			"content",
			new TextFileFormat((TextEncodingKind)42, false, TextNewlineStyle.None)));
	}

	[TestMethod]
	public void WorkspaceTextCodec_Encode_RejectsUndefinedWindows1252CodePoints()
	{
		// The five C1 code points that Windows-1252 leaves undefined are rejected on decode, so the
		// encoder rejects them as well; otherwise, a committed document could never be decoded again.
		EncoderFallbackException exception = Assert.ThrowsExactly<EncoderFallbackException>(() => WorkspaceTextCodec.Encode(
			"before\u0081after",
			new TextFileFormat(TextEncodingKind.Windows1252, false, TextNewlineStyle.None)));

		StringAssert.Contains(exception.Message, "U+0081");
	}

	[TestMethod]
	public void WorkspaceTextCodec_Windows1252RoundTripsDefinedCodePoints()
	{
		TextFileFormat format = new(TextEncodingKind.Windows1252, false, TextNewlineStyle.None);
		string content = "caf\u00E9\r\n\u2013";

		byte[] bytes = WorkspaceTextCodec.Encode(content, format);
		string decoded = WorkspaceTextCodec.Decode(bytes, TextEncodingKind.Windows1252, out TextFileFormat decodedFormat);

		Assert.AreEqual(content, decoded);
		Assert.AreEqual(TextEncodingKind.Windows1252, decodedFormat.Encoding);
		Assert.IsFalse(decodedFormat.HasBom);
	}

	[TestMethod]
	public void WorkspaceTextCodec_DecodesBomlessUtf16AndEmptyContent()
	{
		string littleEndianContent = WorkspaceTextCodec.Decode(
			Encoding.Unicode.GetBytes("without mark"),
			TextEncodingKind.Utf16LittleEndian,
			out TextFileFormat littleEndianFormat);
		string bigEndianContent = WorkspaceTextCodec.Decode(
			Encoding.BigEndianUnicode.GetBytes("without mark"),
			TextEncodingKind.Utf16BigEndian,
			out TextFileFormat bigEndianFormat);

		// Without a byte-order mark the caller-supplied encoding decides how the bytes are read.
		Assert.AreEqual("without mark", littleEndianContent);
		Assert.AreEqual(TextEncodingKind.Utf16LittleEndian, littleEndianFormat.Encoding);
		Assert.IsFalse(littleEndianFormat.HasBom);
		Assert.AreEqual(TextNewlineStyle.None, littleEndianFormat.NewlineStyle);
		Assert.AreEqual("without mark", bigEndianContent);
		Assert.AreEqual(TextEncodingKind.Utf16BigEndian, bigEndianFormat.Encoding);
		Assert.IsFalse(bigEndianFormat.HasBom);

		// Empty input decodes as empty text without a newline style and encodes back to empty bytes.
		Assert.AreEqual(string.Empty, WorkspaceTextCodec.Decode([], TextEncodingKind.Utf8, out TextFileFormat emptyFormat));
		Assert.AreEqual(TextNewlineStyle.None, emptyFormat.NewlineStyle);
		Assert.AreEqual(0, WorkspaceTextCodec.Encode(string.Empty, emptyFormat).Length);

		// A byte-order mark without content still decodes to empty text and reports the mark.
		byte[] bomOnly = WorkspaceTextCodec.Encode(
			string.Empty,
			new TextFileFormat(TextEncodingKind.Utf8, true, TextNewlineStyle.None));
		Assert.AreEqual(3, bomOnly.Length);
		Assert.AreEqual(string.Empty, WorkspaceTextCodec.Decode(bomOnly, TextEncodingKind.Windows1252, out TextFileFormat bomOnlyFormat));
		Assert.IsTrue(bomOnlyFormat.HasBom);
		Assert.AreEqual(TextEncodingKind.Utf8, bomOnlyFormat.Encoding);
	}

	[TestMethod]
	public void WorkspaceTextCodec_RejectsUtf32UndefinedByteSequencesAndOutOfRangeCharacters()
	{
		// UTF-32 input is rejected by both byte orders even though no encoding kind models it.
		byte[] utf32LittleEndian = [0xFF, 0xFE, 0x00, 0x00, 0x41, 0x00, 0x00, 0x00];
		byte[] utf32BigEndian = [0x00, 0x00, 0xFE, 0xFF, 0x00, 0x00, 0x00, 0x41];

		Assert.ThrowsExactly<DecoderFallbackException>(() => WorkspaceTextCodec.Decode(
			utf32LittleEndian,
			TextEncodingKind.Utf8,
			out _));
		Assert.ThrowsExactly<DecoderFallbackException>(() => WorkspaceTextCodec.Decode(
			utf32BigEndian,
			TextEncodingKind.Utf8,
			out _));

		// The undefined Windows-1252 byte values are rejected instead of decoding as control characters.
		Assert.ThrowsExactly<DecoderFallbackException>(() => WorkspaceTextCodec.Decode(
			[0x81],
			TextEncodingKind.Windows1252,
			out _));

		// Windows-1252 can only encode its own repertoire.
		Assert.ThrowsExactly<EncoderFallbackException>(() => WorkspaceTextCodec.Encode(
			"\u2603",
			new TextFileFormat(TextEncodingKind.Windows1252, false, TextNewlineStyle.None)));
	}

	[TestMethod]
	public void WorkspaceTextCodec_Encode_RejectsUnpairedSurrogateWithStrictUtf8()
	{
		// The strict UTF-8 encoder rejects an unpaired surrogate instead of writing a replacement
		// character that the next decode would not round-trip.
		Assert.ThrowsExactly<EncoderFallbackException>(() => WorkspaceTextCodec.Encode(
			"a\uD800b",
			new TextFileFormat(TextEncodingKind.Utf8, false, TextNewlineStyle.None)));
	}

	[TestMethod]
	public void WorkspaceTextCodec_DecodeBomOnlyUtf16_ReportsTheMarkAndEmptyText()
	{
		string content = WorkspaceTextCodec.Decode([0xFF, 0xFE], TextEncodingKind.Utf8, out TextFileFormat format);

		Assert.AreEqual(string.Empty, content);
		Assert.AreEqual(TextEncodingKind.Utf16LittleEndian, format.Encoding);
		Assert.IsTrue(format.HasBom);
		Assert.AreEqual(TextNewlineStyle.None, format.NewlineStyle);
	}

	[TestMethod]
	public void WorkspaceTextCodec_DecodeOddLengthUtf16Payload_IsRejected()
	{
		// The trailing half code unit has no valid decode under the strict fallback.
		Assert.ThrowsExactly<DecoderFallbackException>(() => WorkspaceTextCodec.Decode(
			[0xFF, 0xFE, 0x41],
			TextEncodingKind.Utf8,
			out _));
	}

	[TestMethod]
	public void WorkspaceTextCodec_DecodeUndefinedFallbackEncoding_IsAnArgumentErrorEvenWithABom()
	{
		// The fallback encoding is validated up front, so an undefined value is an argument error even
		// when a recognized byte-order mark would have taken precedence over it.
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WorkspaceTextCodec.Decode(
			[0xEF, 0xBB, 0xBF, 0x41],
			(TextEncodingKind)42,
			out _));
	}

	[TestMethod]
	public void WorkspaceTextCodec_Encode_RejectsNullContent()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => WorkspaceTextCodec.Encode(
			null!,
			new TextFileFormat(TextEncodingKind.Utf8, false, TextNewlineStyle.Lf)));
	}

	[TestMethod]
	public void WorkspaceTextCodec_RejectsUndefinedEncodingKindAndNullContent()
	{
		// Undefined encoding kinds are invalid input for both directions instead of being mapped to a
		// fallback encoding.
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WorkspaceTextCodec.Decode(
			[0x41],
			(TextEncodingKind)99,
			out _));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WorkspaceTextCodec.Encode(
			"content",
			new TextFileFormat((TextEncodingKind)99, false, TextNewlineStyle.None)));
		Assert.ThrowsExactly<ArgumentNullException>(() => WorkspaceTextCodec.DetectNewlineStyle(null!));
	}
}
