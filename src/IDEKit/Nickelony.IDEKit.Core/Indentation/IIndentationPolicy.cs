namespace Nickelony.IDEKit.Core.Indentation;

/// <summary>
/// Computes the language-specific indentation decisions for the consumer's indentation strategy.
/// </summary>
/// <remarks>
/// <para>
/// Implementations are supplied by the host or language layer; the caller builds the context and
/// applies the returned indentation.
/// </para>
/// <para>
/// The policy's contract:
/// </para>
/// <list type="bullet">
/// <item>The returned string replaces the line's leading whitespace entirely; it is not appended
/// to the current indentation.</item>
/// <item>Returning the current leading whitespace is the "no change" signal; the caller then
/// performs no edit.</item>
/// <item><see cref="IndentationContext.CurrentLineText"/> includes the current leading whitespace;
/// use <see cref="IndentationOperations.GetLeadingWhitespace(string)"/> when the policy needs to
/// inspect or preserve it.</item>
/// <item>When <see cref="IndentationContext.UseSmartIndent"/> is <see langword="false"/>, return
/// the line's current leading whitespace instead of applying language-aware rules, so the
/// caller's smart-indentation setting is honored.</item>
/// <item>A document's first line is never presented to a policy, because there is no previous
/// line to derive indentation from.</item>
/// </list>
/// </remarks>
public interface IIndentationPolicy
{
	/// <summary>
	/// Computes the indentation that should be applied to the current line.
	/// </summary>
	/// <param name="context">The indentation inputs for the current line.</param>
	/// <returns>The desired leading whitespace for the current line.</returns>
	string GetDesiredIndentation(in IndentationContext context);
}
