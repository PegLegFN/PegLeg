using Godot;
using System.Linq;

[Tool]
public partial class SubmenuController : Container
{
	[Export]
	bool resetWhenInvisible = false;

	[Export]
	PackedScene buttonScene;

	[Export]
	Control buttonListControl;

	[Export]
	Control buttonParent;

	[Export]
	Control pageContainer;

	[Export]
	Control pageParent;

	SubmenuController activeSubmenu;
	Control[] pages;

	SubmenuController RootSubmenu
	{
		get
		{
			var root = this;
			while (root.parent is SubmenuController next)
				root = next;
			return root;
		}
	}

	bool searchedForParent = false;
	SubmenuController parent;
	SubmenuController Parent
	{
		get
		{
			if (searchedForParent)
				return parent;
			searchedForParent = true;
			var parentNode = GetParentOrNull<Control>();
			while (parentNode is not null)
			{
				if (parentNode is SubmenuController parentSM)
					return parent = parentSM;
				parentNode = parentNode.GetParentOrNull<Control>();
			}
			return null;
		}
	}

	public override void _Ready()
	{
		buttonListControl.PivotOffsetRatio = Vector2.One * 0.5f;
		if (!Engine.IsEditorHint())
		{
			parent = Parent;
			if (parent is not null)
				resetWhenInvisible = false;
			pages = [.. pageParent
				.GetChildren()
				.Select(n => (Control)n)
				.Where(n => n is not null)
			];
			for (int i = 0; i < pages.Length; i++)
			{
				if (buttonParent.GetChildCount() <= i)
				{
					buttonParent.AddChild(buttonScene.Instantiate());
				}
				var btn = buttonParent.GetChild(i);
				Button btnCtrl = (Button)btn;
				//if (btnCtrl is null)
				//{
				//    //find button
				//}
				int index = i;
				btnCtrl.Pressed += () => SetActivePage(index);
				btnCtrl.Text = pages[i].Name;
			}
			int btnCount = buttonParent.GetChildCount();
			for (int i = pages.Length; i < btnCount; i++)
			{
				if (buttonParent.GetChild(i) is Control child)
					child.Visible = false;
			}
			PageProgress = 0;
			if (resetWhenInvisible)
				VisibilityChanged += () =>
				{
					if (!IsVisibleInTree())
						ResetMenu();
				};
		}
	}

	void ResetMenu()
	{
		while (activeSubmenu is not null)
		{
			GoBack();
		}
	}

	void SetActivePage(int index)
	{
		for (int i = 0; i < pages.Length; i++)
		{
			pages[i].Visible = i == index;
		}
		RootSubmenu.activeSubmenu = this;
		var tween = CreateTween();
		tween.TweenProperty(this, "PageProgress", 1, 0.1f);
	}

	void GoBack()
	{
		var root = RootSubmenu;
		if (root.activeSubmenu is null)
			return;
		var tween = CreateTween();
		tween.TweenProperty(root.activeSubmenu, "PageProgress", 0, 0.1f);
		root.activeSubmenu = root.activeSubmenu.parent;
	}

	public override void _Notification(int what)
	{
		if (what == NotificationSortChildren)
			UpdateLayout();
	}

	public override Vector2 _GetMinimumSize()
	{
		var buttonMin = buttonListControl.GetCombinedMinimumSize();
		var pageMin = pageContainer.GetCombinedMinimumSize();

		return new(
			Mathf.Max(Mathf.Max(buttonMin.X, pageMin.X), 1),
			Mathf.Max(Mathf.Max(buttonMin.Y, pageMin.Y), 1)
		);
	}

	float _pageProgress = 0;
	[Export(PropertyHint.Range, "0,1")]
	float PageProgress
	{
		get => _pageProgress;
		set
		{
			_pageProgress = Mathf.Clamp(value, 0, 1);
			if (pageContainer is null || buttonListControl is null)
				return;
			FitChildInRect(pageContainer,
				new Rect2(
					Size.X * (1.0f - _pageProgress) * Vector2.Right,
					Size
				)
			);
			buttonListControl.Scale = (0.5f + (1.0f - _pageProgress) * 0.5f) * Vector2.One;
			buttonListControl.Modulate = Colors.White.Lerp(Colors.Transparent, _pageProgress);
		}
	}

	private void UpdateLayout()
	{
		if (buttonListControl is null)
			return;
		FitChildInRect(buttonListControl,
			new Rect2(
				Vector2.Zero,
				Size
			)
		);
		PageProgress = _pageProgress;
	}
}
