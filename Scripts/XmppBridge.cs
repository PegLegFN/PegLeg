using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

public partial class XmppBridge : Control
{
    [Export]
    LineEdit status;
    [Export]
    TextEdit partyData;
    [Export]
    TextEdit memberData;
    [Export]
    PackedScene accountScene;
    [Export]
    Control accountParent;

    GameAccount account;
    List<string> listedAccounts = [];
    string selectedMember;

    public override async void _Ready()
    {
        var win = GetWindow();
        win.ContentScaleSize = (Vector2I)Size;
        win.Borderless = false;
        XmppManager.OnUserStatusChanged += TryAddAccount;
        AppConfig.PreloadConfig();
        var lastUsedId = AppConfig.Get<string>("account", "lastUsed");
        account = GameAccount.GetOrCreateAccount(lastUsedId);
        account.XmppManager.OnPartyUpdated += UpdatePartyData;
        account.XmppManager.OnPartyMemberUpdated += UpdateMemberData;
        AddAccount(account.accountId, "Unset");
        await account.SetAsActiveAccount();
        await account.FetchFriends();
        foreach (var f in account.Friends)
        {
            AddAccount(f.accountId, "Unknown/Offline");
        }
        await GameAccount.ActiveAccount.XmppManager.Connect();
        //GD.Print(lastUsedAccount.AuthToken);
        //var party = await FnWebAddresses.party
        //    .MakeRequest($"/party/api/v1/Fortnite/user/{lastUsedAccount.accountId}")
        //    .SetAuthorisation(lastUsedAccount.AuthHeader)
        //    .Send();
        //var partyJson = await party.Content.ReadAsStringAsync();
        //GD.Print(partyJson);
    }

    private void UpdateMemberData(PartyData.Member obj)
    {
        if (selectedMember == obj.account_id)
        {
            CallDeferred(nameof(AccountSelected), selectedMember);
        }
    }

    private void TryAddAccount(string arg1, string arg2)
    {
        CallDeferred(nameof(AddAccount), arg1, arg2);
    }

    async void AddAccount(string acc, string status)
    {
        if (listedAccounts.Contains(acc))
            return;
        var newAccount = GameAccount.GetOrCreateAccount(acc);
        listedAccounts.Add(acc);
        await account.Authenticate();
        await newAccount.UpdateIconTask();
        var newEntry = accountScene.Instantiate<GameAccountEntry>();
        accountParent.AddChild(newEntry);
        newEntry.SetAccount(newAccount);
        newEntry.SetStatus(status);
        newEntry.Pressed += AccountSelected;
    }

    private void AccountSelected(string accountId)
    {
        var memberDict = account.XmppManager.Party?.members;
        memberData.Text = "";
        if (memberDict is null)
            return;
        //GD.Print(string.Join(", ", memberDict.Values.Select(m => m.account_id)));
        if (!memberDict.TryGetValue(accountId, out var member))
            return;
        selectedMember = accountId;

        var scrollH = memberData.ScrollHorizontal;
        var scrollV = memberData.ScrollVertical;
        memberData.Text = JsonSerializer.Serialize(member, Helpers.JsonOptions.CamelCase);
        memberData.ScrollHorizontal = scrollH;
        memberData.ScrollVertical = scrollV;
    }

    private void UpdatePartyData()
    {
        CallDeferred(nameof(PartyText), string.Join("\n", account.XmppManager.Party.meta.Select(kvp => $"{kvp.Key} = {kvp.Value}")));
        //partyData.Text = string.Join("\n", obj.Select(kvp=>$"{kvp.Key} = {kvp.Value}"));
    }

    Dictionary<string, string> missionData = [];
    public void CaptureMissions()
    {
        var meta = GameAccount.ActiveAccount.XmppManager.Party.members[GameAccount.ActiveAccount.accountId].meta;
        missionData = [];
        TransferMeta(meta, "Default:CampaignInfo_j");
        TransferMeta(meta, "Default:ZoneInstanceId_s");
    }

    void TransferMeta(Dictionary<string,string> meta, string key)
    {
        if (meta.TryGetValue(key, out var val))
            missionData[key] = val;
    }

    public async void PatchMissions()
    {
        await GameAccount.ActiveAccount.XmppManager.SendPartyMemberPatch(missionData);
    }

    void PartyText(string text)
    {
        var scrollH = partyData.ScrollHorizontal;
        var scrollV = partyData.ScrollVertical;
        partyData.Text = text;
        partyData.ScrollHorizontal = scrollH;
        partyData.ScrollVertical = scrollV;
    }

    public async void Connect()
    {
        //await GameAccount.activeAccount.XmppManager.SendStatus((string)null);
    }

    public async void SetStatus()
    {
        await GameAccount.ActiveAccount.XmppManager.SendStatus(status.Text);
    }
}
