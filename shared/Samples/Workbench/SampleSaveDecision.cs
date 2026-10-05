namespace Nickelony.Samples;

/// <summary>The answer of the window when a tab with unsaved changes closes.</summary>
internal enum SampleSaveDecision
{
	/// <summary>Save the changes, then close the tab.</summary>
	Save,

	/// <summary>Drop the changes, then close the tab.</summary>
	Discard,

	/// <summary>Keep the tab open.</summary>
	Cancel,
}
