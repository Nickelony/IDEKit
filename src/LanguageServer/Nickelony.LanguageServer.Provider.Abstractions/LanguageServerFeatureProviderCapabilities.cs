namespace Nickelony.LanguageServer.Provider.Abstractions;

/// <summary>Describes which features the connected language server advertises, as one immutable snapshot.</summary>
/// <remarks>
/// <para>
/// A flag is <see langword="false"/> before the first successful start, while the provider is not available, and after disposal, so a host reads
/// the snapshot again after <see cref="ILanguageServerCapabilitiesSource.CapabilitiesChanged"/> instead of treating <see langword="false"/> as final.
/// A feature the server does not advertise returns its documented fallback rather than failing, and the provider still sends hover,
/// definition and signature-help requests when their flag is <see langword="false"/>, so a flag gates presentation, not the request.
/// </para>
/// <para>
/// The snapshot carries the features a host decides on. It adds no flag for completion or semantic tokens, because a host
/// with a completion or semantic-token source shows both unconditionally and an unsupported call returns its fallback. The trigger characters
/// tell a host which typed characters open completion and signature help, which differ by server. The concrete <c>LanguageServerFeatureProvider</c>
/// also answers them through <see cref="IDEKit.LanguageFeatures.Completion.ICompletionTriggerCharacters"/>
/// and <see cref="IDEKit.LanguageFeatures.SignatureHelp.ISignatureHelpTriggerCharacters"/>, which the editor controllers read.
/// A decorator or a fake of <see cref="ILanguageServerFeatureProvider"/> answers neither unless it implements them itself, so it opens no completion
/// on the server's characters.
/// </para>
/// </remarks>
public sealed record LanguageServerFeatureProviderCapabilities
{
	/// <summary>The snapshot of a provider that is not available: no feature is advertised.</summary>
	public static LanguageServerFeatureProviderCapabilities None { get; } = new();

	/// <summary>Indicates whether the server advertises hover.</summary>
	public bool SupportsHover { get; init; }

	/// <summary>Indicates whether the server advertises definition lookup.</summary>
	public bool SupportsDefinition { get; init; }

	/// <summary>Indicates whether the server advertises signature help.</summary>
	public bool SupportsSignatureHelp { get; init; }

	/// <summary>Indicates whether the server advertises find references.</summary>
	public bool SupportsReferences { get; init; }

	/// <summary>Indicates whether the server advertises rename.</summary>
	public bool SupportsRename { get; init; }

	/// <summary>The characters that the server opens completion on when they are typed.</summary>
	public IReadOnlyList<string> CompletionTriggerCharacters { get; init; } = [];

	/// <summary>The characters that the server opens signature help on when they are typed.</summary>
	public IReadOnlyList<string> SignatureHelpTriggerCharacters { get; init; } = [];

	/// <summary>The characters that the server updates signature help on when it is already open.</summary>
	public IReadOnlyList<string> SignatureHelpRetriggerCharacters { get; init; } = [];
}
