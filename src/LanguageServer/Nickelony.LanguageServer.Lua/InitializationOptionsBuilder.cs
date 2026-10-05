namespace Nickelony.LanguageServer.Lua;

/// <summary>Builds the initialization-options payload sent to LuaLS.</summary>
internal static class InitializationOptionsBuilder
{
	/// <summary>Builds the LuaLS-specific initialization options.</summary>
	/// <returns>An anonymous initialization-options object serialized into the initialize request.</returns>
	/// <remarks>
	/// Every flag is fixed. <c>useSemanticByRange</c> is <see langword="false"/> because the client implements full
	/// semantic-token requests only. <c>changeConfiguration</c> and <c>viewDocument</c> are <see langword="false"/> because the client cannot handle
	/// the <c>$/command</c> and <c>window/showDocument</c> flows they gate. <c>trustByClient</c> is <see langword="false"/> so LuaLS keeps
	/// its own plugin-trust checks instead of taking an assertion from a library that cannot present or answer a trust prompt.
	/// </remarks>
	internal static object Build()
	{
		return new
		{
			changeConfiguration = false,
			viewDocument = false,
			trustByClient = false,
			useSemanticByRange = false,
		};
	}
}
