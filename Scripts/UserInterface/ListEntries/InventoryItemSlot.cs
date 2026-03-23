using Godot;
using System;
using System.Threading;

public partial class InventoryItemSlot : Node
{
	//todo: allow slots to show a greyed out preview of the target item under an empty slot
	[Export]
	bool showInspector = false;
	[Export]
	bool autoUpdateAccount = true;
	[Export]
	bool isSurvivorSlot;

	[ExportGroup("Nodes")]
	[Export]
	GameItemEntry entry;

	[Export]
	Control inspectArea;

	[Export]
	Control open;

	[Export]
	Control locked;

	[Export]
	Control buttonControl;

	[Export]
	ContextMenuHook ctxMenuHook;

	[ExportGroup("ContextMenus")]

	[Export]
	ContextComponentList itemCtx;

	[Export]
	ContextComponentList emptyCtx;

	[ExportGroup("Defaults")]
	[Export]
	string slotRequirement;

	[Export]
	string profileType;

	[Export]
	string itemTypeFilter;

	[Export]
	string defaultSquadName;

	[Export]
	int defaultSquadIndex;

	public event Action<InventoryItemSlot> OnItemChangeRequested;
	public event Action<InventoryItemSlot> OnSlotItemChanged;

	GameAccount overrideAccount;
	public GameProfile currentProfile { get; private set; }
	public GameItem slottedItem { get; private set; }
	Predicate<GameItem> predicateFilter;
	bool isEmpty = true;

	public static Predicate<GameItem> CreateSquadPredicate(string squadNameFilter, int squadIndexFilter) => item =>
			item?.attributes?["squad_id"]?.ToString() == squadNameFilter &&
			(int)item.attributes["squad_slot_idx"] == squadIndexFilter;

	public override void _Ready()
	{
		if (!string.IsNullOrWhiteSpace(defaultSquadName) && defaultSquadIndex >= 0)
			predicateFilter = CreateSquadPredicate(defaultSquadName, defaultSquadIndex);
		if (string.IsNullOrWhiteSpace(slotRequirement))
			slotRequirement = null;
		if (isSurvivorSlot)
			entry.useSquadForRating = true;
		SetLocked(true);
		GameAccount.ActiveAccountChanged += OnActiveAccountChanged;
		OnActiveAccountChanged();
	}



	public void SetSlotData(string profileId, string itemType, string squadName, int squadIndex, string newSlotRequirement = null) =>
		SetSlotData(profileId, itemType, CreateSquadPredicate(squadName, squadIndex), newSlotRequirement);

	public void SetSlotData(string profileId, string itemType, Predicate<GameItem> predicate, string newSlotRequirement = null)
	{
		profileType = profileId;
		itemTypeFilter = itemType;
		predicateFilter = predicate;
		slotRequirement = newSlotRequirement;

		if (currentProfile is not null)
			UpdateProfile();
	}

	void OnActiveAccountChanged()
	{
		if (autoUpdateAccount && overrideAccount == null)
			SetProfileFromAccount();
	}

	public void SetOverrideAccount(GameAccount account = null)
	{
		if (profileType is null || (profileType != "campaign" && account != null)) //only the campaign profile is publicly viewable
			return;
		overrideAccount = account;
		SetProfileFromAccount();
	}

	CancellationTokenSource setFromAccountCts;
	async void SetProfileFromAccount()
	{
		if (profileType is null)
			return;

		profileUpdateCts?.Cancel();
		setFromAccountCts = setFromAccountCts.CancelAndRegenerate(out var ct);

		var account = overrideAccount ?? GameAccount.ActiveAccount;
		var newProfile = await account.GetProfile(profileType).Query();
		if (ct.IsCancellationRequested)
			return;

		bool hadProfile = currentProfile is not null;
		if (hadProfile)
		{
			currentProfile.OnItemUpdated -= ProfileItemChanged;
			currentProfile.OnItemRemoved -= ProfileItemChanged;
		}

		currentProfile = newProfile.hasProfile ? newProfile : null;

		if (currentProfile is null)
		{
			if (slottedItem is not null)
			{
				entry.ClearItem();
				slottedItem = null;
				isEmpty = true;
				OnSlotItemChanged?.Invoke(this);
			}
			GD.Print("no profile");
			SetLocked(true);
			return;
		}

		currentProfile.OnItemUpdated += ProfileItemChanged;
		currentProfile.OnItemRemoved += ProfileItemChanged;

		UpdateProfile();
	}


	CancellationTokenSource profileUpdateCts;
	public async void UpdateProfile()
	{
		profileUpdateCts?.Cancel();

		if (currentProfile is null)
			return;

		profileUpdateCts = profileUpdateCts.CancelAndRegenerate(out var ct);

		await currentProfile.Query();
		if (!currentProfile.hasProfile || ct.IsCancellationRequested)
			return;

		if (slotRequirement is not null && currentProfile.GetFirstTemplateItem(slotRequirement) is null)
		{
			SetLocked(true);
			return;
		}
		SetLocked(false);

		await Helpers.WaitForFrame();
		if (ct.IsCancellationRequested)
			return;

		ValidateItem(currentProfile.GetFirstItem(itemTypeFilter, predicateFilter), true);
	}

	void SetLocked(bool value)
	{
		locked.Visible = value;
		if (value)
		{
			buttonControl.Visible = false;
			entry.Visible = false;
			inspectArea.Visible = false;
			open.Visible = false;
		}
		else
			SetEmpty(isEmpty);
	}

	void SetEmpty(bool value)
	{
		buttonControl.Visible = overrideAccount is null;
		ctxMenuHook.componentList = value ? emptyCtx : itemCtx;
		isEmpty = value;
		entry.Visible = !isEmpty;
		inspectArea.Visible = !isEmpty && showInspector;
		open.Visible = isEmpty;
	}

	void ProfileItemChanged(GameItem newItem)
	{
		if (predicateFilter is null)
			return;
		ValidateItem(newItem);
	}

	public void Inspect() => GameItemViewer.Instance.ShowItem(slottedItem);

	public void UpdateItem() => entry.UpdateItem();

	public void RequestChange()
	{
		if (Input.IsKeyPressed(Key.Shift))
			Inspect();
		else if (overrideAccount is null)
			OnItemChangeRequested?.Invoke(this);
	}

	void ValidateItem(GameItem newItem, bool knownMatch = false)
	{
		bool isMatch =
			newItem?.profile is not null &&
			(
				knownMatch ||
				(
					(itemTypeFilter is null || newItem.template?.Type == itemTypeFilter) &&
					(predicateFilter is null || predicateFilter(newItem))
				)
			);
		bool wasSlotted = newItem == slottedItem;

		if (isMatch)
		{
			if (wasSlotted)
				return;
			slottedItem = newItem;
			entry.SetItem(slottedItem);
			SetEmpty(false);
			OnSlotItemChanged?.Invoke(this);
		}
		else if (wasSlotted || knownMatch) // item was slotted, but no longer matches
		{
			entry.ClearItem();
			slottedItem = null;
			SetEmpty(true);
			OnSlotItemChanged?.Invoke(this);
		}
	}

	public override void _ExitTree()
	{
		GameAccount.ActiveAccountChanged -= OnActiveAccountChanged;
		if (currentProfile is not null)
		{
			currentProfile.OnItemUpdated -= ProfileItemChanged;
			currentProfile.OnItemRemoved -= ProfileItemChanged;
		}
	}

}
