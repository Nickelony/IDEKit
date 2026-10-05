namespace Nickelony.IDEKit.Workspace.Views.Tests;

/// <summary>An inline dispatch delegate that records which view members were served outside a dispatched action.</summary>
/// <remarks>
/// A collaborator of the coordinator must touch a view only where the dispatch delegate allows it. <see cref="Watch{TView}"/> hooks a view double,
/// and <see cref="MembersOutsideDispatch"/> lists every property read and method call that ran while no dispatched action was active.
/// A dispatched action may start another, so the probe counts the depth. The event accessors are not reported, because the attacher subscribes
/// outside the delegate on purpose.
/// </remarks>
internal sealed class RecordingViewDispatcher
{
	private int _depth;

	/// <summary>The number of actions the delegate ran.</summary>
	public int DispatchCount { get; private set; }

	/// <summary>The names of the view members that ran outside a dispatched action.</summary>
	public List<string> MembersOutsideDispatch { get; } = [];

	/// <summary>Runs the action inline and completes at once.</summary>
	/// <param name="action">The action to run.</param>
	public Task Dispatch(Action action)
	{
		DispatchCount++;
		_depth++;

		try
		{
			action();
		}
		finally
		{
			_depth--;
		}

		return Task.CompletedTask;
	}

	/// <summary>Starts recording the members the view serves outside a dispatched action.</summary>
	/// <param name="view">The view to watch.</param>
	public TView Watch<TView>(TView view)
		where TView : FakeWorkspaceView
	{
		view.OnMember = member =>
		{
			if (_depth == 0)
				MembersOutsideDispatch.Add(member);
		};

		return view;
	}
}
