using System.Text;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Workspace.FileSystem;

/// <summary>Encodes and decodes workspace text files and detects their format metadata.</summary>
/// <remarks>
/// <see cref="LocalWorkspaceFileSystem"/> returns raw bytes that the document store decodes here. Both directions are strict:
/// an invalid sequence fails the decode, and a character the encoding cannot represent fails the encode. A write encodes
/// with the document's <see cref="TextFileFormat"/>. A UTF-32 byte-order mark is rejected, so a UTF-32 file is a decode failure.
/// </remarks>
public static class WorkspaceTextCodec
{
	private static readonly byte[] s_utf8Bom = [0xEF, 0xBB, 0xBF];
	private static readonly byte[] s_utf16LittleEndianBom = [0xFF, 0xFE];
	private static readonly byte[] s_utf16BigEndianBom = [0xFE, 0xFF];
	private static readonly byte[] s_utf32LittleEndianBom = [0xFF, 0xFE, 0x00, 0x00];
	private static readonly byte[] s_utf32BigEndianBom = [0x00, 0x00, 0xFE, 0xFF];

	/// <summary>Decodes bytes, detects their text format, and records the line terminator style.</summary>
	/// <param name="bytes">The encoded bytes to decode.</param>
	/// <param name="noBomEncodings">The candidate encodings, in order of preference, for bytes that carry no recognized byte-order mark.</param>
	/// <param name="fileFormat">Receives the detected encoding, byte-order mark, and line terminator style.</param>
	/// <returns>The decoded text.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="noBomEncodings"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="noBomEncodings"/> is empty or lists an encoding twice.</exception>
	/// <exception cref="DecoderFallbackException">
	/// The bytes contain a UTF-32 mark, a sequence that is invalid for the encoding that the mark names, or a sequence that is invalid
	/// for every candidate.
	/// </exception>
	/// <exception cref="NotSupportedException">
	/// A candidate, or the encoding that a mark names, cannot be resolved by the process, typically because no code-page provider is registered;
	/// the message names the provider. Registering the provider recovers, because nothing is cached for the failure.
	/// </exception>
	/// <remarks>
	/// <para>
	/// A recognized UTF-8 or UTF-16 byte-order mark decides the encoding, whatever the candidates are, and an invalid sequence after the mark fails
	/// the decode instead of trying a candidate. The UTF-32 check runs first, so a UTF-16 little-endian mark followed by a NUL character is rejected
	/// as UTF-32 instead of decoding that character.
	/// </para>
	/// <para>
	/// Bytes without a mark decode with the first candidate that accepts them, and <paramref name="fileFormat"/> records that candidate.
	/// A candidate accepts bytes only when its strict decoder does, so a UTF-8 candidate before a code page falls
	/// through for a file that is not valid UTF-8, whereas a single-byte code page accepts every byte sequence except one that holds a byte it leaves
	/// undefined (see <see cref="TextEncoding"/>), so it ends the search.
	/// </para>
	/// </remarks>
	public static string Decode(ReadOnlySpan<byte> bytes, IReadOnlyList<TextEncoding> noBomEncodings, out TextFileFormat fileFormat)
	{
		ArgumentNullException.ThrowIfNull(noBomEncodings);
		EnsureCandidates(noBomEncodings, nameof(noBomEncodings));

		if (bytes.StartsWith(s_utf32LittleEndianBom) || bytes.StartsWith(s_utf32BigEndianBom))
			throw new DecoderFallbackException("UTF-32 input is not supported.");

		if (TryReadByteOrderMark(bytes, out TextEncoding markedEncoding, out int markLength))
		{
			string markedText = DecodeStrict(markedEncoding, bytes[markLength..]);
			fileFormat = new TextFileFormat(markedEncoding, HasBom: true, LineTerminatorDetector.Detect(markedText));
			return markedText;
		}

		DecoderFallbackException? lastFailure = null;

		for (int index = 0; index < noBomEncodings.Count; index++)
		{
			TextEncoding candidate = noBomEncodings[index];

			try
			{
				string text = DecodeStrict(candidate, bytes);
				fileFormat = new TextFileFormat(candidate, HasBom: false, LineTerminatorDetector.Detect(text));
				return text;
			}
			catch (DecoderFallbackException exception)
			{
				lastFailure = exception;
			}
		}

		string names = string.Join(", ", noBomEncodings.Select(candidate => candidate.DisplayName));
		throw new DecoderFallbackException($"The bytes are not valid in any candidate encoding ({names}).", lastFailure);
	}

