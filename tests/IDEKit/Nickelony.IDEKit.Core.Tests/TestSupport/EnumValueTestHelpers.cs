namespace Nickelony.IDEKit.Core.Tests;

/// <summary>Reads the numeric value of an enum member for the API-stability tests.</summary>
/// <remarks>
/// Routing the cast through a generic method keeps MSTEST0032 from reporting the assertion as an always-true constant comparison; do not inline
/// the cast into an assertion.
/// </remarks>
internal static class EnumValueTestHelpers
{
	/// <summary>Returns the numeric value of the supplied enum member.</summary>
	internal static int Value<TEnum>(TEnum value)
		where TEnum : struct, Enum => Convert.ToInt32(value);
}
