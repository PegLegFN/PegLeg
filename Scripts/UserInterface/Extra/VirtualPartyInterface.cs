using Godot;
using System;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

public partial class VirtualPartyInterface: Control
{

    public override void _Ready()
    {
        GameAccount.ActiveAccountChanged += UpdateAccount;
    }

    JsonObject currentParty;

    private async void UpdateAccount()
    {
        currentParty = null;
        var acc = GameAccount.ActiveAccount;
        var req = await FnWebAddresses.EpicParty
            .MakeRequest($"/party/api/v1/Fortnite/user/{acc.accountId}")
            .SetAccount(acc)
            .Send();
        if (!req.IsSuccessStatusCode)
            return;
        var party = (await req.Content.ReadFromJsonAsync<JsonObject>())["current"].AsArray().FirstOrDefault()?.AsObject();
        if(party is not null)
        {
            currentParty = party;
            return;
        }
        //create party
        JoinParty("");
    }

    public void JoinParty(string partyId)
    {
        if (currentParty == null)
            return;
        //join party
        SetFortStats();
    }

    public void SetFortStats()
    {
        if (currentParty == null)
            return;
        //party must have more than 1 member
        //patch self party member meta with fort stat info
    }
}