	/// <summary>Encodes text using the specified encoding and byte-order-mark setting.</summary>
	/// <param name="content">The text to encode.</param>
	/// <param name="fileFormat">The encoding, byte-order mark, and line terminator style to associate with the output.</param>
	/// <returns>The encoded bytes.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The format asks for a byte-order mark that its encoding has none of.</exception>
	/// <exception cref="EncoderFallbackException">
	/// The content contains a character that the format's encoding cannot represent, such as an unpaired surrogate, a character outside a code page,
	/// or a character that a Windows single-byte code page leaves undefined (see <see cref="TextEncoding"/>).
	/// </exception>
	/// <exception cref="NotSupportedException">
	/// The format's encoding cannot be resolved by the process, typically because no code-page provider is registered; the message names
	/// the provider.
	/// </exception>
	/// <remarks>
	/// The content is encoded as supplied; <see cref="TextFileFormat.LineTerminatorStyle"/> is metadata and does not normalize line terminators.
	/// Only UTF-8 and UTF-16 have a byte-order mark. The characters that a Windows single-byte code page leaves undefined are rejected
	/// because their byte values are rejected again when the file is decoded.
	/// </remarks>
	public static byte[] Encode(string content, TextFileFormat fileFormat)
	{
		ArgumentNullException.ThrowIfNull(content);

		EnsureEncodable(fileFormat, nameof(fileFormat));

		TextEncoding encoding = fileFormat.Encoding;
		int undefinedIndex = encoding.IndexOfUndefinedCharacter(content);

		if (undefinedIndex >= 0)
		{
			throw new EncoderFallbackException(
				$"The content contains the control character U+{(int)content[undefinedIndex]:X4}, which {encoding.DisplayName} cannot round-trip."
			);
		}

		Encoding encoder = encoding.Strict;
		byte[]? preamble = fileFormat.HasBom ? GetPreamble(encoding) : null;

		if (preamble is null)
			return encoder.GetBytes(content);

		// The body is encoded directly into the result buffer after the preamble, so the bytes are
		// copied once instead of encoding into a temporary array and copying it again.
		byte[] result = new byte[preamble.Length + encoder.GetByteCount(content)];

		Buffer.BlockCopy(src: preamble, srcOffset: 0, dst: result, dstOffset: 0, count: preamble.Length);
		encoder.GetBytes(s: content, charIndex: 0, charCount: content.Length, bytes: result, byteIndex: preamble.Length);
		return result;
	}

	// The shared format validation for every boundary that accepts a format: a byte-order mark can be
	// written only for an encoding that has one. Callers pass the parameter name of the argument they
	// accepted the format through, so the rule has one home and one message.
	internal static void EnsureEncodable(TextFileFormat fileFormat, string paramName)
	{
		if (fileFormat.HasBom && GetPreamble(fileFormat.Encoding) is null)
			throw new ArgumentException($"{fileFormat.Encoding.DisplayName} does not support a BOM.", paramName);
	}

	// The shared validation of a candidate list, for the decode and for the open options that hold the list.
	// Both callers reject a null list before they call it.
	internal static void EnsureCandidates(IReadOnlyList<TextEncoding> candidates, string paramName)
	{
		if (candidates.Count == 0)
			throw new ArgumentException("At least one candidate encoding is required.", paramName);

		for (int index = 1; index < candidates.Count; index++)
		{
			for (int earlier = 0; earlier < index; earlier++)
			{
				if (candidates[index] == candidates[earlier])
					throw new ArgumentException($"The candidate encoding {candidates[index].DisplayName} is listed twice.", paramName);
			}
		}
	}

	private static string DecodeStrict(TextEncoding encoding, ReadOnlySpan<byte> content)
	{
		int undefinedIndex = encoding.IndexOfUndefinedByte(content);

		if (undefinedIndex >= 0)
			throw new DecoderFallbackException($"Undefined {encoding.DisplayName} byte 0x{content[undefinedIndex]:X2}.");

		return encoding.Strict.GetString(content);
	}

	private static bool TryReadByteOrderMark(ReadOnlySpan<byte> bytes, out TextEncoding encoding, out int markLength)
	{
		if (bytes.StartsWith(s_utf8Bom))
		{
			encoding = TextEncoding.Utf8;
			markLength = s_utf8Bom.Length;
			return true;
		}

		if (bytes.StartsWith(s_utf16LittleEndianBom))
		{
			encoding = TextEncoding.Utf16LittleEndian;
			markLength = s_utf16LittleEndianBom.Length;
			return true;
		}

		if (bytes.StartsWith(s_utf16BigEndianBom))
		{
			encoding = TextEncoding.Utf16BigEndian;
			markLength = s_utf16BigEndianBom.Length;
			return true;
		}

		encoding = default;
		markLength = 0;
		return false;
	}

	private static byte[]? GetPreamble(TextEncoding encoding)
	{
		if (encoding == TextEncoding.Utf8)
			return s_utf8Bom;

		if (encoding == TextEncoding.Utf16LittleEndian)
			return s_utf16LittleEndianBom;

		return encoding == TextEncoding.Utf16BigEndian ? s_utf16BigEndianBom : null;
	}
}
