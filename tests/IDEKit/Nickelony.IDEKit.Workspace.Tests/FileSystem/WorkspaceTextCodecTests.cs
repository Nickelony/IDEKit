using System.Text;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

[TestClass]
public sealed class WorkspaceTextCodecTests
{
	private static TextEncoding ShiftJis => TextEncoding.FromCodePage(932);

	private static TextEncoding Windows1250 => TextEncoding.FromCodePage(1250);

	// The Japanese word for Japanese (U+65E5 U+672C U+8A9E) in Shift-JIS, which no UTF-8 reader accepts, and the Czech letters U+0159 U+011B U+0161 in Windows-1250, whose first byte is invalid UTF-8 too.
	private static readonly byte[] s_shiftJisBytes = [0x93, 0xFA, 0x96, 0x7B, 0x8C, 0xEA];
	private static readonly byte[] s_windows1250Bytes = [0xF8, 0xEC, 0x9A];

	[TestMethod]
	public async Task WorkspaceTextCodec_DecodesByteOrderMarksAndFallbackEncodingAndRecordsLineTerminators()
	{
		using var temp = new TemporaryDirectory();

		string directory = temp.Path;
		string classicScriptPath = Path.Combine(directory, "strings.txt");

		File.WriteAllBytes(classicScriptPath, [0x63, 0x61, 0x66, 0xE9, 0x0D, 0x0A]);
		string utf8Path = Path.Combine(directory, "utf8.txt");

		File.WriteAllBytes(utf8Path, WorkspaceTextCodec.Encode("one\r\ntwo", new TextFileFormat(TextEncoding.Utf8, true, LineTerminatorStyle.CrLf)));

		string utf16LittleEndianPath = Path.Combine(directory, "utf16-le.txt");

		File.WriteAllBytes(
			utf16LittleEndianPath,
			WorkspaceTextCodec.Encode("little", new TextFileFormat(TextEncoding.Utf16LittleEndian, true, LineTerminatorStyle.None))
		);

		string utf16BigEndianPath = Path.Combine(directory, "utf16-be.txt");

		File.WriteAllBytes(
			utf16BigEndianPath,
			WorkspaceTextCodec.Encode("big", new TextFileFormat(TextEncoding.Utf16BigEndian, true, LineTerminatorStyle.None))
		);

		var fileSystem = new LocalWorkspaceFileSystem();

		WorkspaceFileReadResult classicScriptBytes = await fileSystem.ReadAsync(classicScriptPath, CancellationToken.None);
		WorkspaceFileReadResult utf8Bytes = await fileSystem.ReadAsync(utf8Path, CancellationToken.None);
		WorkspaceFileReadResult utf16LittleEndianBytes = await fileSystem.ReadAsync(utf16LittleEndianPath, CancellationToken.None);
		WorkspaceFileReadResult utf16BigEndianBytes = await fileSystem.ReadAsync(utf16BigEndianPath, CancellationToken.None);

		TextFileFormat classicScriptFormat = new();
		TextFileFormat utf8FormatWithBom = new();
		TextFileFormat utf16LittleEndianFormat = new();
		TextFileFormat utf16BigEndianFormat = new();

		string classicScriptContent = WorkspaceTextCodec.Decode(
			classicScriptBytes.RawBytes!.Value.Span,
			[TextEncoding.Windows1252],
			out classicScriptFormat
		);

		string utf8Content = WorkspaceTextCodec.Decode(utf8Bytes.RawBytes!.Value.Span, [TextEncoding.Windows1252], out utf8FormatWithBom);

		string utf16LittleEndianContent = WorkspaceTextCodec.Decode(
			utf16LittleEndianBytes.RawBytes!.Value.Span,
			[TextEncoding.Windows1252],
			out utf16LittleEndianFormat
		);

		string utf16BigEndianContent = WorkspaceTextCodec.Decode(
			utf16BigEndianBytes.RawBytes!.Value.Span,
			[TextEncoding.Windows1252],
			out utf16BigEndianFormat
		);

		Assert.AreEqual("café\r\n", classicScriptContent);
		Assert.AreEqual(TextEncoding.Windows1252, classicScriptFormat.Encoding);
		Assert.IsFalse(classicScriptFormat.HasBom);
		Assert.AreEqual(LineTerminatorStyle.CrLf, classicScriptFormat.LineTerminatorStyle);
		Assert.AreEqual("one\r\ntwo", utf8Content);
		Assert.AreEqual(TextEncoding.Utf8, utf8FormatWithBom.Encoding);
		Assert.IsTrue(utf8FormatWithBom.HasBom);
		Assert.AreEqual(LineTerminatorStyle.CrLf, utf8FormatWithBom.LineTerminatorStyle);
		Assert.AreEqual("little", utf16LittleEndianContent);
		Assert.AreEqual(TextEncoding.Utf16LittleEndian, utf16LittleEndianFormat.Encoding);
		Assert.IsTrue(utf16LittleEndianFormat.HasBom);
		Assert.AreEqual("big", utf16BigEndianContent);
		Assert.AreEqual(TextEncoding.Utf16BigEndian, utf16BigEndianFormat.Encoding);
		Assert.IsTrue(utf16BigEndianFormat.HasBom);
		Assert.IsTrue(classicScriptBytes.OnDiskStamp.Exists);
		Assert.IsFalse(string.IsNullOrEmpty(classicScriptBytes.OnDiskStamp.ContentHash));
	}

