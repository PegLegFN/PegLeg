using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

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
    bool connecting = false;

    private async void Connect()
    {
        if (connecting)
            return;
        connecting = true;
        Clear(true);
        try
        {
            string[] allPlayers = [..await GetTeammatesFromMatch()];
            if (allPlayers.Length == 0)
                allPlayers = [.. await GetTeammatesFromParty()];
            if (allPlayers.Length == 0)
            {
                GD.Print("no players in match or party");
                Clear();
                return;
            }
            //var matchResponse = await FnWebAddresses.game
            //    .MakeRequest($"/fortnite/api/matchmaking/session/findPlayer/{GameAccount.activeAccount.accountId}")
            //    .SetAuthorisation(GameAccount.activeAccount.AuthHeader)
            //    .Send();
            //var matchData = (await matchResponse.Content.ReadFromJsonAsync<MatchData[]>(Helpers.JsonOptions.Fields))?.FirstOrDefault();
            //if(matchData?.attributes.Gamemode != "FORTPVE")
            //{
            //    Clear();
            //    return;
            //}
            //List<string> allPlayers = [.. matchData?.publicPlayers, .. matchData?.privatePlayers];
            //allPlayers.Remove(GameAccount.activeAccount.accountId);
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
        finally
        {
            connecting = false;
        }
    }

    struct PartyCollection
    {
        public PartyData[] current;
    }
    struct PartyData
    {
        public PartyMember[] members;

        public struct PartyMember
        {
            [JsonPropertyName("account_id")]
            public string accountId;
        }
    }

    private async Task<IEnumerable<string>> GetTeammatesFromMatch()
    {
        var matchResponse = await FnWebAddresses.game
            .MakeRequest($"/fortnite/api/matchmaking/session/findPlayer/{GameAccount.activeAccount.accountId}")
            .SetAuthorisation(GameAccount.activeAccount.AuthHeader)
            .Send();
        var matchData = ((await matchResponse.Content.ReadFromJsonAsync<MatchData[]>(Helpers.JsonOptions.Fields))?.FirstOrDefault()).Value;
        if (matchData.attributes.Gamemode != "FORTPVE")
            return [];
        matchData.publicPlayers ??= matchData.privatePlayers;
        return matchData.publicPlayers?.Union(matchData.privatePlayers ?? []).Distinct().Except([GameAccount.activeAccount.accountId]) ?? [];
    }

    private async Task<IEnumerable<string>> GetTeammatesFromParty()
    {
        var partyResponse = await FnWebAddresses.party
            .MakeRequest($"/party/api/v1/Fortnite/user/{GameAccount.activeAccount.accountId}")
            .SetAuthorisation(GameAccount.activeAccount.AuthHeader)
            .Send();
        var partyData = await partyResponse.Content.ReadFromJsonAsync<PartyCollection>(Helpers.JsonOptions.Fields);
        if (partyData.current.Length == 0)
            return [];
        return partyData.current[0].members.Select(m => m.accountId).Except([GameAccount.activeAccount.accountId]);
    }

    private async void ConnectParty()
    {
        if (connecting)
            return;
        connecting = true;
        Clear(true);
        try
        {
            string[] allPlayers = [..await GetTeammatesFromParty()];
            if (allPlayers.Length == 0)
            {
                GD.Print("no players in party");
                Clear();
                return;
            }
            //var partyResponse = await FnWebAddresses.party
            //    .MakeRequest($"/party/api/v1/Fortnite/user/{GameAccount.activeAccount.accountId}")
            //    .SetAuthorisation(GameAccount.activeAccount.AuthHeader)
            //    .Send();
            //var partyData = await partyResponse.Content.ReadFromJsonAsync<PartyCollection>(Helpers.JsonOptions.Fields);
            //if (partyData.current.Length == 0)
            //{
            //    Clear();
            //    return;
            //}
            //List<string> allPlayers = [.. partyData.current[0].members.Select(m => m.accountId)];
            //allPlayers.Remove(GameAccount.activeAccount.accountId);
            List<GameAccount> allAccounts = [];
            foreach (var player in allPlayers)
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
        finally
        {
            connecting = false;
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
