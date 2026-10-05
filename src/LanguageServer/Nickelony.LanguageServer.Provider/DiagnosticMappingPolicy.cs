using Nickelony.IDEKit.Core.Identifiers;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Supplies the language-specific parts of diagnostic mapping.</summary>
/// <remarks>
/// <para>
/// The mapping pipeline is protocol-level and shared; only the language-specific rules vary, so a provider
/// overrides <c>LanguageServerProfile.DiagnosticMappingPolicy</c> with a policy of its own and the pipeline stays in the framework. Build the policy
/// by overriding individual values of <see cref="Default"/> with a <c>with</c> expression or an object initializer.
/// </para>
/// <para>
/// Record equality compares <see cref="WordCharacters"/> by reference.
/// </para>
/// </remarks>
public sealed record DiagnosticMappingPolicy
{
	private string _unknownMessage = "Unknown diagnostic.";
	private IdentifierCharacterPolicy _wordCharacters = IdentifierCharacterPolicy.Default;

	/// <summary>The language-neutral policy: a fixed fallback message and the identifier word rule (letters, digits, and <c>_</c>).</summary>
	public static DiagnosticMappingPolicy Default { get; } = new();

	/// <summary>The displayed message for a diagnostic whose server message is blank, which defaults to <c>"Unknown diagnostic."</c>.</summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The value is empty or whitespace-only.</exception>
	public string UnknownMessage
	{
		get => _unknownMessage;
		init
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(value);
			_unknownMessage = value;
		}
	}

	/// <summary>
	/// The character rules of the word that a malformed range
	/// falls back to, which defaults to <see cref="IdentifierCharacterPolicy.Default"/> (letters, digits, and <c>_</c>).
	/// </summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
	/// <remarks>
	/// The part-character rule runs on the thread that delivers the diagnostics payload, so it must be thread-safe.
	/// </remarks>
	public IdentifierCharacterPolicy WordCharacters
	{
		get => _wordCharacters;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			_wordCharacters = value;
		}
	}
}
