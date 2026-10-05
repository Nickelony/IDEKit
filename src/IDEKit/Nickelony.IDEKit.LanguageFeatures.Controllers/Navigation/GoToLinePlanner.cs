using Nickelony.IDEKit.Core.Navigation;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Navigation;

/// <summary>Reads the text of the go to line mode, <c>line</c> or <c>line:column</c>, and clamps it to a document.</summary>
/// <remarks>
/// <para>
/// <see cref="LineColumnQuery.TryParse"/> defines what the text may hold. The line clamps to the document. A column clamps to the line,
/// from the column before its first character to the column after its last one, and a query without a column puts the caret at the start of the line.
/// </para>
/// <para>
/// The planner holds no state and reads only its arguments.
/// </para>
/// </remarks>
internal static class GoToLinePlanner
{
	/// <summary>Plans where the caret goes for a query.</summary>
	/// <param name="query">The text typed after the prefix of the mode.</param>
	/// <param name="lineCount">The number of lines of the document, at least one.</param>
	/// <param name="getLineLength">Gets the length of a one-based line without its line terminator.</param>
	/// <returns>The clamped position, or <see langword="null"/> when the query is no position.</returns>
	internal static GoToLinePlan? Plan(string query, int lineCount, Func<int, int> getLineLength)
	{
		if (!LineColumnQuery.TryParse(query, out LineColumnQuery parsed))
			return null;

		int clampedLine = Math.Clamp(parsed.Line, 1, Math.Max(1, lineCount));

		if (parsed.Column is not { } requestedColumn)
			return new GoToLinePlan(clampedLine, Column: 1, HasColumn: false);

		int clampedColumn = Math.Clamp(requestedColumn, 1, getLineLength(clampedLine) + 1);

		return new GoToLinePlan(clampedLine, clampedColumn, HasColumn: true);
	}
}
