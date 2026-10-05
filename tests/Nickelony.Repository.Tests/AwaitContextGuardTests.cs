using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>
/// A library await ends in <c>ConfigureAwait(false)</c>, because a host may block on the task from a UI thread, and the build
/// enforces it with CA2007. A binding awaits on the captured context and says so with <c>ConfigureAwait(true)</c>, because it touches the editor
/// after the await. A package that holds controllers beside engine-free machinery states the kind at every await, so a bare await fails a guard.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed partial class AwaitContextGuardTests
{
	private const string Guard = nameof(AwaitContextGuardTests);

	// The projects whose awaits all resume on the thread that owns the editor or the toolkit element, keyed by project name. CA2007 stays off
	// for them, because it cannot tell true from false, and this guard requires ConfigureAwait(true) instead.
	private static readonly AllowList s_contextProjects = new(
		"projects that resume on the captured context",
		("Nickelony.IDEKit.AvalonEdit*", "The WPF editor bindings touch the editor and its elements after an await."),
		("Nickelony.IDEKit.AvaloniaEdit*", "The Avalonia editor bindings touch the editor and its elements after an await."),
		("Nickelony.KeyBindings.Avalonia", "The adapter handles input on the thread of the Avalonia element."),
		("Nickelony.KeyBindings.Wpf", "The adapter handles input on the thread of the WPF element."),
		("Nickelony.QuickPick.Avalonia", "The window touches Avalonia elements after an await."),
		("Nickelony.QuickPick.Wpf", "The window touches WPF elements after an await.")
	);

	// The projects that hold controllers, which resume on the captured context, beside engine-free machinery, which does not. CA2007 stays off for
	// them for the same reason, and this guard requires every await to state its kind.
	private static readonly AllowList s_mixedProjects = new(
		"projects that hold controllers beside engine-free machinery",

		(
			"Nickelony.IDEKit.LanguageFeatures.Controllers",
			"The controllers resume on the editor thread, and the sessions, resolvers and sources they drive do not."
		),

		("Nickelony.QuickPick", "The quick-pick controller resumes on its creating thread, and the provider query and the gates do not.")
	);

	// An await in a binding that does not end in ConfigureAwait(true), keyed "{path}: {type}.{member}".
	private static readonly AllowList s_unmarkedBindingAwaits = new("binding awaits without ConfigureAwait(true)");

	// An await in a mixed project that states no ConfigureAwait, keyed "{path}: {type}.{member}".
	private static readonly AllowList s_bareAwaits = new("awaits that state no ConfigureAwait");

	// A library await that ends in ConfigureAwait(true), keyed "{path}: {type}.{member}".
	private static readonly AllowList s_capturingLibraryAwaits = new("library awaits that resume on the captured context");

	[TestMethod]
	public void EveryLibraryProjectEnablesCa2007UnlessItResumesOnTheCapturedContext()
	{
		List<string> violations = [];

		foreach (ProjectFile project in RepositoryModel.SourceProjects)
		{
			bool enabled = project.Document.Descendants("RequireConfigureAwait").Any(element => element.Value.Trim() == "true");
			bool exempt = s_contextProjects.Allows(project.Name) || s_mixedProjects.Allows(project.Name);

			if (!exempt && !enabled)
			{
				violations.Add(
					$"{project.Name} does not set RequireConfigureAwait, so CA2007 does not run for it. Set it, or list the project in {Guard} with the reason it resumes on the captured context."
				);
			}

			if (exempt && enabled)
			{
				violations.Add(
					$"{project.Name} resumes on the captured context and sets RequireConfigureAwait, which makes CA2007 reject its ConfigureAwait(true)."
				);
			}
		}

		violations.AddRange(s_contextProjects.UnmatchedMessages(Guard));
		violations.AddRange(s_mixedProjects.UnmatchedMessages(Guard));

		string targets = File.ReadAllText(RepositoryPaths.Below("Directory.Build.targets"));

		if (
			!targets.Contains("RequireConfigureAwait", StringComparison.Ordinal)
			|| !targets.Contains("ConfigureAwait.globalconfig", StringComparison.Ordinal)
		)
		{
			violations.Add("Directory.Build.targets does not add ConfigureAwait.globalconfig for a project that sets RequireConfigureAwait.");
		}

		string config = File.ReadAllText(RepositoryPaths.Below("ConfigureAwait.globalconfig"));

		if (!Ca2007Severity().IsMatch(config))
			violations.Add("ConfigureAwait.globalconfig does not set dotnet_diagnostic.CA2007.severity to warning or error.");

		Assert.IsEmpty(violations, "CA2007 projects:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void AnAwaitInAnEditorBindingOrAnAdapterSaysConfigureAwaitTrue()
	{
		HashSet<string> offenders = new(StringComparer.Ordinal);
		int read = 0;

		foreach (ProductFile file in ProductSyntax.Files().Where(file => s_contextProjects.Allows(file.Project.Name)))
		{
			foreach ((string key, AwaitContextKind kind) in Awaits(file))
			{
				read++;

				if (kind != AwaitContextKind.CapturesContext)
					offenders.Add($"{key} ({Describe(kind)})");
			}
		}

		Assert.IsGreaterThan(0, read, "The guard read no await of a binding, so it checks nothing.");

		List<string> violations = [];

		foreach (string offender in offenders.Order(StringComparer.Ordinal))
		{
			string key = offender[..offender.LastIndexOf(" (", StringComparison.Ordinal)];

			if (!s_unmarkedBindingAwaits.Allows(key))
			{
				violations.Add(
					s_unmarkedBindingAwaits.Rejection(
						$"{offender} does not end in ConfigureAwait(true), and a binding touches the editor after an await.",
						Guard
					)
				);
			}
		}

		violations.AddRange(s_unmarkedBindingAwaits.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Binding awaits:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void AnAwaitInAControllerPackageStatesItsContext()
	{
		HashSet<string> offenders = new(StringComparer.Ordinal);

		foreach (ProductFile file in ProductSyntax.Files().Where(file => s_mixedProjects.Allows(file.Project.Name)))
		{
			foreach ((string key, _) in Awaits(file).Where(site => site.Kind == AwaitContextKind.Unstated))
				offenders.Add(key);
		}

		List<string> violations = [];

		foreach (string offender in offenders.Order(StringComparer.Ordinal).Where(offender => !s_bareAwaits.Allows(offender)))
		{
			violations.Add(
				s_bareAwaits.Rejection(
					$"{offender} awaits without ConfigureAwait. Say ConfigureAwait(true) where the code touches the editor afterwards, and ConfigureAwait(false) where it does not.",
					Guard
				)
			);
		}

		violations.AddRange(s_bareAwaits.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Controller package awaits:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void ALibraryAwaitNeverResumesOnTheCapturedContext()
	{
		HashSet<string> offenders = new(StringComparer.Ordinal);

		foreach (
			ProductFile file in ProductSyntax
				.Files()
				.Where(file => !s_contextProjects.Allows(file.Project.Name) && !s_mixedProjects.Allows(file.Project.Name))
		)
		{
			foreach ((string key, _) in Awaits(file).Where(site => site.Kind == AwaitContextKind.CapturesContext))
				offenders.Add(key);
		}

		List<string> violations = [];

		foreach (string offender in offenders.Order(StringComparer.Ordinal).Where(offender => !s_capturingLibraryAwaits.Allows(offender)))
		{
			violations.Add(
				s_capturingLibraryAwaits.Rejection(
					$"{offender} ends in ConfigureAwait(true), and a host may block on the task of a library from a UI thread. Use ConfigureAwait(false).",
					Guard
				)
			);
		}

		violations.AddRange(s_capturingLibraryAwaits.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Library awaits:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	/// <summary>
	/// The awaits of a file, as an <c>await</c> expression, an <c>await using</c> statement or declaration, or an <c>await foreach</c>.
	/// </summary>
	private static IEnumerable<(string Key, AwaitContextKind Kind)> Awaits(ProductFile file)
	{
		foreach (SyntaxNode node in file.Root.DescendantNodes())
		{
			ExpressionSyntax? awaited = node switch
			{
				AwaitExpressionSyntax expression => expression.Expression,

				UsingStatementSyntax { AwaitKeyword.RawKind: not 0 } statement => statement.Expression
					?? statement.Declaration?.Variables.FirstOrDefault()?.Initializer?.Value,

				LocalDeclarationStatementSyntax { AwaitKeyword.RawKind: not 0 } declaration => declaration
					.Declaration.Variables.FirstOrDefault()
					?.Initializer?.Value,

				CommonForEachStatementSyntax { AwaitKeyword.RawKind: not 0 } loop => loop.Expression,
				_ => null,
			};

			if (awaited is null)
				continue;

			yield return ($"{file.Path}: {EnclosingMember(node)}", Classify(awaited));
		}
	}

	private static AwaitContextKind Classify(ExpressionSyntax awaited)
	{
		ExpressionSyntax expression = awaited;

		while (expression is ParenthesizedExpressionSyntax parenthesized)
			expression = parenthesized.Expression;

		if (
			expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ConfigureAwait" } } call
		)
		{
			return call.ArgumentList.Arguments.FirstOrDefault()?.Expression switch
			{
				LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.TrueLiteralExpression) => AwaitContextKind.CapturesContext,
				LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.FalseLiteralExpression) => AwaitContextKind.LeavesContext,
				_ => AwaitContextKind.Computed,
			};
		}

		return AwaitContextKind.Unstated;
	}

	private static string Describe(AwaitContextKind kind)
	{
		return kind switch
		{
			AwaitContextKind.LeavesContext => "ends in ConfigureAwait(false)",
			AwaitContextKind.Computed => "ends in ConfigureAwait with a computed argument",
			_ => "states no ConfigureAwait",
		};
	}

	/// <summary>The type and the member that contain the node, as <c>Type.Member</c>, where a constructor is <c>.ctor</c>.</summary>
	private static string EnclosingMember(SyntaxNode node)
	{
		TypeDeclarationSyntax? type = node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();

		string member =
			node.Ancestors()
				.Select(ancestor =>
					ancestor switch
					{
						MethodDeclarationSyntax method => method.Identifier.ValueText,
						ConstructorDeclarationSyntax => ".ctor",
						PropertyDeclarationSyntax property => property.Identifier.ValueText,
						OperatorDeclarationSyntax => "operator",
						_ => null,
					}
				)
				.FirstOrDefault(name => name is not null)
			?? "<member>";

		return $"{(type is null ? string.Empty : ProductSyntax.NameOf(type))}.{member}";
	}

	[GeneratedRegex(@"^dotnet_diagnostic\.CA2007\.severity\s*=\s*(warning|error)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
	private static partial Regex Ca2007Severity();

	private enum AwaitContextKind
	{
		/// <summary>The await states no ConfigureAwait.</summary>
		Unstated,

		/// <summary>The await ends in <c>ConfigureAwait(true)</c>.</summary>
		CapturesContext,

		/// <summary>The await ends in <c>ConfigureAwait(false)</c>.</summary>
		LeavesContext,

		/// <summary>The await ends in <c>ConfigureAwait</c> with an argument that is no literal.</summary>
		Computed,
	}
}
