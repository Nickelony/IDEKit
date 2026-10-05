using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Editing;
using Nickelony.IDEKit.LanguageFeatures.Navigation;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Names the requested document in a parsed response the way the request named it.</summary>
/// <remarks>
/// A server names the document of every location by its URI, and the path read back from the URI
/// can differ from the request's identifier, for example in the case of the drive letter that LuaLS writes. A definition in the requested document
/// therefore carries no identifier, as <see cref="DefinitionLocation"/> defines, and a reference or an edit of it carries the request's identifier,
/// so a host that compares identifiers ordinally finds its own document. The caller passes the request's normalized path, which the request
/// pipeline resolved once, so no location normalizes it again.
/// </remarks>
internal static class RequestedDocumentNormalizer
{
	internal static DefinitionLocation? Normalize(DefinitionLocation? location, string requestFilePath, LocalPathComparisonPolicy pathComparison)
	{
		if (location?.DocumentId is not { } documentId || !IsRequested(documentId, requestFilePath, pathComparison))
			return location;

		return new DefinitionLocation(location.TargetRange, documentId: null, location.SelectionRange);
	}

	internal static IReadOnlyList<ReferenceLocation> Normalize(
		IReadOnlyList<ReferenceLocation> locations,
		string requestDocumentId,
		string requestFilePath,
		LocalPathComparisonPolicy pathComparison
	)
	{
		return
		[
			.. locations.Select(location =>
				IsRequested(location.DocumentId, requestFilePath, pathComparison)
					? new ReferenceLocation(requestDocumentId, location.Range)
					: location
			),
		];
	}

	internal static WorkspaceEdit? Normalize(
		WorkspaceEdit? edit,
		string requestDocumentId,
		string requestFilePath,
		LocalPathComparisonPolicy pathComparison
	)
	{
		if (edit is null)
			return null;

		return new WorkspaceEdit([
			.. edit.DocumentEdits.Select(documentEdit =>
				IsRequested(documentEdit.DocumentId, requestFilePath, pathComparison)
					? new TextDocumentEdit(requestDocumentId, documentEdit.TextEdits)
					: documentEdit
			),
		]);
	}

	internal static IReadOnlyList<CodeAction> Normalize(
		IReadOnlyList<CodeAction> actions,
		string requestDocumentId,
		string requestFilePath,
		LocalPathComparisonPolicy pathComparison
	)
	{
		return
		[
			.. actions.Select(action =>
				action.Edit is null
					? action
					: new CodeAction(
						title: action.Title,
						edit: Normalize(edit: action.Edit, requestDocumentId, requestFilePath, pathComparison),
						kind: action.Kind,
						isPreferred: action.IsPreferred,
						data: action.Data,
						command: action.Command
					)
			),
		];
	}

	// A response path and the request's path are both normalized already.
	private static bool IsRequested(string documentId, string requestFilePath, LocalPathComparisonPolicy pathComparison) =>
		pathComparison.Comparer.Equals(documentId, requestFilePath);
}
