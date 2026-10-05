using Nickelony.IDEKit.Core.Navigation;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Navigation;
using Nickelony.QuickPick;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>
/// Covers the open editors mode: the order of the tabs, what a row shows, the filter, and the activation of the tab the user picks.
/// </summary>
[TestClass]
public sealed class OpenEditorsQuickPickProviderTests
{
	private static readonly string s_folder = Path.Combine(Path.GetTempPath(), "work", "src");

	private static OpenEditorDescriptor Editor(string name, bool isUnsaved = false, string? title = null) =>
		new(title ?? name, Path.Combine(s_folder, name), isUnsaved);

	[TestMethod]
	public void Constructor_NullCallbacks_Throw()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new OpenEditorsQuickPickProvider(null!, _ => { }));
		Assert.ThrowsExactly<ArgumentNullException>(() => new OpenEditorsQuickPickProvider(() => [], null!));
	}

	[TestMethod]
	public void Descriptor_NullArguments_Throw()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new OpenEditorDescriptor(null!, "a", false));
		Assert.ThrowsExactly<ArgumentNullException>(() => new OpenEditorDescriptor("a", null!, false));
	}

	[TestMethod]
	public void Open_ListsTheTabsInTheOrderOfTheHost()
	{
		OpenEditorDescriptor[] editors = [Editor("b.cs"), Editor("a.cs"), Editor("c.lua")];
		using var host = new QuickPickTestHost(new OpenEditorsQuickPickProvider(() => editors, _ => { }));

		host.Controller.Open("edt ");

		Assert.AreSequenceEqual(["b.cs", "a.cs", "c.lua"], host.RowTexts);
		Assert.AreEqual("b.cs", host.Controller.State.ActiveItem!.Label);
	}

	[TestMethod]
	public void Open_ShowsTheFolderOfTheDocumentAsTheDescriptionAndMarksAnUnsavedOne()
	{
		OpenEditorDescriptor[] editors = [Editor("a.cs"), Editor("b.cs", isUnsaved: true)];
		using var host = new QuickPickTestHost(new OpenEditorsQuickPickProvider(() => editors, _ => { }));

		host.Controller.Open("edt ");

		QuickPickItem[] items = [.. host.Controller.State.Rows.Select(row => row.Item!)];

		Assert.AreEqual(s_folder, items[0].Description);
		Assert.IsNull(items[0].TrailingText);
		Assert.IsNull(items[0].IconKey);
		Assert.AreEqual(OpenEditorsQuickPickOptions.Default.UnsavedText, items[1].TrailingText);
		Assert.AreEqual(OpenEditorDescriptor.UnsavedIconKey, items[1].IconKey);
	}

	[TestMethod]
	public void Texts_DefaultToEnglishAndFollowTheOptions()
	{
		OpenEditorDescriptor[] editors = [Editor("a.cs")];
		IQuickPickProvider english = new OpenEditorsQuickPickProvider(() => editors, _ => { });

		Assert.AreEqual("Open editors", english.Name);
		Assert.AreEqual("Select an open editor to switch to", english.Placeholder);
		Assert.AreEqual("No matching open editors", english.EmptyText);

		IQuickPickProvider german = new OpenEditorsQuickPickProvider(
			() => editors,
			_ => { },

			new OpenEditorsQuickPickOptions
			{
				Name = "Editoren",
				Placeholder = "Editor wählen",
				EmptyText = "Kein Editor",
			}
		);

		Assert.AreEqual("Editoren", german.Name);
		Assert.AreEqual("Editor wählen", german.Placeholder);
		Assert.AreEqual("Kein Editor", german.EmptyText);
	}

	[TestMethod]
	public void Options_RejectANullText()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new OpenEditorsQuickPickOptions { Name = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => new OpenEditorsQuickPickOptions { Placeholder = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => new OpenEditorsQuickPickOptions { EmptyText = null! });
	}

	[TestMethod]
	public void Open_TheUnsavedTextOfTheOptions_ReplacesTheDefaultAndNullHidesIt()
	{
		OpenEditorDescriptor[] editors = [Editor("b.cs", isUnsaved: true)];

		using (
			var localized = new QuickPickTestHost(
				new OpenEditorsQuickPickProvider(() => editors, _ => { }, new OpenEditorsQuickPickOptions { UnsavedText = "ungespeichert" })
			)
		)
		{
			localized.Controller.Open("edt ");
			Assert.AreEqual("ungespeichert", localized.Controller.State.ActiveItem!.TrailingText);
		}

		using var hidden = new QuickPickTestHost(
			new OpenEditorsQuickPickProvider(() => editors, _ => { }, new OpenEditorsQuickPickOptions { UnsavedText = null })
		);

		hidden.Controller.Open("edt ");

		Assert.IsNull(hidden.Controller.State.ActiveItem!.TrailingText);
		Assert.AreEqual(OpenEditorDescriptor.UnsavedIconKey, hidden.Controller.State.ActiveItem.IconKey);
	}

	[TestMethod]
	public void Open_ADocumentBelowARoot_ShowsItsFolderBelowTheRoot()
	{
		string root = Path.Combine(Path.GetTempPath(), "work");

		OpenEditorDescriptor[] editors =
		[
			Editor("a.cs"),
			new("top.cs", Path.Combine(root, "top.cs"), false),
			new("far.cs", Path.Combine(Path.GetTempPath(), "other", "far.cs"), false),
		];

		var options = new OpenEditorsQuickPickOptions { RootPaths = [root] };
		using var host = new QuickPickTestHost(new OpenEditorsQuickPickProvider(() => editors, _ => { }, options));

		host.Controller.Open("edt ");

		QuickPickItem[] items = [.. host.Controller.State.Rows.Select(row => row.Item!)];

		Assert.AreEqual("src", items[0].Description);
		Assert.IsNull(items[1].Description);
		Assert.AreEqual(Path.Combine(Path.GetTempPath(), "other").TrimEnd(Path.DirectorySeparatorChar), items[2].Description);
	}

	[TestMethod]
	public void Open_SeveralRoots_PutTheNameOfTheRootInFrontOfTheFolder()
	{
		string first = Path.Combine(Path.GetTempPath(), "work");
		string second = Path.Combine(Path.GetTempPath(), "lib");

		OpenEditorDescriptor[] editors = [Editor("a.cs"), new("b.cs", Path.Combine(second, "b.cs"), false)];

		var options = new OpenEditorsQuickPickOptions { RootPaths = [first, second] };
		using var host = new QuickPickTestHost(new OpenEditorsQuickPickProvider(() => editors, _ => { }, options));

		host.Controller.Open("edt ");

		Assert.AreSequenceEqual(["work/src", "lib"], host.Controller.State.Rows.Select(row => row.Item!.Description));
	}

	[TestMethod]
	public void Open_ATabWhoseDocumentIdHasNoFolder_ShowsNoDescription()
	{
		OpenEditorDescriptor[] editors = [new("Untitled-1", "untitled-1", true)];
		using var host = new QuickPickTestHost(new OpenEditorsQuickPickProvider(() => editors, _ => { }));

		host.Controller.Open("edt ");

		Assert.IsNull(host.Controller.State.ActiveItem!.Description);
	}

	[TestMethod]
	public void Open_TwoTabsOfOneDocument_GetDistinctKeys()
	{
		OpenEditorDescriptor[] editors = [Editor("a.cs"), Editor("a.cs", title: "a.cs (2)")];
		using var host = new QuickPickTestHost(new OpenEditorsQuickPickProvider(() => editors, _ => { }));

		host.Controller.Open("edt ");

		Assert.HasCount(2, host.Keys.Distinct(StringComparer.Ordinal));
		Assert.AreSequenceEqual(["a.cs", "a.cs (2)"], host.RowTexts);
	}

	[TestMethod]
	public void Open_AQuery_FiltersByTitleAndByFolderAsAPath()
	{
		OpenEditorDescriptor[] editors =
		[
			Editor("Alpha.cs"),
			Editor("Beta.cs"),
			new("Gamma.lua", Path.Combine(path1: Path.GetTempPath(), path2: "work", path3: "lib", path4: "Gamma.lua"), false),
		];

		using var host = new QuickPickTestHost(new OpenEditorsQuickPickProvider(() => editors, _ => { }));

		host.Controller.Open("edt bet");

		Assert.AreSequenceEqual(["Beta.cs"], host.RowTexts);

		host.Controller.SetText("edt lib/gam");
		host.PassDebounce();

		Assert.AreSequenceEqual(["Gamma.lua"], host.RowTexts);
	}

	[TestMethod]
	public void Open_AQueryThatMatchesNoTab_SaysSo()
	{
		using var host = new QuickPickTestHost(new OpenEditorsQuickPickProvider(() => [Editor("a.cs")], _ => { }));

		host.Controller.Open("edt zzz");

		Assert.AreEqual("No matching open editors", host.Controller.State.Rows.Single().Message);
	}

	[TestMethod]
	public void AcceptAsync_ActivatesTheTabOfTheItemWithTheTagTheHostAttached()
	{
		object tag = new();
		List<OpenEditorDescriptor> activated = [];
		OpenEditorDescriptor[] editors = [Editor("a.cs"), new("b.cs", Path.Combine(s_folder, "b.cs"), false) { Tag = tag }];

		using var host = new QuickPickTestHost(new OpenEditorsQuickPickProvider(() => editors, activated.Add));

		host.Controller.Open("edt ");
		host.Controller.MoveActive(1);

		Assert.IsTrue(host.Accept());

		Assert.HasCount(1, activated);
		Assert.AreSame(tag, activated[0].Tag);
		Assert.AreEqual("b.cs", activated[0].Title);
		Assert.IsFalse(host.Controller.State.IsOpen);
	}

	[TestMethod]
	public void GetItemsAsync_AsksTheHostForItsTabsOnTheDispatcherThreadEachTime()
	{
		List<int> threads = [];

		using var host = new QuickPickTestHost(
			new OpenEditorsQuickPickProvider(
				() =>
				{
					threads.Add(Environment.CurrentManagedThreadId);
					return [Editor("a.cs")];
				},

				_ => { }
			)
		);

		host.Controller.Open("edt ");
		host.Controller.SetText("edt a");
		host.PassDebounce();

		Assert.HasCount(2, threads);
		Assert.IsTrue(threads.All(thread => thread == host.Dispatcher.OwnerThreadId));
	}

	[TestMethod]
	public void GetItemsAsync_TheHostCallbackThrows_IsAFailedQueryAndTheSessionGoesOn()
	{
		using var host = new QuickPickTestHost(new OpenEditorsQuickPickProvider(() => throw new InvalidOperationException("tabs"), _ => { }));

		host.Controller.Open("edt ");

		Assert.IsTrue(host.Controller.State.IsOpen);
		Assert.IsEmpty(host.Controller.State.Rows);
		Assert.IsInstanceOfType<InvalidOperationException>(host.Faults.Single());
	}
}
