using Nickelony.IDEKit.Infrastructure;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Signatures;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Signatures;
#endif

/// <summary>
/// Stores the options used by a signature help controller.
/// </summary>
/// <remarks>
/// Each option's default matches <see cref="Default"/>, so a host can start from
/// <see cref="Default"/> and override only the options it needs with a <c>with</c> expression. Every
/// option validates itself when it is assigned, so an invalid value is rejected at the offending
/// <c>with</c> expression or object initializer instead of when the controller is constructed.
/// </remarks>
public sealed record TextSignatureHelpControllerOptions
{
	private readonly TimeSpan _refreshDebounceDelay = TimeSpan.FromMilliseconds(50.0);

	/// <summary>
	/// Gets the debounce delay before a scheduled refresh runs. Defaults to 50 milliseconds.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative.</exception>
	public TimeSpan RefreshDebounceDelay
	{
		get => _refreshDebounceDelay;
		init => _refreshDebounceDelay = NumericValidation.NonNegative(value, nameof(RefreshDebounceDelay));
	}

	/// <summary>
	/// Gets a value indicating whether overload navigation wraps around at the first and last
	/// signature. Defaults to <see langword="true"/>.
	/// </summary>
	/// <remarks>
	/// When cycling is enabled, navigating past an edge continues from the opposite end; when it is
	/// disabled, navigating past the first or last signature dismisses the presentation instead.
	/// </remarks>
	public bool Cycle { get; init; } = true;

	/// <summary>
	/// Gets the default options: a 50-millisecond refresh debounce delay and cyclic overload
	/// navigation.
	/// </summary>
	public static TextSignatureHelpControllerOptions Default { get; } = new();
}
