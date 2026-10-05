namespace Nickelony.IDEKit.Core.Tests;

/// <summary>Pins what each numeric validator accepts, what it rejects, and the message and parameter name of the exception.</summary>
[TestClass]
public sealed class NumericValidationTests
{
	[TestMethod]
	[DataRow(0.0)]
	[DataRow(1.5)]
	[DataRow(-1.5)]
	[DataRow(double.MaxValue)]
	[DataRow(double.MinValue)]
	public void Finite_AcceptsEveryFiniteValue(double value) => Assert.AreEqual(value, NumericValidation.Finite(value, "value"));

	[TestMethod]
	[DataRow(double.NaN)]
	[DataRow(double.PositiveInfinity)]
	[DataRow(double.NegativeInfinity)]
	public void Finite_RejectsNaNAndInfinity(double value)
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NumericValidation.Finite(value, "offset"));

		Assert.AreEqual("offset", exception.ParamName);
		Assert.AreEqual(value, exception.ActualValue);
		Assert.Contains("The value must be finite.", exception.Message);
	}

	[TestMethod]
	[DataRow(0.0, true)]
	[DataRow(2.5, true)]
	[DataRow(double.MaxValue, true)]
	[DataRow(-0.001, false)]
	[DataRow(double.NaN, false)]
	[DataRow(double.PositiveInfinity, false)]
	[DataRow(double.NegativeInfinity, false)]
	public void IsFiniteNonNegative_AcceptsZeroAndPositiveFiniteValues(double value, bool expected) =>
		Assert.AreEqual(expected, NumericValidation.IsFiniteNonNegative(value));

	[TestMethod]
	[DataRow(0.0)]
	[DataRow(2.5)]
	public void FiniteNonNegative_ReturnsTheAcceptedValue(double value) =>
		Assert.AreEqual(value, NumericValidation.FiniteNonNegative(value, "value"));

	[TestMethod]
	[DataRow(-0.001)]
	[DataRow(double.NaN)]
	[DataRow(double.PositiveInfinity)]
	[DataRow(double.NegativeInfinity)]
	public void FiniteNonNegative_RejectsNegativeNaNAndInfinity(double value)
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			NumericValidation.FiniteNonNegative(value, "spacing")
		);

		Assert.AreEqual("spacing", exception.ParamName);
		Assert.AreEqual(value, exception.ActualValue);
		Assert.Contains("The value must be finite and non-negative.", exception.Message);
	}

	[TestMethod]
	[DataRow(0.001, true)]
	[DataRow(double.MaxValue, true)]
	[DataRow(0.0, false)]
	[DataRow(-1.0, false)]
	[DataRow(double.NaN, false)]
	[DataRow(double.PositiveInfinity, false)]
	[DataRow(double.NegativeInfinity, false)]
	public void IsFinitePositive_AcceptsOnlyFiniteValuesAboveZero(double value, bool expected) =>
		Assert.AreEqual(expected, NumericValidation.IsFinitePositive(value));

	[TestMethod]
	public void FinitePositive_ReturnsTheAcceptedValue() => Assert.AreEqual(0.25, NumericValidation.FinitePositive(0.25, "value"));

	[TestMethod]
	[DataRow(0.0)]
	[DataRow(-1.0)]
	[DataRow(double.NaN)]
	[DataRow(double.PositiveInfinity)]
	[DataRow(double.NegativeInfinity)]
	public void FinitePositive_RejectsZeroNegativeNaNAndInfinity(double value)
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			NumericValidation.FinitePositive(value, "width")
		);

		Assert.AreEqual("width", exception.ParamName);
		Assert.AreEqual(value, exception.ActualValue);
		Assert.Contains("The value must be finite and greater than zero.", exception.Message);
	}

	[TestMethod]
	[DataRow(0.001)]
	[DataRow(double.PositiveInfinity)]
	public void Positive_AcceptsValuesAboveZeroIncludingPositiveInfinity(double value) =>
		Assert.AreEqual(value, NumericValidation.Positive(value, "value"));

	[TestMethod]
	[DataRow(0.0)]
	[DataRow(-1.0)]
	[DataRow(double.NaN)]
	[DataRow(double.NegativeInfinity)]
	public void Positive_RejectsZeroNegativeAndNaN(double value)
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			NumericValidation.Positive(value, "maxWidth")
		);

		Assert.AreEqual("maxWidth", exception.ParamName);
		Assert.AreEqual(value, exception.ActualValue);
		Assert.Contains("The value must be greater than zero.", exception.Message);
	}

	[TestMethod]
	public void NonNegative_AcceptsZeroAndPositiveDelays()
	{
		Assert.AreEqual(TimeSpan.Zero, NumericValidation.NonNegative(TimeSpan.Zero, "delay"));
		Assert.AreEqual(TimeSpan.FromSeconds(2.0), NumericValidation.NonNegative(TimeSpan.FromSeconds(2.0), "delay"));
	}

	[TestMethod]
	public void NonNegative_RejectsANegativeDelay()
	{
		var delay = TimeSpan.FromMilliseconds(-1.0);

		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			NumericValidation.NonNegative(delay, "debounce")
		);

		Assert.AreEqual("debounce", exception.ParamName);
		Assert.AreEqual(delay, exception.ActualValue);
		Assert.Contains("The delay must not be negative.", exception.Message);
	}

	[TestMethod]
	public void EveryValidator_WithANullParameterName_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => NumericValidation.Finite(1.0, null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => NumericValidation.FiniteNonNegative(1.0, null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => NumericValidation.FinitePositive(1.0, null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => NumericValidation.Positive(1.0, null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => NumericValidation.NonNegative(TimeSpan.Zero, null!));
	}
}
