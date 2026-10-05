using System.Buffers;
using System.Collections.Concurrent;
using System.Text;

namespace Nickelony.IDEKit.Workspace.FileSystem;

/// <summary>Identifies the text encoding that decodes or persists a workspace document, by code page.</summary>
/// <remarks>
/// <para>
/// Two values are equal when their code pages are, and the default value is UTF-8, so a default <see cref="TextFileFormat"/> reads and writes UTF-8.
/// UTF-32 and UTF-7 are not text-file encodings, and <see cref="FromCodePage"/> does not accept them.
/// </para>
/// <para>
/// An encoding that the runtime does not build in, such as Windows-1252 or Shift-JIS, needs the host
/// to call <c>Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)</c> once at startup. The library never registers a provider,
/// because that is a process-wide side effect. <see cref="FromCodePage"/> throws when the process cannot resolve the encoding, whereas a preset
/// resolves at its first decode or encode, where the failure is the <c>EncodingUnavailable</c> failure code of the operation, so a host can name
/// <see cref="Windows1252"/> before it registers the provider. Nothing is cached for an encoding that failed to resolve, so the operation succeeds
/// once the host registers the provider.
/// </para>
/// <para>
/// The Windows single-byte code pages (874 and 1250 to 1258) have one more rule. The runtime decodes a byte that such a page
/// leaves undefined to a control character of the same value or to a private-use character, and encodes that character back to the byte,
/// so neither direction would fail. Both directions refuse the bytes in 0x80 to 0x9F that decode to themselves and the bytes that decode
/// to U+E000 to U+F8FF. The DOS, Mac and ISO code pages, and the multi-byte code pages, keep the runtime's own behavior.
/// </para>
/// </remarks>
public readonly struct TextEncoding : IEquatable<TextEncoding>
{
	private const int Utf8CodePage = 65001;
	private const int Utf32LittleEndianCodePage = 12000;
	private const int Utf32BigEndianCodePage = 12001;
	private const int Utf7CodePage = 65000;

	private const string ProviderHint =
		"The encoding needs a registered code-page provider: call Encoding.RegisterProvider(CodePagesEncodingProvider.Instance) during startup.";

	// The resolved data of each code page. A failed resolution adds nothing, so a host that registers the provider later recovers.
	private static readonly ConcurrentDictionary<int, Profile> s_profiles = new();

	// Zero stands for UTF-8, so default(TextEncoding), and with it a default TextFileFormat, is UTF-8.
	private readonly int _codePage;

	internal TextEncoding(int codePage) => _codePage = codePage == Utf8CodePage ? 0 : codePage;

	/// <summary>UTF-8.</summary>
	public static TextEncoding Utf8 => default;

	/// <summary>UTF-16 in little-endian byte order.</summary>
	public static TextEncoding Utf16LittleEndian => new(1200);

	/// <summary>UTF-16 in big-endian byte order.</summary>
	public static TextEncoding Utf16BigEndian => new(1201);

	/// <summary>Windows-1252, the Western European code page.</summary>
	/// <remarks>
	/// Using it requires the code-page provider that the remarks of <see cref="TextEncoding"/> describe, and the undefined-byte rule of those remarks
	/// applies to it.
	/// </remarks>
	public static TextEncoding Windows1252 => new(1252);

	/// <summary>The code page number, such as 65001 for UTF-8.</summary>
	public int CodePage => _codePage == 0 ? Utf8CodePage : _codePage;

	/// <summary>The name that a host shows to a user, such as "UTF-8", "Windows-1250" or "Japanese (Shift-JIS)".</summary>
	/// <remarks>
	/// A preset and a Windows single-byte code page (the set in the remarks of <see cref="TextEncoding"/>)
	/// have a fixed name, "Windows-" and the code page number, so the Windows pages read alike. Any other encoding shows the description
	/// that the runtime gives it, or its code page number when the process has not resolved it.
	/// </remarks>
	public string DisplayName
	{
		get
		{
			if (this == Utf8)
				return "UTF-8";

			if (this == Utf16LittleEndian)
				return "UTF-16 LE";

			if (this == Utf16BigEndian)
				return "UTF-16 BE";

			if (IsWindowsSingleByte(CodePage))
				return $"Windows-{CodePage}";

			return s_profiles.TryGetValue(CodePage, out Profile? profile) ? profile.Strict.EncodingName : $"Code page {CodePage}";
		}
	}

	/// <summary>The encoding that decodes and encodes strictly: it throws instead of substituting a replacement character.</summary>
	internal Encoding Strict => GetProfile().Strict;

	/// <summary>Creates the encoding of a code page.</summary>
	/// <param name="codePage">The code page number, such as 932 for Shift-JIS.</param>
	/// <returns>The encoding; the preset when the code page is one of the presets.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="codePage"/> is zero, negative, or above 65535.</exception>
	/// <exception cref="ArgumentException"><paramref name="codePage"/> is a UTF-32 or UTF-7 code page.</exception>
	/// <exception cref="NotSupportedException">
	/// The process cannot resolve the code page, because the runtime does not know it or no registered code-page
	/// provider serves it; the message names the provider.
	/// </exception>
	public static TextEncoding FromCodePage(int codePage)
	{
		// Zero would select the code page of the machine, which differs between machines.
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(codePage);

		if (IsUnsupportedFamily(codePage))
			throw new ArgumentException($"Code page {codePage} is not a text-file encoding.", nameof(codePage));

		return From(GetEncoding(codePage));
	}

	internal int IndexOfUndefinedByte(ReadOnlySpan<byte> bytes) => GetProfile().UndefinedBytes is { } undefined ? bytes.IndexOfAny(undefined) : -1;

	internal int IndexOfUndefinedCharacter(ReadOnlySpan<char> text) =>
		GetProfile().UndefinedCharacters is { } undefined ? text.IndexOfAny(undefined) : -1;

	/// <inheritdoc/>
	public bool Equals(TextEncoding other) => _codePage == other._codePage;

	/// <inheritdoc/>
	public override bool Equals(object? obj) => obj is TextEncoding other && Equals(other);

	/// <inheritdoc/>
	public override int GetHashCode() => CodePage;

	/// <inheritdoc/>
	public override string ToString() => DisplayName;

	/// <summary>Compares two encodings by code page.</summary>
	public static bool operator ==(TextEncoding left, TextEncoding right) => left.Equals(right);

	/// <summary>Compares two encodings by code page.</summary>
	public static bool operator !=(TextEncoding left, TextEncoding right) => !left.Equals(right);

	// The runtime reports a code page it does not serve as NotSupportedException, and a few small numbers as ArgumentException; both mean
	// that this process cannot resolve the code page, whereas an out-of-range number stays the caller's argument error.
	private static Encoding GetEncoding(int codePage)
	{
		try
		{
			return Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
		}
		catch (Exception exception) when (exception is NotSupportedException or (ArgumentException and not ArgumentOutOfRangeException))
		{
			throw new EncodingUnavailableException($"No data is available for code page {codePage}. {ProviderHint}", exception);
		}
	}

	// The Windows single-byte code pages: 874 and 1250 to 1258.
	private static bool IsWindowsSingleByte(int codePage) => codePage is 874 or (>= 1250 and <= 1258);

	private static bool IsUnsupportedFamily(int codePage) => codePage is Utf32LittleEndianCodePage or Utf32BigEndianCodePage or Utf7CodePage;

	private static TextEncoding From(Encoding strict)
	{
		s_profiles.GetOrAdd(strict.CodePage, static (_, encoding) => Profile.Create(encoding), strict);
		return new TextEncoding(strict.CodePage);
	}

	private Profile GetProfile() => s_profiles.GetOrAdd(CodePage, static codePage => Profile.Create(GetEncoding(codePage)));

	// The strict encoding of a code page and the bytes and characters that the runtime passes through although the code page leaves them undefined.
	private sealed class Profile
	{
		private Profile(Encoding strict, SearchValues<byte>? undefinedBytes, SearchValues<char>? undefinedCharacters)
		{
			Strict = strict;
			UndefinedBytes = undefinedBytes;
			UndefinedCharacters = undefinedCharacters;
		}

		internal Encoding Strict { get; }

		internal SearchValues<byte>? UndefinedBytes { get; }

		internal SearchValues<char>? UndefinedCharacters { get; }

		// The set of pages and the rule are described in the remarks of TextEncoding. A Windows single-byte code page maps every defined byte
		// of 0x80-0x9F to a printable character, so a byte there that decodes to itself is undefined, and the table also decodes some undefined
		// bytes elsewhere to a private-use character.
		internal static Profile Create(Encoding strict)
		{
			if (!IsWindowsSingleByte(strict.CodePage))
				return new Profile(strict, undefinedBytes: null, undefinedCharacters: null);

			List<byte> undefinedBytes = [];
			List<char> undefinedCharacters = [];

			for (int value = 0; value <= 0xFF; value++)
			{
				string text;

				try
				{
					text = strict.GetString([(byte)value]);
				}
				catch (DecoderFallbackException)
				{
					// The strict decoder already rejects this byte.
					continue;
				}

				if (value is >= 0x80 and <= 0x9F && text.Length == 1 && text[0] == (char)value)
				{
					undefinedBytes.Add((byte)value);
					undefinedCharacters.Add((char)value);
				}
				else if (text.Any(IsPrivateUse))
				{
					undefinedBytes.Add((byte)value);
					undefinedCharacters.AddRange(text.Where(IsPrivateUse));
				}
			}

			if (undefinedBytes.Count == 0)
				return new Profile(strict, undefinedBytes: null, undefinedCharacters: null);

			return new Profile(strict, SearchValues.Create(undefinedBytes.ToArray()), SearchValues.Create(undefinedCharacters.ToArray()));
		}

		private static bool IsPrivateUse(char character) => character is >= (char)0xE000 and <= (char)0xF8FF;
	}
}
