using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class QuestGroupViewer : Control
{
    [Signal]
    public delegate void QuestUpdatedEventHandler();
    [Signal]
    public delegate void PressedEventHandler();

    [Export]
    CheckButton unselectedButton;
    [Export]
    QuestViewer questViewer;
    [Export]
    Control questNodeParent;
    [Export]
    Control noQuestNodesAlert;
    [Export]
    PackedScene questNodeScene;
    [Export]
    PackedScene questArrowScene;
    [Export]
    ScrollContainer scrollContainer;
    [Export]
    Control leftButtonParent;
    [Export]
    Control rightButtonParent;
    [Export(PropertyHint.ArrayType)]
    QuestNode[] questNodes = [];
    [Export(PropertyHint.ArrayType)]
    ShaderHook[] questArrows = [];

    List<QuestNode> questNodeList;
    List<ShaderHook> questArrowList;

    [Export]
    int maxNodesPerPage = 20;
	int nodesPerPage;
	List<QuestSlot> questDataList;
    QuestSlot firstUnlocked;
	int currentPage;
    int currentNodeIndex = 0;
    int currentQuestIndex = 0;
    bool isQuestOnPage = false;
    int maxPage;
    bool useArrows;

    public override void _Ready()
    {
        base._Ready();

        questNodeList = new(questNodes);
        questArrowList = new(questArrows);

        //start with 5 nodes and 5 arrows

        for (int i = questNodeList.Count; i < maxNodesPerPage; i++)
        {
            if (questArrowList.Count < i)
            {
                //add new arrow
                var newArrow = questArrowScene.Instantiate<ShaderHook>();
                questArrowList.Add(newArrow);
                questNodeParent.AddChild(newArrow);
            }

            //add new node
            var newNode = questNodeScene.Instantiate<QuestNode>();
            questNodeList.Add(newNode);
            questNodeParent.AddChild(newNode);
        }

        for (int i = 0; i < questNodeList.Count; i++)
        {
            int index = i;
            questNodeList[i].Pressed += () => OnQuestPressed(index);
        }
        ClearQuestNodes();
    }

    public void ClearQuestNodes()
    {
        for (int i = 0; i < questNodeList.Count; i++)
        {
            if (i > 0)
                questArrowList[i - 1].Visible = false;
            questNodeList[i].Visible = false;
        }
        leftButtonParent.Visible = false;
        rightButtonParent.Visible = false;
        //clear selected quest
    }


    public void SetQuestNodes(QuestGroupEntry questGroup)
    {
        var groupData = questGroup.questGroupData;
        useArrows = groupData.Sequence;

        questDataList = questGroup.questSlotList.Where(q => (q.isUnlocked || groupData.ShowLocked) && (!q.isClaimed || groupData.ShowComplete)).ToList();
        if (questDataList.FirstOrDefault()?.questTemplate.DisplayName.Contains("Endurance") ?? false)
            questDataList = [.. questDataList.OrderBy(q => int.Parse(q.questTemplate.DisplayName.Split(" ")[^1]))];
        firstUnlocked = questDataList.FirstOrDefault(q => q.isUnlocked && !q.isClaimed);

        nodesPerPage = maxNodesPerPage;
        if (questDataList.Count > nodesPerPage)
		{
            while ((questDataList.Count % nodesPerPage) < (nodesPerPage * 0.6f))
			{
				nodesPerPage--;
                if (nodesPerPage < (maxNodesPerPage * 0.6f))
                {
                    GD.Print("Nodes Per Page gone weird");
                    break;//this should never happen
                }
            }
		}
		else
			nodesPerPage = Mathf.Max(questDataList.Count, 1);
        currentQuestIndex = 0;

        if (questDataList.Count>0)
        {
            var focusNode = questDataList.FirstOrDefault(q => q.isUnlocked && !q.isClaimed, null) ?? questDataList[^1];
            if (!useArrows)
                focusNode = questDataList.FirstOrDefault(q => q.isPinned) ?? questDataList[0];
            currentQuestIndex = questDataList.IndexOf(focusNode);
        }

        questViewer.Visible = questDataList.Count > 0;
        scrollContainer.Visible = questDataList.Count > 0;
        noQuestNodesAlert.Visible = questDataList.Count == 0;
        currentPage = currentQuestIndex / nodesPerPage;
        maxPage = Mathf.Max((questDataList.Count - 1) / nodesPerPage, 0);
        SetPage(currentPage);
    }

    void OnQuestPressed(int nodeIndex)
    {
        currentNodeIndex = nodeIndex;
        int pageStartIndex = currentPage * nodesPerPage;
        currentQuestIndex = pageStartIndex + nodeIndex;
        isQuestOnPage = true;
        questViewer.SetupQuest(questDataList[currentQuestIndex]);
        if (questDataList[currentQuestIndex].isUnlocked)
            questDataList[currentQuestIndex].questItem.MarkItemSeen();
        EmitSignal(SignalName.Pressed);
    }

    public void NextPage() => AddPage(1);
    public void PrevPage() => AddPage(-1);
    public void AddPage(int offset) => SetPage(currentPage+offset);

	public async void SetPage(int newPage)
	{
        int prevPage = currentPage;
        currentPage = Mathf.Clamp(newPage, 0, maxPage);

        isQuestOnPage = false;
        QuestNode pressOnComplete = null;
        int pageStartIndex = currentPage * nodesPerPage;
        int pageLength = Mathf.Min(nodesPerPage, questDataList.Count - pageStartIndex);
        for (int i = 0; i < pageLength; i++)
        {
            var thisQuest = questDataList[pageStartIndex + i];
            questNodeList[i].Visible = true;
            //apply quest to node
            questNodeList[i].SetupQuestNode(thisQuest, unselectedButton.ButtonGroup, firstUnlocked != thisQuest && useArrows);
            isQuestOnPage |= pageStartIndex + i == currentNodeIndex;
            if (pageStartIndex + i == currentQuestIndex)
            {
                isQuestOnPage = true;
                pressOnComplete = questNodeList[i];
            }

            if (i > 0 && i <= questArrowList.Count)
            {
                if (useArrows)
                {
                    var prevQuest = questDataList[pageStartIndex + i - 1];
                    //set arrow effects
                    questArrowList[i - 1].SetShaderBool(prevQuest.isUnlocked && !prevQuest.isClaimed, "Animation");
                    questArrowList[i - 1].SetShaderBool(prevQuest.isClaimed, "UseCompleteColor");
                    questArrowList[i - 1].Visible = true;
                }
                else
                {
                    questArrowList[i - 1].Visible = false;
                }
            }
        }
        for (int i = pageLength; i < questNodeList.Count; i++)
        {
            questNodeList[i].Visible = false;
            if (i > 0 && i <= questArrowList.Count)
                questArrowList[i - 1].Visible = false;
        }

        leftButtonParent.Visible = currentPage > 0;
        rightButtonParent.Visible = currentPage < maxPage;

        await Helpers.WaitForFrame();
        await Helpers.WaitForFrame();

        if (prevPage > currentPage)
        {
            scrollContainer.ScrollHorizontal = 999999;
        }
        else if (prevPage < currentPage)
        {
            scrollContainer.ScrollHorizontal = 0;
        }
        if (!isQuestOnPage)
            unselectedButton.ButtonPressed = true;
        else
            pressOnComplete?.Press();
    }
}
