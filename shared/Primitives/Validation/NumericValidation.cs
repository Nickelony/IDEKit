#if QUICKPICKFAMILY
namespace Nickelony.QuickPick;

#else
namespace Nickelony.IDEKit.Core.Validation;

#endif

/// <summary>
/// Validates the numeric values that options records and controllers accept, and reports a violation as an <see cref="ArgumentOutOfRangeException"/>.
/// </summary>
#if QUICKPICKFAMILY
internal
#else
public
#endif
static class NumericValidation
{
	/// <summary>Returns the value when it is finite; otherwise, throws.</summary>
	/// <param name="value">The value to validate.</param>
	/// <param name="parameterName">The parameter or property name reported by the exception.</param>
	/// <returns><paramref name="value"/>, unchanged.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="parameterName"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">The value is not finite.</exception>
	/// <remarks>
	/// Intended for a member that accepts any finite value, including a negative one, such as an offset a host may place on either side
	/// of its anchor.
	/// </remarks>
	public static double Finite(double value, string parameterName)
	{
		ArgumentNullException.ThrowIfNull(parameterName);
		return double.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(parameterName, value, "The value must be finite.");
	}

	/// <summary>Determines whether the value is finite and not negative.</summary>
	/// <param name="value">The value to inspect.</param>
	/// <returns><see langword="true"/> when the value is finite and at least zero; otherwise, <see langword="false"/>.</returns>
	public static bool IsFiniteNonNegative(double value) => double.IsFinite(value) && value >= 0.0;

	/// <summary>Returns the value when it is finite and non-negative; otherwise, throws.</summary>
	/// <param name="value">The value to validate.</param>
	/// <param name="parameterName">The parameter or property name reported by the exception.</param>
	/// <returns><paramref name="value"/>, unchanged.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="parameterName"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">The value is not finite, or it is negative.</exception>
	public static double FiniteNonNegative(double value, string parameterName)
	{
		ArgumentNullException.ThrowIfNull(parameterName);

		return IsFiniteNonNegative(value)
			? value
			: throw new ArgumentOutOfRangeException(parameterName, value, "The value must be finite and non-negative.");
	}

	/// <summary>Determines whether the value is finite and greater than zero.</summary>
	/// <param name="value">The value to inspect.</param>
	/// <returns><see langword="true"/> when the value is finite and greater than zero; otherwise, <see langword="false"/>.</returns>
	public static bool IsFinitePositive(double value) => double.IsFinite(value) && value > 0.0;

	/// <summary>Returns the value when it is finite and greater than zero; otherwise, throws.</summary>
	/// <param name="value">The value to validate.</param>
	/// <param name="parameterName">The parameter or property name reported by the exception.</param>
	/// <returns><paramref name="value"/>, unchanged.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="parameterName"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">The value is not finite, or it is zero or negative.</exception>
	public static double FinitePositive(double value, string parameterName)
	{
		ArgumentNullException.ThrowIfNull(parameterName);

		return IsFinitePositive(value)
			? value
			: throw new ArgumentOutOfRangeException(parameterName, value, "The value must be finite and greater than zero.");
	}

	/// <summary>Returns the value when it is greater than zero, allowing positive infinity; otherwise, throws.</summary>
	/// <param name="value">The value to validate.</param>
	/// <param name="parameterName">The parameter or property name reported by the exception.</param>
	/// <returns><paramref name="value"/>, unchanged.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="parameterName"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">The value is zero, negative, or NaN.</exception>
	public static double Positive(double value, string parameterName)
	{
		ArgumentNullException.ThrowIfNull(parameterName);
		return value > 0.0 ? value : throw new ArgumentOutOfRangeException(parameterName, value, "The value must be greater than zero.");
	}

	/// <summary>Returns the delay when it is non-negative; otherwise, throws.</summary>
	/// <param name="delay">The delay to validate.</param>
	/// <param name="parameterName">The parameter or property name reported by the exception.</param>
	/// <returns><paramref name="delay"/>, unchanged.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="parameterName"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">The delay is negative.</exception>
	public static TimeSpan NonNegative(TimeSpan delay, string parameterName)
	{
		ArgumentNullException.ThrowIfNull(parameterName);
		return delay >= TimeSpan.Zero ? delay : throw new ArgumentOutOfRangeException(parameterName, delay, "The delay must not be negative.");
	}
}
