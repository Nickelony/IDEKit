namespace Nickelony.IDEKit.FileWatching;

/// <summary>Classifies how a watched file changed.</summary>
public enum FileChangeKind
{
	/// <summary>The file was created.</summary>
	Created = 1,

	/// <summary>The file content or metadata changed.</summary>
	Changed = 2,

	/// <summary>The file was deleted.</summary>
	Deleted = 3,
}
