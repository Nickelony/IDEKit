namespace Nickelony.Samples;

/// <summary>Colors the text of one editor for its language and follows the theme.</summary>
internal interface ISampleHighlighter : IDisposable
{
	/// <summary>Recolors the text with the theme.</summary>
	void ApplyTheme(SampleTheme theme);
}
