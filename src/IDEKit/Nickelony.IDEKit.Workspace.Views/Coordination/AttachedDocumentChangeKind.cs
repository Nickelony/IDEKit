namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Classifies a change of a document that has an attached view.</summary>
public enum AttachedDocumentChangeKind
{
	/// <summary>The first view of the document attached.</summary>
	Opened,

	/// <summary>An operation changed the text of the document.</summary>
	ContentChanged,

	/// <summary>A rename, a save-as or a directory rename gave the document another id.</summary>
	Renamed,

	/// <summary>The last view of the document detached or closed, or a delete or a stop closed it.</summary>
	Closed,
}
