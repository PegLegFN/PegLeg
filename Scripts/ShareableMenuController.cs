using Godot;
using GDStringDict = Godot.Collections.Dictionary<string, string>;

public partial class ShareableMenuController : Control
{
	[Export]
	string bootScenePath = "res://Scenes/boot_scene.tscn";
	[Export]
	TabContainer tabContainer;
	[Export]
	Button shareButton;
	[Export]
	OptionButton publishOptions;
	[Export]
	Button publishButton;

	GDStringDict currentPublishTriggers;
	string currentShareTrigger;


	public override void _Ready()
	{
		tabContainer.TabChanged += TabSelected;
		shareButton.Pressed += CopySelected;
		publishButton.Pressed += PublishSelected;
		TabSelected(tabContainer.CurrentTab);
	}

	void TabSelected(long _)
	{
		var node = tabContainer.GetCurrentTabControl();
		currentShareTrigger = node.GetMetaOrDefault<string>("shareTrigger");
		currentPublishTriggers = node.GetMetaOrDefault<GDStringDict>("publishTriggers");
		if(currentPublishTriggers is not null && currentPublishTriggers.Count > 0)
		{
			publishOptions.Visible = true;
			publishButton.Disabled = false;
			publishOptions.Clear();
			//publishOptions.Disabled = currentPublishTriggers.Count <= 1;

			int idx = 0;
			foreach (var kvp in currentPublishTriggers)
			{
				if (!TriggerInstance.HasTrigger(kvp.Value))
					continue;
				publishOptions.AddItem(kvp.Key);
				publishOptions.SetItemMetadata(idx, kvp.Value);
				idx++;
			}
			if (idx == 0)
			{
				publishOptions.Visible = false;
				publishButton.Disabled = true;
			}
		}
		else
		{
			publishOptions.Visible = false;
			publishButton.Disabled = true;
		}
	}

	public void CopySelected()
	{
		if (currentShareTrigger is not null)
			TriggerInstance.Trigger(currentShareTrigger, ["force"]);
	}

	public void PublishSelected()
	{
		if (currentPublishTriggers is null)
			return;
		var publishTrigger = publishOptions.GetItemMetadata(publishOptions.Selected).AsString();
		GD.Print($"Trying to publish {publishTrigger}");
		TriggerInstance.Trigger(publishTrigger, ["force"]);
	}

	public void BackToDesktop()
	{
		AppConfig.Set("core", "shareMenu", false);
		GetTree().ChangeSceneToFile(bootScenePath);
	}
}
