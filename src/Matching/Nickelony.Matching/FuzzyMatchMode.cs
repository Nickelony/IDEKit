namespace Nickelony.Matching;

/// <summary>Specifies how a <see cref="FuzzyMatcher"/> reads the label and the description of a candidate.</summary>
public enum FuzzyMatchMode
{
	/// <summary>The label is a name, such as a command title, and the description is an optional qualifier.</summary>
	/// <remarks>
	/// The query matches the label first; when it does not, the description and the label are matched as one text with a space between them.
	/// </remarks>
	Text = 0,

	/// <summary>The label is a file name and the description is the folder path that holds it.</summary>
	/// <remarks>
	/// A query without a path separator matches the file name first, so a file name outranks a folder. A query with a <c>/</c> or a <c>\</c> matches
	/// the folder path and the file name as one path, in which the two separators are equal.
	/// </remarks>
	Path = 1,
}
