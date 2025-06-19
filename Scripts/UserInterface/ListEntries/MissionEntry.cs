using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class MissionEntry : Control, IRecyclableEntry
{
    [Signal]
    public delegate void NameChangedEventHandler(string name);
    [Signal]
    public delegate void DescriptionChangedEventHandler(string description);
    [Signal]
    public delegate void LocationChangedEventHandler(string location);
    [Signal]
    public delegate void PowerLevelChangedEventHandler(string powerLevel);
    [Signal]
    public delegate void TooltipChangedEventHandler(string tooltip);
    [Signal]
    public delegate void IconChangedEventHandler(Texture2D icon);
    [Signal]
    public delegate void BackgroundChangedEventHandler(Texture2D background);
    [Signal]
    public delegate void BackgroundVisibleEventHandler(bool visible);
    [Signal]
    public delegate void VenturesIndicatorVisibleEventHandler(bool visible);
    [Signal]
    public delegate void TheaterCategoryChangedEventHandler(string theatreCat);
    [Signal]
    public delegate void TheaterColorChangedEventHandler(Color theatreCol);
    [Signal]
    public delegate void TheaterNameChangedEventHandler(string theatreName);

    [Export]
    bool controlModifierParentLayoutProps = true;
    [Export]
    bool fullItems = false;

    [Export]
    Control alertModifierLayout;
    [Export]
    Control alertModifierParent;

    [Export]
    Control missionRewardLayout;
    [Export]
    Control missionRewardParent;

    [Export]
    Control alertRewardLayout;
    [Export]
    Control alertRewardParent;

    [Export]
    Control highlightedRewardParent;

    [Export]
    Texture2D defaultBackground;

    public GameMission currentMission { get; private set; } = null;
    IMissionHighlightProvider highlightedItemProvider;

    public Control node => this;

    public override void _Ready()
    {
        GameAccount.ActiveAccountChanged += RefreshRewardNotifications;
        AppConfig.OnConfigChanged += OnConfigChanged;
        EmitSignal(SignalName.BackgroundVisible, AppConfig.Get("missions", "show_background", true));
    }

    private void OnConfigChanged(string section, string key, JsonValue val)
    {
        if (section != "missions")
            return;
        if (key == "show_background")
            EmitSignal(SignalName.BackgroundVisible, !val.TryGetValue(out bool show) || show);
    }

    public override void _ExitTree()
    {
        GameAccount.ActiveAccountChanged -= RefreshRewardNotifications;
    }

    private void RefreshRewardNotifications()
    {
        currentMission?.UpdateRewardNotifications(true);
    }

    IRecyclableElementProvider<GameMission> missionProvider;
    public void SetRecyclableElementProvider(IRecyclableElementProvider provider)
    {
        if(provider is IRecyclableElementProvider<GameMission> newProvider)
            missionProvider = newProvider;
    }

    public void SetRecycleIndex(int index)
    {
        if (missionProvider is null)
            return;
        SetMission(missionProvider.GetRecycleElement(index));
        currentMission?.UpdateRewardNotifications();
    }

    public void SetMission(GameMission mission)
	{
        currentMission = mission;

        EmitSignal(SignalName.NameChanged, currentMission.DisplayName);
        EmitSignal(SignalName.DescriptionChanged, currentMission.Description);
        EmitSignal(SignalName.LocationChanged, currentMission.Location);
        EmitSignal(SignalName.IconChanged, currentMission.missionGenerator.GetTexture(FnItemTextureType.Icon));
        EmitSignal(SignalName.PowerLevelChanged, currentMission.PowerLevel.ToString());
        EmitSignal(SignalName.BackgroundChanged, currentMission.backgroundTexture ?? defaultBackground);

        EmitSignal(SignalName.TheaterNameChanged, currentMission.TheaterName);
        EmitSignal(SignalName.VenturesIndicatorVisible, currentMission.TheaterCat == "v");
        EmitSignal(SignalName.TheaterCategoryChanged, currentMission.TheaterCat.ToUpper());
        EmitSignal(SignalName.TheaterColorChanged, currentMission.TheaterCat switch
        {
            "s"=>Colors.Aquamarine,
            "p" => Colors.ForestGreen,
            "c" => Colors.SandyBrown,
            "t" => Colors.MediumPurple,
            "v" => Colors.Cyan,
            _ =>Colors.Transparent
        });

        string eventFlag = currentMission.tileData["requirements"]["eventFlag"].ToString();
        bool hasEventFlag = !string.IsNullOrWhiteSpace(eventFlag);

        //TODO: if a mission has a quest requirement, mission entries should have the option of listing it
        List<string> tooltipDescriptions =
        [
            currentMission.Description ?? "",
            //"Item Id: " + item.templateId,
        ];
        if (mission.SearchTags is JsonArray tagArray && tagArray.Count > 0)
            tooltipDescriptions.Add("Search Tags: " + tagArray.Select(t => t?.ToString()).Except(new string[] { currentMission.DisplayName }).ToArray().Join(", "));

        EmitSignal(SignalName.TooltipChanged, CustomTooltip.GenerateSimpleTooltip(
            currentMission.DisplayName,
            null,
            tooltipDescriptions.ToArray()
            ));

        if(alertModifierLayout is not null && alertModifierParent is not null)
        {
            if (currentMission.alertModifiers.Length > 0)
            {
                for (int i = 0; i < alertModifierParent.GetChildCount(); i++)
                {
                    var alertChild = alertModifierParent.GetChild<Control>(i);
                    if (currentMission.alertModifiers.Length <= i)
                    {
                        alertChild.Visible = false;
                        alertChild.ProcessMode = ProcessModeEnum.Disabled;
                        continue;
                    }

                    alertChild.Visible = true;
                    alertChild.ProcessMode = ProcessModeEnum.Inherit;

                    if (alertChild is TextureRect textureChild)
                    {
                        var modifierTemplate = currentMission.alertModifiers[i].template;
                        textureChild.Texture = modifierTemplate.GetTexture();
                        textureChild.TooltipText = modifierTemplate.DisplayName;
                    }
                    else if (alertChild is GameItemEntry gameItemChild)
                    {
                        gameItemChild.SetItem(currentMission.alertModifiers[i]);
                    }
                }

                if (controlModifierParentLayoutProps)
                {
                    alertModifierParent.SizeFlagsHorizontal = currentMission.alertModifiers.Length == 5 ? SizeFlags.ShrinkCenter : SizeFlags.ExpandFill;
                    if (currentMission.alertModifiers.Length == 6)
                    {
                        var seventhChild = alertModifierParent.GetChild(6) as Control;
                        seventhChild.Visible = true;
                        seventhChild.SelfModulate = Colors.Transparent;
                    }
                }

                alertModifierLayout.Visible = true;
                alertModifierLayout.ProcessMode = ProcessModeEnum.Inherit;
            }
            else
            {
                alertModifierLayout.Visible = false;
                alertModifierLayout.ProcessMode = ProcessModeEnum.Disabled;
            }
        }

        if (missionRewardLayout is not null && missionRewardParent is not null)
        {
            var rewards = fullItems ?
                currentMission.rewardItems :
                currentMission.rewardItems
                    .Where(r => r.template.DisplayName != "Gold" && r.template.DisplayName != "Venture XP")
                    .ToArray();
            if (rewards.Length > 0)
            {
                ApplyItems(rewards, missionRewardParent);

                missionRewardLayout.Visible = true;
                missionRewardLayout.ProcessMode = ProcessModeEnum.Inherit;
            }
            else
            {
                missionRewardLayout.Visible = false;
                missionRewardLayout.ProcessMode = ProcessModeEnum.Disabled;
            }
        }

        if (alertRewardLayout is not null && alertRewardParent is not null)
        {
            var rewards = fullItems ?
                currentMission.alertRewardItems :
                currentMission.alertRewardItems
                    .Where(r => r.template.DisplayName != "Venture XP")
                    .ToArray();
            if (rewards.Length > 0)
            {
                ApplyItems(rewards, alertRewardParent);

                alertRewardLayout.Visible = true;
                alertRewardLayout.ProcessMode = ProcessModeEnum.Inherit;
            }
            else
            {
                alertRewardLayout.Visible = false;
                alertRewardLayout.ProcessMode = ProcessModeEnum.Disabled;
            }
        }

        UpdateHighlightedItems();
    }

    public void SetHighlightProvider(IMissionHighlightProvider provider)
    {
        if(highlightedItemProvider is not null)
        {
            highlightedItemProvider.OnHighlightedItemFilterChanged -= UpdateHighlightedItems;
        }
        highlightedItemProvider = provider;
        if (highlightedItemProvider is not null)
        {
            highlightedItemProvider.OnHighlightedItemFilterChanged += UpdateHighlightedItems;
        }
        UpdateHighlightedItems();
    }

    void UpdateHighlightedItems()
    {
        if (highlightedRewardParent is null || currentMission is null)
            return;
        if (highlightedItemProvider?.HighlightedItemFilter is Predicate<GameItem> predicate)
        {
            var rewards = fullItems ?
                currentMission.allItems :
                currentMission.allItems
                    .Where(r => r.template.DisplayName != "Gold" && r.template.DisplayName != "Venture XP")
                    .OrderBy(r => -r.sortingTemplate.RarityLevel)
                    .ThenBy(r => -r.quantity)
                    .ToArray();
            ApplyItems(rewards.Where(item => predicate(item)).ToArray(), highlightedRewardParent);
            return;
        }
        ApplyItems(Array.Empty<GameItem>(), highlightedRewardParent);
    }

    static void ApplyItems(GameItem[] itemArray, Control parent)
    {
        for (int i = 0; i < parent.GetChildCount(); i++)
        {
            var controlChild = parent.GetChild<GameItemEntry>(i);
            if (itemArray.Length <= i)
            {
                controlChild.Visible = false;
                controlChild.ProcessMode = ProcessModeEnum.Disabled;
                continue;
            }
            controlChild.Visible = true;
            controlChild.ProcessMode = ProcessModeEnum.Inherit;

            bool isRewardBundle = itemArray[i].template.Name.ToLower().StartsWith("zcp_");
            controlChild.addXToAmount = isRewardBundle;
            controlChild.compactifyAmount = !isRewardBundle;
            controlChild.preventInteractability = isRewardBundle;
            controlChild.SetItem(itemArray[i]);
        }
    }

    public void InspectMission() => MissionViewer.ShowMission(currentMission);
}

public interface IMissionHighlightProvider
{
    public event Action OnHighlightedItemFilterChanged;
    public Predicate<GameItem> HighlightedItemFilter { get; }
}
