namespace Nickelony.LanguageServer.Lua;

/// <summary>
/// Builds the initialization-options payload sent to LuaLS.
/// </summary>
internal static class InitializationOptionsFactory
{
	/// <summary>
	/// Builds the LuaLS-specific initialization options.
	/// </summary>
	/// <remarks>
	/// LuaLS reads these flags from the initialization options it receives. <c>useSemanticByRange</c>
	/// is fixed to <see langword="false"/> so the server registers full semantic-token requests (the
	/// client implements full requests only). <c>trustByClient</c> is fixed to
	/// <see langword="false"/> so LuaLS keeps its own plugin-trust checks instead of taking a trust
	/// assertion from a library that cannot present or answer a trust prompt.
	/// <c>changeConfiguration</c> and <c>viewDocument</c> stay fixed to <see langword="false"/> (the
	/// flags were re-decided together with the client callback seams): the client cannot serve the
	/// flows they gate - LuaLS routes configuration modification through a <c>$/command</c>
	/// notification and document reveals through <c>window/showDocument</c>, and neither has a
	/// handler - so advertising the flags would let LuaLS assume the flows were handled. With them
	/// disabled, LuaLS falls back to its own configuration handling (writing the workspace
	/// configuration file or reporting manual instructions). The flags re-open only when the client
	/// gains handlers for those two server messages; exposing them as host-settable knobs before that
	/// would be unproduced surface, the same rule that keeps <c>trustByClient</c> fixed.
	/// </remarks>
	/// <returns>An anonymous initialization-options object serialized into the initialize request.</returns>
	internal static object Create()
	{
		return new
		{
			changeConfiguration = false,
			viewDocument = false,
			trustByClient = false,
			useSemanticByRange = false
		};
	}
}
