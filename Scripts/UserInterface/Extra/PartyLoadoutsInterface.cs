using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

public partial class PartyLoadoutsInterface : Control
{
    [Export]
	HeroLoadoutEntry[] partyLoadoutPanels;
    [Export]
    Label[] partyUsernames;

    public override void _Ready()
    {
        GameAccount.ActiveAccountChanged += Clear;
        Clear();
    }

    struct MatchData
    {
        public MatchAttributes attributes;
        public string[] publicPlayers;
        public string[] privatePlayers;

        public struct MatchAttributes
        {
            [JsonPropertyName("GAMEMODE_s")]
            public string Gamemode;
        }
    }

    struct DisplayNameData
    {
        public string id;
        public string displayName;
        public Dictionary<string, PlatformData> externalAuths;

        public struct PlatformData
        {
            public string externalDisplayName;
        }
    }

    Dictionary<string, string> knownUsernames = [];

    private async void Connect()
    {
        Clear(true);
        try
        {
            var matchResponse = await FnWebAddresses.game
                .MakeRequest($"/fortnite/api/matchmaking/session/findPlayer/{GameAccount.activeAccount.accountId}")
                .SetAuthorisation(GameAccount.activeAccount.AuthHeader)
                .Send();
            var matchData = (await matchResponse.Content.ReadFromJsonAsync<MatchData[]>(Helpers.JsonOptions.Fields))?.FirstOrDefault();
            if(matchData?.attributes.Gamemode != "FORTPVE")
            {
                Clear();
                return;
            }
            List<string> allPlayers = [.. matchData?.publicPlayers, .. matchData?.privatePlayers];
            allPlayers.Remove(GameAccount.activeAccount.accountId);
            List<GameAccount> allAccounts = [];
            foreach(var player in allPlayers)
            {
                var account = GameAccount.GetOrCreateAccount(player);
                try
                {
                    GD.Print("fetching " + player);
                    var profile = account.GetProfile(FnProfileTypes.AccountItems);
                    await profile.Query(silent: true);
                    if (profile.hasProfile)
                        allAccounts.Add(account);
                }
                catch
                {
                    GD.Print("failed to fetch " + player);
                }
            }

            try
            {
                var unknownUsers = allAccounts.Select(a => a.accountId).Where(id => !knownUsernames.ContainsKey(id));
                var displayNameResponse = await FnWebAddresses.account
                    .MakeRequest($"/account/api/public/account?{string.Join("&", unknownUsers.Select(id => $"accountId={id}"))}")
                    .SetAuthorisation(GameAccount.activeAccount.AuthHeader)
                    .Send();
                if (displayNameResponse.IsSuccessStatusCode)
                {
                    var newDisplayNames = await displayNameResponse.Content.ReadFromJsonAsync<DisplayNameData[]>(Helpers.JsonOptions.Fields);
                    foreach (var nameData in newDisplayNames)
                    {
                        var username = nameData.externalAuths.Select(e => e.Value.externalDisplayName).FirstOrDefault(e => e is not null) ?? nameData.displayName;
                        if (username is not null)
                            knownUsernames.Add(nameData.id, username);
                    }
                }
            }
            catch (Exception e)
            {
                GD.PushError(e);
            }

            Clear();
            for (int i = 0; i < 3; i++)
            {
                if (allAccounts.Count <= i)
                    continue;
                partyLoadoutPanels[i].SetAccount(allAccounts[i]);
                if (knownUsernames.TryGetValue(allAccounts[i].accountId, out var username))
                    partyUsernames[i].Text = username;
                GD.Print($"assigned {allAccounts[i].accountId} ({username})");
            }
        }
        catch (Exception e)
        {
            GD.PushError(e);
            Clear();
        }
    }

    private void Clear() => Clear(false);
    private void Clear(bool animated)
    {
        for (int i = 0; i < 3; i++)
        {
            partyUsernames[i].Text = "";
            partyLoadoutPanels[i].ClearLoadout(animated);
        }
    }
}
