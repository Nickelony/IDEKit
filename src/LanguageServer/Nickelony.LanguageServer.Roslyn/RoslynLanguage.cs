namespace Nickelony.LanguageServer.Roslyn;

/// <summary>The language that a Roslyn profile serves.</summary>
/// <remarks>
/// The Roslyn language server serves C# and Visual Basic from one process, so a profile differs by language only in the document language identifier,
/// the display name and the source and project patterns it mirrors. A workspace that holds both languages uses one provider per language,
/// and each starts its own server process.
/// </remarks>
public enum RoslynLanguage
{
	/// <summary>
	/// C#: documents open with the <c>csharp</c> language identifier, and the provider mirrors <c>*.cs</c> and <c>*.csproj</c> files.
	/// </summary>
	CSharp,

	/// <summary>
	/// Visual Basic: documents open with the <c>vb</c> language identifier, and the provider mirrors <c>*.vb</c> and <c>*.vbproj</c> files.
	/// </summary>
	VisualBasic,
}
