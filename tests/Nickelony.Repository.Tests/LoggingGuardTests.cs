using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>
/// A package keeps one of two logging styles: it logs direct diagnostics through the <c>ILogger</c> extension methods and suppresses CA1848
/// with a rationale in its project file, or it logs through <c>LoggerMessage</c> methods that name their event id and event name. A component takes
/// the host's plain <c>ILogger</c> and never a generic logger or a logger factory, so the host's category survives.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed class LoggingGuardTests
{
	private const string Guard = nameof(LoggingGuardTests);
	private const string Ca1848 = "CA1848";

	// The extension methods of ILogger that write a log event directly.
	private static readonly HashSet<string> s_directLogMethods = new(StringComparer.Ordinal)
	{
		"LogCritical",
		"LogDebug",
		"LogError",
		"LogInformation",
		"LogTrace",
		"LogWarning",
	};

	// A package that suppresses CA1848 and still declares a LoggerMessage method, keyed by the project name. The method cites its measurement.
	private static readonly AllowList s_directPackagesWithGeneratedMessages = new("direct packages with LoggerMessage methods");

	// A use of a generic logger or a logger factory, keyed "{path}: {text}".
	private static readonly AllowList s_categorizedLoggers = new("generic loggers and logger factories");

	[TestMethod]
	public void APackageThatLogsDirectDiagnosticsSuppressesCa1848WithARationale()
	{
		List<string> violations = [];
		ILookup<ProjectFile, ProductFile> filesByProject = ProductSyntax.Files().ToLookup(file => file.Project);

		foreach (ProjectFile project in RepositoryModel.SourceProjects)
		{
			int directCalls = filesByProject[project]
				.SelectMany(file => file.Root.DescendantNodes().OfType<InvocationExpressionSyntax>())
				.Count(IsDirectLogCall);

			XElement? suppression = Ca1848Suppression(project);

			if (directCalls > 0 && suppression is null)
			{
				violations.Add(
					$"{project.Name} makes {directCalls} direct ILogger extension calls and does not suppress CA1848. Log through LoggerMessage methods, or suppress CA1848 in the project file with a rationale."
				);
			}

			if (directCalls == 0 && suppression is not null)
				violations.Add($"{project.Name} suppresses CA1848 and makes no direct ILogger extension call. Delete the suppression.");

			if (
				suppression is not null
				&& !(suppression.PreviousNode is XComment comment && comment.Value.Contains(Ca1848, StringComparison.Ordinal))
			)
			{
				violations.Add(
					$"{project.Name} suppresses CA1848 without a comment that names CA1848 and says why, right before the NoWarn element."
				);
			}
		}

		Assert.IsEmpty(violations, "Logging style of a package:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void APackageThatSuppressesCa1848DeclaresNoLoggerMessageMethod()
	{
		List<string> violations = [];
		ILookup<ProjectFile, ProductFile> filesByProject = ProductSyntax.Files().ToLookup(file => file.Project);

		foreach (ProjectFile project in RepositoryModel.SourceProjects.Where(project => Ca1848Suppression(project) is not null))
		{
			int generated = filesByProject[project].SelectMany(file => LoggerMessageAttributes(file.Root)).Count();

			if (generated > 0 && !s_directPackagesWithGeneratedMessages.Allows(project.Name))
			{
				violations.Add(
					s_directPackagesWithGeneratedMessages.Rejection(
						$"{project.Name} logs direct diagnostics and declares {generated} LoggerMessage methods, so it mixes the two logging styles.",
						Guard
					)
				);
			}
		}

		violations.AddRange(s_directPackagesWithGeneratedMessages.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Mixed logging styles:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void ALoggerMessageMethodNamesItsEventIdAndEventName()
	{
		List<string> violations = [];
		int read = 0;

		foreach (ProductFile file in ProductSyntax.Files())
		{
			foreach (AttributeSyntax attribute in LoggerMessageAttributes(file.Root))
			{
				read++;

				HashSet<string> named = new(
					attribute.ArgumentList?.Arguments.Select(argument => argument.NameEquals?.Name.Identifier.ValueText).OfType<string>() ?? [],
					StringComparer.Ordinal
				);

				foreach (string required in new[] { "EventId", "EventName" }.Where(required => !named.Contains(required)))
					violations.Add($"{file.Path}:{attribute.GetLocation().GetLineSpan().StartLinePosition.Line + 1} names no {required}.");
			}
		}

		Assert.IsGreaterThan(0, read, "The guard read no LoggerMessage attribute, so it checks nothing.");

		Assert.IsEmpty(
			violations.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),

			"A LoggerMessage attribute must declare EventId = and EventName = :"
				+ Environment.NewLine
				+ string.Join(Environment.NewLine, violations.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
		);
	}

	[TestMethod]
	public void NoComponentTakesAGenericLoggerOrALoggerFactory()
	{
		HashSet<string> offenders = new(StringComparer.Ordinal);

		foreach (ProductFile file in ProductSyntax.Files())
		{
			foreach (SyntaxNode use in file.Root.DescendantNodes().Where(IsCategorizedLoggerUse))
				offenders.Add($"{file.Path}: {use}");
		}

		List<string> violations = [];

		foreach (string offender in offenders.Order(StringComparer.Ordinal).Where(offender => !s_categorizedLoggers.Allows(offender)))
		{
			violations.Add(
				s_categorizedLoggers.Rejection(
					$"{offender} names a generic ILogger<T> or an ILoggerFactory, which re-categorizes the log events of a host that filters by category. Take a plain ILogger.",
					Guard
				)
			);
		}

		violations.AddRange(s_categorizedLoggers.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Categorized loggers:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	private static bool IsCategorizedLoggerUse(SyntaxNode node) =>
		node is GenericNameSyntax { Identifier.ValueText: "ILogger" } or IdentifierNameSyntax { Identifier.ValueText: "ILoggerFactory" };

	/// <summary>
	/// Reports whether the call is an <c>ILogger</c> extension call that writes an event directly: one of the named level methods,
	/// or <c>Log</c> with a <c>LogLevel</c> first argument.
	/// </summary>
	private static bool IsDirectLogCall(InvocationExpressionSyntax call)
	{
		if (call.Expression is not MemberAccessExpressionSyntax access)
			return false;

		string name = access.Name.Identifier.ValueText;

		return s_directLogMethods.Contains(name)
			|| (
				name == "Log"
				&& call.ArgumentList.Arguments.FirstOrDefault()?.Expression.ToString().StartsWith("LogLevel.", StringComparison.Ordinal) == true
			);
	}

	private static IEnumerable<AttributeSyntax> LoggerMessageAttributes(SyntaxNode root) =>
		root.DescendantNodes().OfType<AttributeSyntax>().Where(attribute => attribute.Name.ToString() is "LoggerMessage" or "LoggerMessageAttribute");

	/// <summary>
	/// The <c>NoWarn</c> element of the project file that lists CA1848, or <see langword="null"/> when the project does not suppress it.
	/// </summary>
	private static XElement? Ca1848Suppression(ProjectFile project)
	{
		return project
			.Document.Descendants("NoWarn")
			.FirstOrDefault(element =>
				element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(Ca1848)
			);
	}
}
