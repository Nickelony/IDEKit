namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

/// <summary>Selects how a typed character opens completion.</summary>
internal enum CompletionTriggerMode
{
	/// <summary>The character opens no completion.</summary>
	None,

	/// <summary>The character schedules a request that runs once the typing pauses for the controller's debounce delay.</summary>
	Scheduled,

	/// <summary>The character requests completion at once, with the character as the trigger of the request.</summary>
	Immediate,
}
