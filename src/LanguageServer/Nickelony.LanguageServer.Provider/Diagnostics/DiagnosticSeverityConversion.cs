using Nickelony.IDEKit.Core.Diagnostics;
using Lsp = Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Maps the diagnostic severity between the protocol and the shared <see cref="DiagnosticSeverity"/> vocabulary.</summary>
/// <remarks>
/// The mapping is explicit instead of a cast, so an unknown protocol value - including future protocol members - cannot leak an undefined enum
/// member into severity ordering. The protocol defines no default severity, so an omitted severity falls back to an error (the highest-signal choice)
/// and an unrecognized value to a warning.
/// </remarks>
internal static class DiagnosticSeverityConversion
{
	/// <summary>Resolves a protocol severity to the corresponding member.</summary>
	/// <param name="severity">The protocol severity, or <see langword="null"/> when the server omitted it.</param>
	/// <returns>
	/// The matching member, <see cref="DiagnosticSeverity.Error"/> for an omitted severity, or <see cref="DiagnosticSeverity.Warning"/> for a value
	/// the protocol does not define.
	/// </returns>
	internal static DiagnosticSeverity FromProtocol(Lsp.DiagnosticSeverity? severity)
	{
		return severity switch
		{
			Lsp.DiagnosticSeverity.Error => DiagnosticSeverity.Error,
			Lsp.DiagnosticSeverity.Warning => DiagnosticSeverity.Warning,
			Lsp.DiagnosticSeverity.Information => DiagnosticSeverity.Information,
			Lsp.DiagnosticSeverity.Hint => DiagnosticSeverity.Hint,
			null => DiagnosticSeverity.Error,
			_ => DiagnosticSeverity.Warning,
		};
	}

	/// <summary>Resolves a shared severity to the corresponding protocol member.</summary>
	/// <param name="severity">The shared severity.</param>
	/// <returns>
	/// The matching member, or <see langword="null"/> for a severity the protocol does not define, so the server applies its own default.
	/// </returns>
	internal static Lsp.DiagnosticSeverity? ToProtocol(DiagnosticSeverity severity)
	{
		return severity switch
		{
			DiagnosticSeverity.Error => Lsp.DiagnosticSeverity.Error,
			DiagnosticSeverity.Warning => Lsp.DiagnosticSeverity.Warning,
			DiagnosticSeverity.Information => Lsp.DiagnosticSeverity.Information,
			DiagnosticSeverity.Hint => Lsp.DiagnosticSeverity.Hint,
			_ => null,
		};
	}
}
