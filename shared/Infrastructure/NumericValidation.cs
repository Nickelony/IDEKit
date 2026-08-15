namespace Nickelony.IDEKit.Infrastructure;

/// <summary>
/// Validates the numeric values shared by the packages' options records and controllers.
/// </summary>
/// <remarks>
/// The options records and constructors validate values when they are assigned or passed, so an invalid
/// value is rejected at the offending call instead of surfacing later while the value is being used.
/// This file is compiled into every package that needs it through a <c>&lt;Compile Include&gt;</c>
/// link. The <c>Nickelony.IDEKit.Infrastructure</c> namespace is intentionally shared by those linked
/// copies instead of following one project's folder-to-namespace convention, so the helper keeps a
/// single identity across packages.
/// </remarks>
internal static class NumericValidation
{
	/// <summary>
	/// Determines whether the value is finite.
	/// </summary>
	/// <param name="value">The value to inspect.</param>
	/// <returns><see langword="true"/> when the value is finite; otherwise, <see langword="false"/>.</returns>
	internal static bool IsFinite(double value) => double.IsFinite(value);

	/// <summary>
	/// Returns the value when it is finite; otherwise, throws.
	/// </summary>
	/// <remarks>
	/// Use this for a member that accepts any finite value, including a negative one, such as an offset
	/// that a host may place on either side of its anchor.
	/// </remarks>
	/// <param name="value">The value to validate.</param>
	/// <param name="parameterName">The parameter or property name reported by the exception.</param>
	/// <returns>The validated value.</returns>
	/// <exception cref="ArgumentOutOfRangeException">The value is not finite.</exception>
	internal static double Finite(double value, string parameterName)
		=> double.IsFinite(value)
			? value
			: throw new ArgumentOutOfRangeException(parameterName, value, "The value must be finite.");

	/// <summary>
	/// Determines whether the value is finite and non-negative.
	/// </summary>
	/// <param name="value">The value to inspect.</param>
	/// <returns><see langword="true"/> when the value is finite and non-negative; otherwise, <see langword="false"/>.</returns>
	internal static bool IsFiniteNonNegative(double value) => double.IsFinite(value) && value >= 0.0;

	/// <summary>
	/// Returns the value when it is finite and non-negative; otherwise, throws.
	/// </summary>
	/// <param name="value">The value to validate.</param>
	/// <param name="parameterName">The parameter or property name reported by the exception.</param>
	/// <returns>The validated value.</returns>
	/// <exception cref="ArgumentOutOfRangeException">The value is not finite, or it is negative.</exception>
	internal static double FiniteNonNegative(double value, string parameterName)
		=> IsFiniteNonNegative(value)
			? value
			: throw new ArgumentOutOfRangeException(parameterName, value, "The value must be finite and non-negative.");

	/// <summary>
	/// Determines whether the value is finite and greater than zero.
	/// </summary>
	/// <param name="value">The value to inspect.</param>
	/// <returns><see langword="true"/> when the value is finite and greater than zero; otherwise, <see langword="false"/>.</returns>
	internal static bool IsFinitePositive(double value) => double.IsFinite(value) && value > 0.0;

	/// <summary>
	/// Returns the value when it is finite and greater than zero; otherwise, throws.
	/// </summary>
	/// <param name="value">The value to validate.</param>
	/// <param name="parameterName">The parameter or property name reported by the exception.</param>
	/// <returns>The validated value.</returns>
	/// <exception cref="ArgumentOutOfRangeException">The value is not finite, or it is zero or negative.</exception>
	internal static double FinitePositive(double value, string parameterName)
		=> IsFinitePositive(value)
			? value
			: throw new ArgumentOutOfRangeException(parameterName, value, "The value must be finite and greater than zero.");

	/// <summary>
	/// Determines whether the value is greater than zero, allowing positive infinity.
	/// </summary>
	/// <remarks>
	/// Positive infinity expresses an unbounded value, so it is accepted by members that can be
	/// unbounded; NaN and negative infinity are rejected.
	/// </remarks>
	/// <param name="value">The value to inspect.</param>
	/// <returns><see langword="true"/> when the value is greater than zero; otherwise, <see langword="false"/>.</returns>
	internal static bool IsPositive(double value) => value > 0.0;

	/// <summary>
	/// Returns the value when it is greater than zero; otherwise, throws.
	/// </summary>
	/// <remarks>
	/// Positive infinity expresses an unbounded value, so it is accepted by members that can be
	/// unbounded; NaN and negative infinity are rejected.
	/// </remarks>
	/// <param name="value">The value to validate.</param>
	/// <param name="parameterName">The parameter or property name reported by the exception.</param>
	/// <returns>The validated value.</returns>
	/// <exception cref="ArgumentOutOfRangeException">The value is zero, negative, or NaN.</exception>
	internal static double Positive(double value, string parameterName)
		=> IsPositive(value)
			? value
			: throw new ArgumentOutOfRangeException(parameterName, value, "The value must be greater than zero.");

	/// <summary>
	/// Returns the delay when it is non-negative; otherwise, throws.
	/// </summary>
	/// <param name="delay">The delay to validate.</param>
	/// <param name="parameterName">The parameter or property name reported by the exception.</param>
	/// <returns>The validated delay.</returns>
	/// <exception cref="ArgumentOutOfRangeException">The delay is negative.</exception>
	internal static TimeSpan NonNegative(TimeSpan delay, string parameterName)
		=> delay >= TimeSpan.Zero
			? delay
			: throw new ArgumentOutOfRangeException(parameterName, delay, "The delay must not be negative.");
}
