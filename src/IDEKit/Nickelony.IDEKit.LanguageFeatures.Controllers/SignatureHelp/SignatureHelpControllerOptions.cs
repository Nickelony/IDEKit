using Nickelony.IDEKit.Core.Validation;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.SignatureHelp;

/// <summary>Stores the options used by a signature help controller.</summary>
/// <remarks>
/// Each option's default matches <see cref="Default"/>, so a host can start from <see cref="Default"/> and override only the options it needs
/// with a <c>with</c> expression. Every option validates itself when it is assigned, so an invalid value is rejected at the offending <c>with</c>
/// expression or object initializer instead of when the controller is constructed.
/// </remarks>
public sealed record SignatureHelpControllerOptions
{
	private readonly TimeSpan _requestDebounceDelay = TimeSpan.FromMilliseconds(50.0);

	/// <summary>The debounce delay before a scheduled request runs; defaults to 50 milliseconds.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative.</exception>
	public TimeSpan RequestDebounceDelay
	{
		get => _requestDebounceDelay;
		init => _requestDebounceDelay = NumericValidation.NonNegative(value, nameof(RequestDebounceDelay));
	}

	/// <summary>Whether overload navigation wraps around at the first and last signature; defaults to <see langword="true"/>.</summary>
	/// <remarks>
	/// When cycling is enabled, navigating past an edge continues from the opposite end; when it is disabled, navigating past the first or last
	/// signature dismisses the presentation instead.
	/// </remarks>
	public bool Cycle { get; init; } = true;

	public static SignatureHelpControllerOptions Default { get; } = new();
}
