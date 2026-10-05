namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Navigation;

/// <summary>The position that a query of the go to line mode names after the clamp.</summary>
/// <param name="LineNumber">The one-based line.</param>
/// <param name="Column">The one-based column, which is <c>1</c> when the query named none.</param>
/// <param name="HasColumn">Whether the query named a column.</param>
internal readonly record struct GoToLinePlan(int LineNumber, int Column, bool HasColumn);
