namespace Nickelony.LanguageServer.Provider;

/// <summary>Supplies the per-language deltas for the shared client-capability payload.</summary>
/// <remarks>
/// <para>
/// Override individual values on <see cref="Default"/> with a <c>with</c> expression or an object initializer, then pass the instance
/// to <see cref="LanguageServerClientCapabilitiesBuilder.Build"/>.
/// </para>
/// <para>
/// Record equality compares <see cref="CodeActionKinds"/> and <see cref="ExtraSemanticTokenModifiers"/> by reference, and each initializer stores
/// its own copy of the list, so two instances that each set a list are not equal even when both were built from the same source list.
/// </para>
/// </remarks>
public sealed record LanguageServerClientCapabilitiesOptions
{
	private IReadOnlyList<string> _codeActionKinds = [];
	private IReadOnlyList<string> _extraSemanticTokenModifiers = [];

	/// <summary>
	/// The shared default options instance, which advertises no code-action kinds, no pull diagnostics, and no extra semantic-token modifiers.
	/// </summary>
	public static LanguageServerClientCapabilitiesOptions Default { get; } = new();

	/// <summary>The code-action kinds advertised to the language server, which defaults to an empty list.</summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The value holds a <see langword="null"/> entry.</exception>
	/// <remarks>
	/// The empty string is a valid entry: it is the LSP's empty code-action kind. The advertised set only bounds which literal code actions
	/// the server may return.
	/// </remarks>
	public IReadOnlyList<string> CodeActionKinds
	{
		get => _codeActionKinds;
		init
		{
			ArgumentNullException.ThrowIfNull(value);

			if (value.Any(static kind => kind is null))
				throw new ArgumentException("The code-action kinds must not contain a null entry.", nameof(value));

			_codeActionKinds = [.. value];
		}
	}

	/// <summary>
	/// Indicates whether the server serves diagnostics through <c>textDocument/diagnostic</c>, which defaults to <see langword="false"/>.
	/// </summary>
	/// <remarks>
	/// When <see langword="true"/>, the payload also advertises the pull capability and its workspace refresh, which makes the server prefer pull
	/// over push diagnostics.
	/// </remarks>
	public bool SupportsPullDiagnostics { get; init; }

	/// <summary>The non-standard semantic-token modifiers the language adds to the shared vocabulary, which defaults to an empty list.</summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The value holds a <see langword="null"/>, empty, or whitespace-only entry.</exception>
	public IReadOnlyList<string> ExtraSemanticTokenModifiers
	{
		get => _extraSemanticTokenModifiers;
		init
		{
			ArgumentNullException.ThrowIfNull(value);

			if (value.Any(static modifier => string.IsNullOrWhiteSpace(modifier)))
			{
				throw new ArgumentException(
					"The extra semantic-token modifiers must not contain a null, empty, or whitespace-only entry.",
					nameof(value)
				);
			}

			_extraSemanticTokenModifiers = [.. value];
		}
	}
}