	[TestMethod]
	[DataRow("one", LineTerminatorStyle.None, DisplayName = "None")]
	[DataRow("one\ntwo", LineTerminatorStyle.Lf, DisplayName = "Lf")]
	[DataRow("one\r\ntwo", LineTerminatorStyle.CrLf, DisplayName = "CrLf")]
	[DataRow("one\rtwo", LineTerminatorStyle.Cr, DisplayName = "Cr")]
	[DataRow("one\r\ntwo\nthree\r", LineTerminatorStyle.Mixed, DisplayName = "Mixed")]
	public void WorkspaceTextCodec_Decode_RecordsTheLineTerminatorStyleOfTheDecodedText(string content, LineTerminatorStyle expected)
	{
		string decoded = WorkspaceTextCodec.Decode(Encoding.UTF8.GetBytes(content), [TextEncoding.Utf8], out TextFileFormat format);

		Assert.AreEqual(content, decoded);
		Assert.AreEqual(expected, format.LineTerminatorStyle);
	}

	[TestMethod]
	public void WorkspaceTextCodec_Encode_RejectsAByteOrderMarkForAnEncodingWithoutOne()
	{
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() =>
			WorkspaceTextCodec.Encode("content", new TextFileFormat(TextEncoding.Windows1252, true, LineTerminatorStyle.None))
		);

		Assert.AreEqual("fileFormat", exception.ParamName);

