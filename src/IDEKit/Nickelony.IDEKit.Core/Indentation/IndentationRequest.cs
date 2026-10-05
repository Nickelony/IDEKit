namespace Nickelony.IDEKit.Core.Indentation;

/// <summary>Describes the inputs required to compute the desired indentation for one line.</summary>
/// <remarks>
/// Carries the two line texts, the indentation unit, and the smart-indent flag, so a policy derives what it needs from the same primitives
/// as the consumer's strategy. <see cref="CurrentLineText"/> includes its leading whitespace; a <see langword="default"/> instance carries
/// <see langword="null"/> text members.
/// </remarks>
public readonly record struct IndentationRequest(string PreviousLineText, string CurrentLineText, string IndentationUnit, bool UseSmartIndent);
