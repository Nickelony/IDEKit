using System.Reflection;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

/// <summary>
/// Reads the public static surface of a type so taxonomy tests derive their expectations from the declared members instead of a hand-maintained copy.
/// </summary>
internal static class StaticMemberTestHelpers
{
	/// <summary>Returns the values of every public static property of the supplied member type.</summary>
	/// <typeparam name="TMember">The property type to collect.</typeparam>
	/// <param name="type">The type whose properties are read.</param>
	/// <returns>The property values in declaration order.</returns>
	internal static TMember[] GetPropertyValues<TMember>(Type type)
	{
		return
		[
			.. type.GetProperties(BindingFlags.Public | BindingFlags.Static)
				.Where(property => property.PropertyType == typeof(TMember))
				.Select(property => (TMember)property.GetValue(null)!),
		];
	}

	/// <summary>Returns the values of every public <see langword="string"/> constant field.</summary>
	/// <param name="type">The type whose constant fields are read.</param>
	/// <returns>The constant values in declaration order.</returns>
	internal static string[] GetStringConstantValues(Type type)
	{
		return
		[
			.. type.GetFields(BindingFlags.Public | BindingFlags.Static)
				.Where(field => field.IsLiteral && field.FieldType == typeof(string))
				.Select(field => (string)field.GetRawConstantValue()!),
		];
	}
}
