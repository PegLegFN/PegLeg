using Godot;
using System.Text.Json.Nodes;

public partial class ProcessSwitchingTabContainer : TabContainer
{
    [Export]
    Node altParent;
    Node defaultParent;
    [Export]
    bool hideInDevMode;

    public override void _Ready()
    {
        TabChanged += UpdateProcessingTab;
        foreach (var child in GetChildren())
        {
            child.ProcessMode = ProcessModeEnum.Disabled;
        }
        UpdateProcessingTab(CurrentTab);
        if(altParent is not null)
        {
            defaultParent = GetParent();
            UpdateDevState();
        }
    }

    private void ConfigChange(string section, string key, JsonValue value)
    {
        if (section != "advanced" && key != "developer")
            return;
        UpdateDevState();
    }

    void UpdateDevState()
    {
        bool isAltParented = (GetParent() == altParent);
        bool shouldBeAltParented = AppConfig.Get("advanced", "developer", false) == hideInDevMode;
        GD.Print($"alt: {shouldBeAltParented}");
        if (isAltParented == shouldBeAltParented)
            return;
        if (shouldBeAltParented)
        {
            Reparent(altParent);
        }
        else
        {
            Reparent(defaultParent);
        }
    }

    public override void _EnterTree()
    {
        if (altParent is not null)
        {
            AppConfig.OnConfigChanged += ConfigChange;
        }
    }

    public override void _ExitTree()
    {
        AppConfig.OnConfigChanged -= ConfigChange;
    }

    Node activeTab = null;
    private void UpdateProcessingTab(long tab)
    {
        var tabChild = GetChild((int)tab);
        if (activeTab is not null)
            activeTab.ProcessMode = ProcessModeEnum.Disabled;
        activeTab = tabChild;
        activeTab.ProcessMode = ProcessModeEnum.Inherit;
    }
}
