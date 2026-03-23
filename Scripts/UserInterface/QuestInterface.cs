using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public partial class AppConfig
{
    public partial class AdvancedConfig
    {
        public bool suspendQuests;
    }
}

public partial class QuestInterface : Control
{
    [Export]
    QuestGroupViewer questGroupViewer;
    [Export]
    Control foldoutParent;
    [Export]
    PackedScene foldoutScene;
    [Export]
    PackedScene questGroupScene;
    [Export]
    Control questListLayout;
    [Export]
    Control loadingIcon;

    List<Foldout> questGroupCollections = [];
    List<QuestGroupEntry> questGroups = [];

    public override void _Ready()
	{
        VisibilityChanged += () =>
        {
            if (IsVisibleInTree())
                LoadQuests();
        };
        //GD.Print("questCollections: " + questGroupCollectionData.Length);
        RefreshTimerController.OnDayChanged += ReloadQuests;
        GameAccount.ActiveAccountChanged += ReloadQuests;
        ReloadQuests();
    }

    private async void ReloadQuests()
    {
        foreach (var node in questGroupCollections)
        {
            node.QueueFree();
        }
        questGroupCollections.Clear();

        //check if in mission and quests are outdated
        //if so, start repeated check if in mission every 15 seconds (force query campaign profile)
        //when no longer in mission, claim any completed quests immediately, then resume reloading quests

        if (!AppConfig.Get("advanced", "suspend_quests", false))
        {
            await GameAccount.ActiveAccount.ClientQuestLoginCampaign();
            if (AppConfig.Get("advanced", "bulk_quest_refresh", false))
                BulkClientQuestLoginCampaign(GameAccount.ActiveAccount);
        }

        questsDirty = true;
        if (IsVisibleInTree())
            LoadQuests();
    }

    private static async void BulkClientQuestLoginCampaign(GameAccount except)
    {
        await Task.WhenAll(GameAccount.OwnedAccounts.Except([except]).Select(a => a.ClientQuestLoginCampaign()));
    }

    public override void _ExitTree()
    {
        RefreshTimerController.OnDayChanged -= ReloadQuests;
        GameAccount.ActiveAccountChanged -= ReloadQuests;
    }

    public void ClearPinnedQuests() => 
        GameAccount.ActiveAccount.ClearPinnedQuests();

    bool questsDirty = true;
    SemaphoreSlim loadQuestsSemaphore = new(1);
    async void LoadQuests()
    {
        using var st = await loadQuestsSemaphore.AwaitToken(() => {
            loadingIcon.Visible = false;
            questListLayout.Visible = true;
        });
        if (!questsDirty)
            return;

        await GameAccount.ActiveAccount.GetProfile(FnProfileTypes.AccountItems).Query();
        await GameCalender.Check();

        questGroupViewer.Visible = false;
        questListLayout.Visible = false;
        loadingIcon.Visible = true;

        while (questsDirty)
        {
            questsDirty = false;
            foreach (var node in questGroupCollections)
            {
                node.QueueFree();
            }
            questGroupCollections.Clear();

            ButtonGroup questButtonGroup = new();
            foreach (var collection in QuestGroupCollectionData.CollectionData.Values.OrderBy(col => col.priority))
            {
                //create foldout
                var foldout = foldoutScene.Instantiate<Foldout>();
                foldout.SetFoldoutName(collection.displayName);
                List<QuestGroupEntry> groupsInFoldout = [];
                foreach (var group in collection.QuestGroups.Values)
                {
                    var groupEntry = questGroupScene.Instantiate<QuestGroupEntry>();
                    groupEntry.SetupQuestGroup(group);
                    if (!groupEntry.HasQuests)
                    {
                        //groupEntry.QueueFree();
                        continue;
                    }
                    groupsInFoldout.Add(groupEntry);
                    questGroups.Add(groupEntry);
                    groupEntry.LinkButtonGroup(questButtonGroup);
                    //foldout.GetInstanceId();
                    groupEntry.Pressed += () =>
                    {
                        questGroupViewer.Visible = true;
                        questGroupViewer.SetQuestNodes(groupEntry);
                    };
                    groupEntry.NotificationVisible += _ =>
                    {
                        foldout.SetNotification(groupsInFoldout.Any(g => g.HasNotification));
                    };
                    foldout.AddFoldoutChild(groupEntry);
                }

                var timer = foldout.GetNode<RefreshTimerHook>("%RefreshTimerContainer");
                if (timer is not null)
                {
                    timer.Visible = true;
                    switch (collection.timer)
                    {
                        case QuestTimerMode.Weekly:
                            timer.SetTimerType(2);
                            break;
                        case QuestTimerMode.Daily:
                            timer.SetTimerType(1);
                            break;
                        case QuestTimerMode.None:
                            timer.Visible = false;
                            break;
                        case QuestTimerMode.Event:
                            var activeFlag = collection.eventFlags.FirstOrDefault(GameCalender.EventFlagActive);
                            if (GameCalender.TryGetFlagRange(activeFlag, out var startDate, out var endDate))
                                timer.SetCustomRefreshTime(endDate, startDate);
                            timer.Visible = activeFlag is not null;
                            break;
                    }
                }

                foldout.SetNotification(groupsInFoldout.Any(g => g.HasNotification));
                foldoutParent.AddChild(foldout);
                questGroupCollections.Add(foldout);
            }
        }
    }
}
