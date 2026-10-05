using System.Collections.Generic;

namespace Nickelony.Repository.Tests;

/// <summary>How the namespace of a file under a shared home is chosen.</summary>
internal enum SharedHomeKind
{
	/// <summary>An editor-binding namespace, chosen by the AVALONIAEDIT symbol.</summary>
	Editor,

	/// <summary>A key-binding adapter namespace, chosen by the AVALONIAKEYBINDINGS symbol.</summary>
	KeyBindings,

	/// <summary>A quick-pick toolkit namespace, chosen by the AVALONIAQUICKPICK symbol.</summary>
	QuickPick,

	/// <summary>A sample-head namespace, chosen by the AVALONIAEDIT symbol.</summary>
	Sample,

	/// <summary>
	/// A primitive that a Core slice publishes and the quick-pick family compiles as internal types of its own, chosen by the QUICKPICKFAMILY symbol:
	/// the Core slice namespace without it, the flat family namespace with it.
	/// </summary>
	Primitive,

	/// <summary>One namespace for every file, whatever the symbols.</summary>
	Neutral,
}

/// <summary>
/// A folder of source that more than one project compiles. <paramref name="Suffix"/> extends the binding root of an editor
/// or key-binding home and is the whole namespace of a neutral home. A folder-derived home appends the folder path below it to the namespace.
/// <paramref name="InternalHelpers"/> marks a home whose files declare internal helpers only, in the one flat infrastructure namespace.
/// <paramref name="SelectsVisibility"/> marks a home whose types are public in the package that publishes them and internal in the family
/// that compiles them, so the member-modifier guards read only the public variant.
/// </summary>
internal sealed record SharedHome(
	string Folder,
	SharedHomeKind Kind,
	string Suffix,
	bool FolderDerived,
	bool InternalHelpers = false,
	bool SelectsVisibility = false
);
