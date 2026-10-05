namespace Nickelony.IDEKit.Core.Indentation;

/// <summary>Computes the language-specific indentation decisions for the consumer's indentation strategy.</summary>
/// <remarks>
/// Implementations are supplied by the host or language layer, and the caller applies
/// the returned indentation. The returned string replaces the line's leading whitespace entirely, and returning the current leading whitespace
/// signals "no change". With <see cref="IndentationRequest.UseSmartIndent"/> <see langword="false"/>, return the current leading whitespace
/// instead of applying language-aware rules; a document's first line is never presented to a policy.
/// </remarks>
public interface IIndentationPolicy
{
	/// <summary>Computes the indentation that should be applied to the current line.</summary>
	string GetDesiredIndentation(in IndentationRequest request);
}
