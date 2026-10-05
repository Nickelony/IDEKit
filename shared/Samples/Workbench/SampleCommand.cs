namespace Nickelony.Samples;

/// <summary>One action of the sample that a menu item and a key binding both run.</summary>
/// <param name="Id">The identifier the key bindings name the command by.</param>
/// <param name="Caption">The text of the menu item.</param>
/// <param name="Menu">The name of the menu that lists the command; the menus appear in the order their first command does.</param>
/// <param name="Execute">Runs the command on the UI thread.</param>
/// <param name="BeginsGroup">Whether a separator comes before the command in its menu.</param>
/// <param name="RunsAnywhere">Whether its shortcut runs it wherever the focus is, such as in the explorer, and not only in an editor.</param>
internal sealed record SampleCommand(string Id, string Caption, string Menu, Action Execute, bool BeginsGroup = false, bool RunsAnywhere = false);
