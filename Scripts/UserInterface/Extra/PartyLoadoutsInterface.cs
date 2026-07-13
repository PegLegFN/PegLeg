using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

public partial class PartyLoadoutsInterface : Control
{
	[Export]
	LineEdit targetFriendUsername;
	HeroLoadoutEntry primaryLoadoutPanel;
	[Export]
	Label primaryUsername;
	[Export]
	HeroLoadoutEntry[] partyLoadoutPanels;
	[Export]
	Label[] partyUsernames;

	public override void _Ready()
	{
		GameAccount.ActiveAccountChanged += Clear;
		Clear();
	}

	public override void _ExitTree()
	{
		GameAccount.ActiveAccountChanged -= Clear;
	}

#pragma warning disable CS0649 //Field is never assigned to, and will always have its default value
	struct MatchData
	{
		public MatchAttributes attributes { get; init; }
		public string[] publicPlayers { get; init; }
		public string[] privatePlayers { get; init; }

		public struct MatchAttributes
		{
			[JsonPropertyName("GAMEMODE_s")]
			public string Gamemode { get; init; }
		}
	}

	struct DisplayNameData
	{
		public string id { get; init; }
		public string displayName { get; init; }
		public Dictionary<string, PlatformData> externalAuths { get; init; }

		public struct PlatformData
		{
			public string externalDisplayName { get; init; }
		}
	}
#pragma warning restore CS0649 //Field is never assigned to, and will always have its default value

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
			GameAccount targetUser = GameAccount.ActiveAccount;
			string[] teammateIds = [];

			// Epic re-enabled findPlayer functionality!! :tada:
			// Plus, with Hestia, its the only approach needed
			if (teammateIds.Length == 0)
				teammateIds = [.. await GetTeammatesFromMatch()];
			//if (teammateIds.Length == 0)
			//	teammateIds = [.. await GetTeammatesFromParty()];
			if (teammateIds.Length == 0)
			{
				GD.Print("no players in match");
				Clear();
				return;
			}
			List<GameAccount> teammateAccounts = [];
			foreach (var player in teammateIds)
			{
				if (player == targetUser.accountId)
					continue;
				var account = GameAccount.GetOrCreateAccount(player);
				try
				{
					GD.Print("fetching " + player);
					var profile = account.GetProfile(FnProfileTypes.AccountItems);
					await profile.Query(silent: true);
					if (profile.hasProfile)
						teammateAccounts.Add(account);
				}
				catch
				{
					GD.Print("failed to fetch " + player);
				}
			}

			var unknownUsers = teammateAccounts.Select(a => a.accountId).Union([targetUser.accountId]).Where(id => !knownUsernames.ContainsKey(id)).ToArray();
			if (unknownUsers.Length > 0)
			{
				var displayNameResponse = await FnWebAddresses.EpicAccount
					.MakeRequest($"/account/api/public/account?{string.Join("&", unknownUsers.Select(id => $"accountId={id}"))}")
					.SetAccount(GameAccount.ActiveAccount)
					.Send();
				if (!await displayNameResponse.CheckForError())
				{
					try
					{
						var newDisplayNames = await displayNameResponse.ReadJson<DisplayNameData[]>();
						foreach (var nameData in newDisplayNames)
						{
							var username = nameData.externalAuths.Select(e => e.Value.externalDisplayName).FirstOrDefault(e => e is not null) ?? nameData.displayName;
							if (username is not null)
								knownUsernames.Add(nameData.id, username);
						}
					}
					catch (Exception e)
					{
						GD.PushError(e);
					}
				}
			}

			Clear();
			primaryLoadoutPanel?.SetAccount(targetUser);
			if (primaryUsername is not null && knownUsernames.TryGetValue(targetUser.accountId, out var primaryDisplayName))
				primaryUsername.Text = primaryDisplayName;
			for (int i = 0; i < 3; i++)
			{
				if (teammateAccounts.Count <= i)
					continue;
				partyLoadoutPanels[i].SetAccount(teammateAccounts[i]);
				if (knownUsernames.TryGetValue(teammateAccounts[i].accountId, out var displayName))
					partyUsernames[i].Text = displayName;
				GD.Print($"assigned {teammateAccounts[i].accountId} ({displayName})");
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

	public struct FriendData
	{
		public string accountId;
	}

#pragma warning disable CS0649 //Field is never assigned to, and will always have its default value
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
#pragma warning restore CS0649 //Field is never assigned to, and will always have its default value

	private async Task<IEnumerable<string>> GetTeammatesFromMatch()
	{
		var matchResponse = await FnWebAddresses.FortGame
			.MakeRequest($"/fortnite/api/matchmaking/session/findPlayer/{GameAccount.ActiveAccount.accountId}")
			.SetAccount(GameAccount.ActiveAccount)
			.Send();
		if (await matchResponse.CheckForError())
			return [];
		var matchDataList = await matchResponse.ReadJson<MatchData[]>();
		var matchData = matchDataList.FirstOrDefault(m => m.attributes.Gamemode == "FORTPVE");
		if(matchData.attributes.Gamemode != "FORTPVE")
		{
			matchData = matchDataList.FirstOrDefault(m => m.attributes.Gamemode == "FORTHESTIABEAUTY");
			if (matchData.attributes.Gamemode != "FORTHESTIABEAUTY")
				return [];
		}

		return matchData.publicPlayers.Union(matchData.privatePlayers).Distinct();
	}

	private async Task<IEnumerable<string>> GetTeammatesFromParty(GameAccount fromAccount = null)
	{
		fromAccount ??= GameAccount.ActiveAccount;
		var partyResponse = await FnWebAddresses.EpicParty
			.MakeRequest($"/party/api/v1/Fortnite/user/{fromAccount.accountId}")
			.SetAccount(GameAccount.ActiveAccount)
			.Send();
		if (await partyResponse.CheckForError())
			return [];
		var partyJson = await partyResponse.ReadJson();
		var partyData = partyJson.Deserialize<PartyCollection>();
		if ((partyData.current?.Length ?? 0) == 0)
			return [];
		return partyData.current[0].members.Select(m => m.accountId) ?? [];
	}

	private async void ConnectParty()
	{
		if (connecting)
			return;
		connecting = true;
		Clear(true);
		try
		{
			string[] allPlayers = [.. await GetTeammatesFromParty()];
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
				var displayNameResponse = await FnWebAddresses.EpicAccount
					.MakeRequest($"/account/api/public/account?{string.Join("&", unknownUsers.Select(id => $"accountId={id}"))}")
					.SetAccount(GameAccount.ActiveAccount)
					.Send();
				if (!await displayNameResponse.CheckForError())
				{
					var newDisplayNames = await displayNameResponse.ReadJson<DisplayNameData[]>(Helpers.JsonOptions.Fields);
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
		primaryUsername?.Text = "";
		primaryLoadoutPanel?.ClearLoadout(animated);
		for (int i = 0; i < 3; i++)
		{
			partyUsernames[i].Text = "";
			partyLoadoutPanels[i].ClearLoadout(animated);
		}
	}
}
