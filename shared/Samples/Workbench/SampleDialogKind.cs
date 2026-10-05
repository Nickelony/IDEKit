namespace Nickelony.Samples;

/// <summary>A child window that a command asks the window to open, because only the window knows its toolkit.</summary>
internal enum SampleDialogKind
{
	/// <summary>The find and replace window, which stays open beside the editor.</summary>
	FindAndReplace,

	/// <summary>The prompt for the new name of the symbol at the caret.</summary>
	Rename,

	/// <summary>The list of the commands and their shortcuts, where a shortcut is recorded, applied and reset.</summary>
	KeyBindings,

	/// <summary>The file picker for a Lua language server executable or a LuaLS archive.</summary>
	LuaServer,

	/// <summary>The file picker for the Roslyn language server executable.</summary>
	RoslynServer,

	/// <summary>The prompt for the name of a new file in the folder the event names.</summary>
	NewFile,

	/// <summary>The prompt for the name of a new folder in the folder the event names.</summary>
	NewFolder,

	/// <summary>The prompt for the new name of the file or folder the event names.</summary>
	RenameEntry,

	/// <summary>The question whether to move the file or folder the event names into the trash.</summary>
	DeleteEntry,
}
