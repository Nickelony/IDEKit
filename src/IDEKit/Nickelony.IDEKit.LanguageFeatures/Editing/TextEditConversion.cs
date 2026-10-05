using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.LanguageFeatures.Editing;

/// <summary>Converts the line and character edits of a language server into the validated offset batch of one text.</summary>
/// <remarks>
/// The edits address the text by line and character. They become offset edits against that text, and <see cref="TextEditKernel"/> validates
/// them as one batch, so a batch with a reversed range or two conflicting edits is rejected in full. A position outside the text clamps to its end.
/// </remarks>
public static class TextEditConversion
{
	/// <summary>Converts the edits that address <paramref name="text"/>.</summary>
	/// <param name="text">The text the edits address.</param>
	/// <param name="edits">The edits to convert; an empty list yields an empty batch.</param>
	/// <param name="prepared">
	/// Receives the validated batch, or an empty batch unless the method returns <see cref="TextEditConversionOutcome.Converted"/>.
	/// </param>
	/// <returns>How the conversion ended.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="edits"/> is <see langword="null"/>.</exception>
	public static TextEditConversionOutcome Convert(string text, IReadOnlyList<TextEdit> edits, out PreparedTextEdits prepared)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(edits);

		prepared = TextEditKernel.Prepare([]).Edits;

		if (edits.Count == 0)
			return TextEditConversionOutcome.Converted;

		var lineMap = TextLineMap.Build(text);
		var inputs = new List<TextIncrementalEdit>(edits.Count);

		foreach (TextEdit edit in edits)
		{
			if (!lineMap.TryGetOffsets(edit.Range, out TextRange range))
				return TextEditConversionOutcome.InvalidRange;

			inputs.Add(new TextIncrementalEdit(range, edit.NewText));
		}

		TextEditPreparationResult result = TextEditKernel.Prepare(new StringTextSnapshot(text), inputs);

		if (!result.Succeeded)
			return TextEditConversionOutcome.Conflict;

		prepared = result.Edits;
		return TextEditConversionOutcome.Converted;
	}
}