		Assert.ThrowsExactly<ArgumentException>(() =>
			WorkspaceTextCodec.Encode("content", new TextFileFormat(ShiftJis, true, LineTerminatorStyle.None))
		);
	}

	[TestMethod]
	public void WorkspaceTextCodec_Encode_TreatsTheDefaultFormatAsUtf8WithoutAByteOrderMark() =>
		Assert.AreSequenceEqual("é"u8.ToArray(), WorkspaceTextCodec.Encode("\u00E9", default));

	[TestMethod]
	public void WorkspaceTextCodec_Encode_RejectsUndefinedWindows1252CodePoints()
	{
		// The five C1 code points that Windows-1252 leaves undefined are rejected on decode, so the
		// encoder rejects them as well; otherwise, a committed document could never be decoded again.
		EncoderFallbackException exception = Assert.ThrowsExactly<EncoderFallbackException>(() =>
			WorkspaceTextCodec.Encode("before\u0081after", new TextFileFormat(TextEncoding.Windows1252, false, LineTerminatorStyle.None))
		);

		Assert.Contains("U+0081", exception.Message);
	}

	[TestMethod]
	public void WorkspaceTextCodec_Windows1252RoundTripsDefinedCodePoints()
	{
		TextFileFormat format = new(TextEncoding.Windows1252, false, LineTerminatorStyle.None);
		string content = "caf\u00E9\r\n\u2013";

		byte[] bytes = WorkspaceTextCodec.Encode(content, format);
		string decoded = WorkspaceTextCodec.Decode(bytes, [TextEncoding.Windows1252], out TextFileFormat decodedFormat);

		Assert.AreEqual(content, decoded);
		Assert.AreEqual(TextEncoding.Windows1252, decodedFormat.Encoding);
		Assert.IsFalse(decodedFormat.HasBom);
	}

	[TestMethod]
	public void WorkspaceTextCodec_DecodesBomlessUtf16AndEmptyContent()
	{
		string littleEndianContent = WorkspaceTextCodec.Decode(
			Encoding.Unicode.GetBytes("without mark"),
			[TextEncoding.Utf16LittleEndian],
			out TextFileFormat littleEndianFormat
		);

		string bigEndianContent = WorkspaceTextCodec.Decode(
			Encoding.BigEndianUnicode.GetBytes("without mark"),
			[TextEncoding.Utf16BigEndian],
			out TextFileFormat bigEndianFormat
		);

		// Without a byte-order mark the caller-supplied encoding decides how the bytes are read.
		Assert.AreEqual("without mark", littleEndianContent);
		Assert.AreEqual(TextEncoding.Utf16LittleEndian, littleEndianFormat.Encoding);
		Assert.IsFalse(littleEndianFormat.HasBom);
		Assert.AreEqual(LineTerminatorStyle.None, littleEndianFormat.LineTerminatorStyle);
		Assert.AreEqual("without mark", bigEndianContent);
		Assert.AreEqual(TextEncoding.Utf16BigEndian, bigEndianFormat.Encoding);
		Assert.IsFalse(bigEndianFormat.HasBom);

		// Empty input decodes as empty text without a line terminator style and encodes back to empty bytes.
		Assert.AreEqual(string.Empty, WorkspaceTextCodec.Decode([], [TextEncoding.Utf8], out TextFileFormat emptyFormat));
		Assert.AreEqual(LineTerminatorStyle.None, emptyFormat.LineTerminatorStyle);
		Assert.IsEmpty(WorkspaceTextCodec.Encode(string.Empty, emptyFormat));

		// A byte-order mark without content still decodes to empty text and reports the mark.
		byte[] bomOnly = WorkspaceTextCodec.Encode(string.Empty, new TextFileFormat(TextEncoding.Utf8, true, LineTerminatorStyle.None));

		Assert.HasCount(3, bomOnly);
		Assert.AreEqual(string.Empty, WorkspaceTextCodec.Decode(bomOnly, [TextEncoding.Windows1252], out TextFileFormat bomOnlyFormat));
		Assert.IsTrue(bomOnlyFormat.HasBom);
		Assert.AreEqual(TextEncoding.Utf8, bomOnlyFormat.Encoding);
	}

	[TestMethod]
	public void WorkspaceTextCodec_RejectsUtf32UndefinedByteSequencesAndOutOfRangeCharacters()
	{
		// UTF-32 input is rejected by both byte orders even though no text encoding models it.
		byte[] utf32LittleEndian = [0xFF, 0xFE, 0x00, 0x00, 0x41, 0x00, 0x00, 0x00];
		byte[] utf32BigEndian = [0x00, 0x00, 0xFE, 0xFF, 0x00, 0x00, 0x00, 0x41];

		Assert.ThrowsExactly<DecoderFallbackException>(() => WorkspaceTextCodec.Decode(utf32LittleEndian, [TextEncoding.Utf8], out _));
		Assert.ThrowsExactly<DecoderFallbackException>(() => WorkspaceTextCodec.Decode(utf32BigEndian, [TextEncoding.Utf8], out _));

		// The undefined Windows-1252 byte values are rejected instead of decoding as control characters.
		Assert.ThrowsExactly<DecoderFallbackException>(() => WorkspaceTextCodec.Decode([0x81], [TextEncoding.Windows1252], out _));

		// Windows-1252 can only encode its own repertoire.
		Assert.ThrowsExactly<EncoderFallbackException>(() =>
			WorkspaceTextCodec.Encode("\u2603", new TextFileFormat(TextEncoding.Windows1252, false, LineTerminatorStyle.None))
		);
	}

	[TestMethod]
	public void WorkspaceTextCodec_Encode_RejectsUnpairedSurrogateWithStrictUtf8()
	{
		// The strict UTF-8 encoder rejects an unpaired surrogate instead of writing a replacement
		// character that the next decode would not round-trip.
		Assert.ThrowsExactly<EncoderFallbackException>(() =>
			WorkspaceTextCodec.Encode("a\uD800b", new TextFileFormat(TextEncoding.Utf8, false, LineTerminatorStyle.None))
		);
	}

	[TestMethod]
	public void WorkspaceTextCodec_DecodeBomOnlyUtf16_ReportsTheMarkAndEmptyText()
	{
		string content = WorkspaceTextCodec.Decode([0xFF, 0xFE], [TextEncoding.Utf8], out TextFileFormat format);

		Assert.AreEqual(string.Empty, content);
		Assert.AreEqual(TextEncoding.Utf16LittleEndian, format.Encoding);
		Assert.IsTrue(format.HasBom);
		Assert.AreEqual(LineTerminatorStyle.None, format.LineTerminatorStyle);
	}

	[TestMethod]
	public void WorkspaceTextCodec_DecodeOddLengthUtf16Payload_IsRejected()
	{
		// The trailing half code unit has no valid decode under the strict fallback.
		Assert.ThrowsExactly<DecoderFallbackException>(() => WorkspaceTextCodec.Decode([0xFF, 0xFE, 0x41], [TextEncoding.Utf8], out _));
	}

	[TestMethod]
	public void WorkspaceTextCodec_Encode_RejectsNullContent()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			WorkspaceTextCodec.Encode(null!, new TextFileFormat(TextEncoding.Utf8, false, LineTerminatorStyle.Lf))
		);
	}

	[TestMethod]
	public void WorkspaceTextCodec_Decode_ValidatesTheCandidatesEvenWhenAByteOrderMarkDecides()
	{
		byte[] utf8WithMark = [0xEF, 0xBB, 0xBF, 0x41];

		Assert.ThrowsExactly<ArgumentNullException>(() => WorkspaceTextCodec.Decode(utf8WithMark, null!, out _));
		Assert.ThrowsExactly<ArgumentException>(() => WorkspaceTextCodec.Decode(utf8WithMark, [], out _));
		Assert.ThrowsExactly<ArgumentException>(() => WorkspaceTextCodec.Decode(utf8WithMark, [TextEncoding.Utf8, TextEncoding.Utf8], out _));
	}

	[TestMethod]
	public void WorkspaceTextCodec_Decode_ByteOrderMarkDecidesBeforeAnyCandidate()
	{
		byte[] utf8WithMark = [0xEF, 0xBB, 0xBF, 0x63, 0x61, 0x66, 0xC3, 0xA9];
		byte[] utf16WithMark = [0xFF, 0xFE, 0x41, 0x00];

		string utf8Text = WorkspaceTextCodec.Decode(utf8WithMark, [ShiftJis, Windows1250], out TextFileFormat utf8Format);
		string utf16Text = WorkspaceTextCodec.Decode(utf16WithMark, [ShiftJis, Windows1250], out TextFileFormat utf16Format);

		Assert.AreEqual("caf\u00E9", utf8Text);
		Assert.AreEqual(new TextFileFormat(TextEncoding.Utf8, true, LineTerminatorStyle.None), utf8Format);
		Assert.AreEqual("A", utf16Text);
		Assert.AreEqual(new TextFileFormat(TextEncoding.Utf16LittleEndian, true, LineTerminatorStyle.None), utf16Format);
	}

	[TestMethod]
	public void WorkspaceTextCodec_Decode_InvalidBytesAfterAByteOrderMarkFailWithoutTryingACandidate()
	{
		// Windows-1252 would accept 0xC3 0x28, so a candidate that was tried after the mark would hide the failure.
		byte[] invalidAfterMark = [0xEF, 0xBB, 0xBF, 0xC3, 0x28];
		Assert.ThrowsExactly<DecoderFallbackException>(() => WorkspaceTextCodec.Decode(invalidAfterMark, [TextEncoding.Windows1252], out _));
	}

	[TestMethod]
	public void WorkspaceTextCodec_Decode_FirstCandidateThatDecodesStrictlyWinsAndIsRecorded()
	{
		byte[] ascii = "plain"u8.ToArray();

		// Bytes that both candidates accept belong to the first one listed.
		WorkspaceTextCodec.Decode(ascii, [Windows1250, TextEncoding.Utf8], out TextFileFormat firstIsWindows);
		WorkspaceTextCodec.Decode(ascii, [TextEncoding.Utf8, Windows1250], out TextFileFormat firstIsUtf8);

		Assert.AreEqual(Windows1250, firstIsWindows.Encoding);
		Assert.AreEqual(TextEncoding.Utf8, firstIsUtf8.Encoding);
		Assert.IsFalse(firstIsWindows.HasBom);
	}

	[TestMethod]
	public void WorkspaceTextCodec_Decode_InvalidUtf8FallsThroughToTheNextCandidate()
	{
		string windows = WorkspaceTextCodec.Decode(s_windows1250Bytes, [TextEncoding.Utf8, Windows1250], out TextFileFormat windowsFormat);
		string shiftJis = WorkspaceTextCodec.Decode(s_shiftJisBytes, [TextEncoding.Utf8, ShiftJis], out TextFileFormat shiftJisFormat);

		Assert.AreEqual("\u0159\u011B\u0161", windows);
		Assert.AreEqual(Windows1250, windowsFormat.Encoding);
		Assert.AreEqual("\u65E5\u672C\u8A9E", shiftJis);
		Assert.AreEqual(ShiftJis, shiftJisFormat.Encoding);
	}

	[TestMethod]
	public void WorkspaceTextCodec_Decode_ASingleByteCodePageRefusesItsUndefinedBytesSoTheNextCandidateDecodes()
	{
		// 0x81 is undefined in Windows-1250, whereas 0x81 0x40 is the ideographic space in Shift-JIS; the code page must not swallow the
		// byte it leaves undefined.
		byte[] bytes = [0x81, 0x40];

		string text = WorkspaceTextCodec.Decode(bytes, [Windows1250, ShiftJis], out TextFileFormat format);

		Assert.AreEqual(ShiftJis, format.Encoding);
		Assert.AreEqual("\u3000", text);
	}

	[TestMethod]
	public void WorkspaceTextCodec_Decode_EveryCandidateFailingIsOneDecoderFallbackFailureThatNamesThem()
	{
		// Invalid UTF-8, and an odd length that UTF-16 rejects too.
		byte[] bytes = [0xC3, 0x28, 0x41];

		DecoderFallbackException exception = Assert.ThrowsExactly<DecoderFallbackException>(() =>
			WorkspaceTextCodec.Decode(bytes, [TextEncoding.Utf8, TextEncoding.Utf16LittleEndian], out _)
		);

		Assert.Contains("UTF-8", exception.Message);
		Assert.Contains("UTF-16 LE", exception.Message);
		Assert.IsInstanceOfType<DecoderFallbackException>(exception.InnerException);
	}

	[TestMethod]
	public void WorkspaceTextCodec_RoundTripsEachPresetAndCodePageByteForByte()
	{
		(TextEncoding Encoding, string Text, byte[] Bytes)[] cases =
		[
			(TextEncoding.Utf8, "caf\u00E9 \u65E5", [0x63, 0x61, 0x66, 0xC3, 0xA9, 0x20, 0xE6, 0x97, 0xA5]),
			(TextEncoding.Utf16LittleEndian, "A\u00E9", [0x41, 0x00, 0xE9, 0x00]),
			(TextEncoding.Utf16BigEndian, "A\u00E9", [0x00, 0x41, 0x00, 0xE9]),
			(TextEncoding.Windows1252, "caf\u00E9", [0x63, 0x61, 0x66, 0xE9]),
			(Windows1250, "\u0159\u011B\u0161", s_windows1250Bytes),
			(ShiftJis, "\u65E5\u672C\u8A9E", s_shiftJisBytes),
		];

		foreach ((TextEncoding encoding, string text, byte[] bytes) in cases)
		{
			byte[] encoded = WorkspaceTextCodec.Encode(text, new TextFileFormat(encoding, false, LineTerminatorStyle.None));

			Assert.AreSequenceEqual(bytes, encoded);

			// A candidate list of that encoding alone decodes the bytes it wrote, and records the encoding.
			Assert.AreEqual(text, WorkspaceTextCodec.Decode(encoded, [encoding], out TextFileFormat decoded), encoding.DisplayName);
			Assert.AreEqual(encoding, decoded.Encoding, encoding.DisplayName);
		}
	}

	[TestMethod]
	public void WorkspaceTextCodec_Encode_RejectsACharacterThatTheCodePageCannotRepresent()
	{
		// The strict encoder refuses to write a replacement for a character outside the code page.
		Assert.ThrowsExactly<EncoderFallbackException>(() =>
			WorkspaceTextCodec.Encode("\u00E9", new TextFileFormat(ShiftJis, false, LineTerminatorStyle.None))
		);

		Assert.ThrowsExactly<EncoderFallbackException>(() =>
			WorkspaceTextCodec.Encode("\u0439", new TextFileFormat(Windows1250, false, LineTerminatorStyle.None))
		);
	}

	[TestMethod]
	public void WorkspaceTextCodec_Windows1250_RefusesItsUndefinedBytesAndCodePointsInBothDirections()
	{
		// The runtime decodes 0x81, 0x83, 0x88, 0x90 and 0x98 to the control character of the same value and encodes it back, so only the
		// codec's own check keeps a file with such a byte from decoding and a document with such a character from being written.
		foreach (byte undefined in new byte[] { 0x81, 0x83, 0x88, 0x90, 0x98 })
		{
			Assert.ThrowsExactly<DecoderFallbackException>(() => WorkspaceTextCodec.Decode([undefined], [Windows1250], out _));

			Assert.ThrowsExactly<EncoderFallbackException>(() =>
				WorkspaceTextCodec.Encode(((char)undefined).ToString(), new TextFileFormat(Windows1250, false, LineTerminatorStyle.None))
			);
		}

		// 0x80 is the euro sign in Windows-1250, so it is defined.
		Assert.AreEqual("\u20AC", WorkspaceTextCodec.Decode([0x80], [Windows1250], out _));
	}

	[TestMethod]
	public void WorkspaceTextCodec_ACodePageThatMapsTheControlRangeKeepsItsControlCharacters()
	{
		// ISO 8859-1 defines every byte, so the undefined-byte rule of the Windows code pages must not touch it.
		var latin1 = TextEncoding.FromCodePage(28591);

		Assert.AreEqual("\u0081", WorkspaceTextCodec.Decode([0x81], [latin1], out _));

		Assert.AreSequenceEqual(
			new byte[] { 0x81 },
			WorkspaceTextCodec.Encode("\u0081", new TextFileFormat(latin1, false, LineTerminatorStyle.None))
		);
	}

	[TestMethod]
	[DataRow(874, (byte)0xDB, 0xF8C1)]
	[DataRow(1253, (byte)0xAA, 0xF8F9)]
	[DataRow(1255, (byte)0xD9, 0xF88D)]
	[DataRow(1257, (byte)0xA1, 0xF8FC)]
	public void WorkspaceTextCodec_WindowsSingleByteCodePages_RefuseTheBytesThatDecodeToPrivateUseInBothDirections(
		int codePage,
		byte privateUseByte,
		int privateUseCharacter
	)
	{
		// The runtime decodes these undefined bytes to a private-use character and encodes it back, so only the codec's own check refuses them.
		var encoding = TextEncoding.FromCodePage(codePage);

		Assert.ThrowsExactly<DecoderFallbackException>(() => WorkspaceTextCodec.Decode([privateUseByte], [encoding], out _));

		Assert.ThrowsExactly<EncoderFallbackException>(() =>
			WorkspaceTextCodec.Encode(((char)privateUseCharacter).ToString(), new TextFileFormat(encoding, false, LineTerminatorStyle.None))
		);
	}

	[TestMethod]
	public void WorkspaceTextCodec_AMacCodePageKeepsTheRuntimesPrivateUseMapping()
	{
		// The undefined-byte rule covers the Windows single-byte code pages only: the Mac pages map 0xF0 to the Apple logo on purpose.
		var macRoman = TextEncoding.FromCodePage(10000);

		Assert.AreEqual("\uF8FF", WorkspaceTextCodec.Decode([0xF0], [macRoman], out _));

		Assert.AreSequenceEqual(
			new byte[] { 0xF0 },
			WorkspaceTextCodec.Encode("\uF8FF", new TextFileFormat(macRoman, false, LineTerminatorStyle.None))
		);
	}

	[TestMethod]
	public void WorkspaceTextCodec_AnEncodingTheProcessCannotResolve_ThrowsInBothDirectionsAndNamesTheProvider()
	{
		// No provider serves this code page, whichever providers the test assembly registered.
		var unresolvable = new TextEncoding(4242);

		// The UTF-8 candidate decodes the first bytes, so the unresolvable candidate is never needed; the invalid bytes need it.
		Assert.AreEqual("A", WorkspaceTextCodec.Decode([0x41], [TextEncoding.Utf8, unresolvable], out _));

		EncodingUnavailableException decode = Assert.ThrowsExactly<EncodingUnavailableException>(() =>
			WorkspaceTextCodec.Decode([0xC3, 0x28], [TextEncoding.Utf8, unresolvable], out _)
		);

		EncodingUnavailableException encode = Assert.ThrowsExactly<EncodingUnavailableException>(() =>
			WorkspaceTextCodec.Encode("x", new TextFileFormat(unresolvable, false, LineTerminatorStyle.None))
		);

		Assert.Contains("CodePagesEncodingProvider", decode.Message);
		Assert.Contains("CodePagesEncodingProvider", encode.Message);
		Assert.AreEqual("Code page 4242", unresolvable.DisplayName);
	}
}
