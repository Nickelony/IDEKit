using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Provider;

/// <summary>The document a request is for, which the request pipeline hands to the delegates of a request descriptor.</summary>
/// <param name="normalizedFilePath">The normalized local path of the document, which the pipeline resolved once for the request.</param>
/// <param name="documentText">The document text the request's offsets index.</param>
/// <remarks>
/// The line map is built on first use and shared by every delegate of the request, so a request that converts no offset
/// or range builds none. The delegates of one request run in sequence, so the type takes no lock.
/// </remarks>
internal sealed class RequestedDocument(string normalizedFilePath, string documentText)
{
	private TextLineMap? _lineMap;

	/// <summary>The normalized local path of the document.</summary>
	internal string NormalizedFilePath { get; } = normalizedFilePath;

	/// <summary>The line map of the document text.</summary>
	internal TextLineMap LineMap => _lineMap ??= TextLineMap.Build(documentText);
}
