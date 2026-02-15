using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using XmppDotNet;

public partial class TimelineEventViewer : ModalWindow
{
    static TimelineEventViewer instance;
    [Export]
    QuestGroupEntry questGroupEntry;
    [Export]
    QuestGroupViewer questGroupViewer;
    [Export]
    Label nameLabel;
    [Export]
    Label descriptionLabel;
    [Export]
    Label timerPrefix;
    [Export]
    RefreshTimerHook timer;
    [Export]
    ShaderHook styleTarget;
    [Export]
    Control questButton;
    [Export]
    Control keyItemPanel;
    [Export]
    Control seasonPanel;
    [Export]
    GameItemEntry[] keyItems;
    [Export]
    GameItemEntry seasonLlama;
    [Export]
    GameItemEntry seasonModifier;
    [Export]
    GameItemEntry[] ventureModifiers;
    [Export]
    Godot.Collections.Dictionary<string, Texture2D> styles;
    [Export]
    VirtualTabBar questGroupTabs;

    QuestViewer questViewer;
    Control questNodePanel;
    List<QuestGroupData> activeQuestGroups = [];

    public override void _Ready()
	{
		instance = this;
        questViewer = questGroupViewer.GetNode<QuestViewer>("%QuestViewer");
        questNodePanel = questGroupViewer.GetNode<Control>("%QuestNodes");
        questGroupTabs.LatestTabChanged += SetQuestGroupIndex;
        base._Ready();
    }

	public static void ShowEvent(TimelineInterface.BaseEventMarker marker)
	{
        QuestSlot singleQuest = null;
        instance.activeQuestGroups.Clear();
        instance.questGroupTabs.Visible = false;

        if (marker is TimelineInterface.EventMarker eMarker)
        {
            if (eMarker.eventQuest is string eQuest)
            {
                var questTemplate = GameItemTemplate.Get(eQuest);
                singleQuest = questTemplate is null ? null : new QuestSlot(questTemplate);
            }
            else if (eMarker.questGroups is string[] qGroupPaths && qGroupPaths.Length > 0)
            {
                LoadQuestGroups(qGroupPaths);
            }
        }

        if(marker is TimelineInterface.QuestlineMarker qMarker)
        {
            if (QuestGroupCollectionData.CollectionData.TryGetValue("questlines", out var qCollection))
            {
                if (qCollection.QuestGroups.TryGetValue(qMarker.eventFlag, out var qGroup))
                    instance.activeQuestGroups.Add(qGroup);
                else if (qMarker.questGroups is string[] qGroupPaths && qGroupPaths.Length > 0)
                {
                    LoadQuestGroups(qGroupPaths);
                }
            }
        }

        if (instance.styleTarget is not null)
        {
            instance.styles.TryGetValue(marker.style, out var tex);
            instance.styleTarget.SetShaderTexture(tex, "background");
            instance.styleTarget.SelfModulate = marker.color;
        }

        instance.questGroupViewer.Visible = false;
        instance.questNodePanel.Visible = false;
        instance.keyItemPanel.Visible = false;

        if (singleQuest is not null)
        {
            instance.questGroupViewer.Visible = true;
            instance.questViewer.SetupQuest(singleQuest);
        }
        else if (instance.activeQuestGroups.Count > 0)
        {
            instance.SetQuestGroupIndex(0);
        }

        if (!instance.questGroupViewer.Visible)
        {
            instance.keyItemPanel.Visible = true;
            for (int i = 0; i < instance.keyItems.Length; i++)
            {
                instance.keyItems[i].Visible = marker.KeyGameItems.Length > i;
                if (instance.keyItems[i].Visible)
                    instance.keyItems[i].SetItem(marker.KeyGameItems[i]);
            }
        }

        if (marker.fromDate < DateTime.UtcNow)
        {
            instance.timer.SetTimerType(2);
            instance.timer.SetCustomRefreshTime(marker.toDate, marker.fromDate);
            instance.timerPrefix.Text = "Leaves in ";
        }
        else
        {
            instance.timer.SetTimerType(0);
            instance.timer.SetCustomRefreshTime(marker.fromDate);
            instance.timerPrefix.Text = "Returns in ";
        }

        instance.nameLabel.Text = marker.DisplayName;
        instance.descriptionLabel.Text = marker.description;
        instance.SetWindowOpen(true);
    }

    static void LoadQuestGroups(string[] qGroupPaths)
    {
        if (qGroupPaths.Length == 1 && !qGroupPaths[0].Contains('.'))
        {
            if (QuestGroupCollectionData.CollectionData.TryGetValue(qGroupPaths[0], out var qCollection))
            {
                instance.activeQuestGroups.AddRange(qCollection.QuestGroups.Values);
            }
        }
        else
        {
            foreach (var qGroupPath in qGroupPaths)
            {
                string qCollectionId = qGroupPath;
                string qGroupId = "";
                if (qCollectionId.Contains('.'))
                {
                    var split = qCollectionId.Split('.');
                    qCollectionId = split[0];
                    qGroupId = split[1];
                }
                if (QuestGroupCollectionData.CollectionData.TryGetValue(qCollectionId, out var qCollection))
                {
                    if (qGroupId == "")
                        instance.activeQuestGroups.Add(qCollection.QuestGroups.Values.First());
                    else if (qCollection.QuestGroups.TryGetValue(qGroupId, out var qGroup))
                        instance.activeQuestGroups.Add(qGroup);
                }
            }
        }
        if (instance.activeQuestGroups.Count > 1)
        {
            instance.questGroupTabs.Visible = true;
            instance.questGroupTabs.SetTabContents([..
                instance.activeQuestGroups.Select(g => new VirtualTabBar.TabData()
                    {
                        text = g.shortName ?? g.displayName
                    }
                )
            ]);
        }
    }

    public void SetQuestGroupIndex(int index)
    {
        if (index < 0 || index > activeQuestGroups.Count)
            return;
        var qGroupData = activeQuestGroups[index];
        questGroupEntry.SetupQuestGroup(qGroupData);
        if (instance.questGroupEntry.questSlotList.Count == 0)
        {
            qGroupData = qGroupData with { showLocked = true };
            instance.questGroupEntry.SetupQuestGroup(qGroupData);
        }

        instance.questGroupViewer.Visible = instance.questGroupEntry.questSlotList.Count > 0;
        instance.questNodePanel.Visible = instance.questGroupViewer.Visible;
        instance.keyItemPanel.Visible = !instance.questGroupViewer.Visible;

        if (instance.questGroupViewer.Visible)
        {
            instance.questGroupViewer.SetQuestNodes(instance.questGroupEntry);
        }
    }
}
