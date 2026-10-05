namespace Nickelony.Samples;

/// <summary>The language of a sample document, which selects its highlighting and its language features.</summary>
internal enum SampleDocumentKind
{
	/// <summary>Lua source, served by the sample language providers or by the Lua language server.</summary>
	Lua,

	/// <summary>C# source, served by the Roslyn language server once it is started.</summary>
	CSharp,

	/// <summary>Visual Basic source, served by the Roslyn language server once it is started.</summary>
	VisualBasic,

	/// <summary>JSON, highlighted by regular expressions; the settings file is also served by its JSON Schema.</summary>
	Json,

	/// <summary>Markdown, which a preview renders beside its text.</summary>
	Markdown,

	/// <summary>Any other text, which has no highlighting and no language features.</summary>
	PlainText,
}
