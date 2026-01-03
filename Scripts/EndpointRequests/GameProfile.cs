using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

static class FnProfileTypes
{
    public const string AccountItems = "campaign";
    public const string Backpack = "theater0";
    public const string Storage = "outpost0";
    public const string VentureBackpack = "theater2";
    public const string PeopleCollection = "collection_book_people0";
    public const string SchematicCollection = "collection_book_schematics0";
    public const string CosmeticInventory = "athena";
    public const string Common = "common_core";
}

public class GameProfile
{
    public GameProfile(GameAccount account, string profileId)
    {
        this.account = account;
        this.profileId = profileId;
    }

    public void InvalidateProfile()
    {
        rvn = -1;
        foreach (var item in items.Values)
        {
            item.DisconnectFromProfile();
        }
        items.Clear();
        groupedItems.Clear();
        statAttributes = null;
    }

    int rvn = -1;
    public bool hasProfile => rvn >= 0;
    public GameAccount account { get; private set; }
    public string profileId { get; private set; }
    public JsonObject statAttributes { get; private set; }

    Dictionary<string, GameItem> items = [];
    Dictionary<string, List<GameItem>> groupedItems = new(StringComparer.OrdinalIgnoreCase);

    public event Action OnProfileChanged;
    public event Action OnStatsChanged;
    public event Action<string> OnStatChanged;
    public event Action<GameItem> OnItemAdded;
    public event Action<GameItem> OnItemUpdated;
    public event Action<GameItem> OnItemRemoved;
    public event Action<string, GameItem> OnItemReassociated;

    IEnumerable<GameItem> GetItemSubset(string type)
    {
        if (type is null)
            return items.Values;
        if (groupedItems.TryGetValue(type, out var subset))
            return subset;
        return [];
    }

    public GameItem GetItem(string uuid) => uuid is not null && items.TryGetValue(uuid, out GameItem value) ? value : null;
    public GameItem[] GetItems(Predicate<GameItem> predicate) => GetItems(null, predicate);
    public GameItem[] GetItems(string type = null, Predicate<GameItem> predicate = null)
    {
        var typedItems = GetItemSubset(type);
        if (predicate is null)
            return [.. typedItems];
        return [.. typedItems.Where(predicate.ToFunc())];
    }

    public GameItem[] GetTemplateItems(string templateId = null, Predicate<GameItem> predicate = null)
    {
        string type = templateId.Split(":")[0];
        return GetItems(type, item => item.templateId == templateId && predicate.Try(item));
    }

    public GameItem GetFirstItem(Predicate<GameItem> predicate) => GetFirstItem(null, predicate);
    public GameItem GetFirstItem(string type = null, Predicate<GameItem> predicate = null)
    {
        var typedItems = GetItemSubset(type);
        if (predicate is null)
            return typedItems.FirstOrDefault();
        return typedItems.FirstOrDefault(predicate.ToFunc());
    }
    public GameItem GetFirstTemplateItem(string templateId, Predicate<GameItem> predicate = null)
    {
        if (templateId is null)
            return null;
        return GetFirstItem(
            templateId.Split(":")[0],
            item =>
                item?.templateId.Equals(templateId, StringComparison.OrdinalIgnoreCase) == true &&
                predicate.Try(item)
        );
    }

    public void SendItemUpdate(GameItem item)
    {
        if (item?.profile == this)
        {
            OnItemUpdated?.Invoke(item);
            item.NotifyChanged();
        }
    }

    public async Task<GameProfile> Query(bool forceFetch = false, bool forceCompleteFetch = false, bool silent = false)
    {
        if (account is null)
            return this;

        await account.profileOperationSemaphore.WaitAsync();
        try
        {
            if (!hasProfile || forceFetch)
            {
                if (forceCompleteFetch)
                    rvn = -1;
                await PerformOperationUnsafe("QueryProfile", silent: silent);
            }
            return this;
        }
        finally
        {
            account.profileOperationSemaphore.Release();
        }
    }

    //should only be used after manually entering the profileOperationSemaphore (and validating account existance), such as in GameAccount.QueryAllProfiles()
    public async Task<GameProfile> QueryUnsafe(bool forceFetch = false)
    {
        if (!hasProfile || forceFetch)
        {
            await PerformOperationUnsafe("QueryProfile");
        }
        return this;
    }

    public async Task<JsonArray> PerformOperation(string operation, JsonNode content) =>
        await PerformOperation(operation, content.ToString());

    public async Task<JsonArray> PerformOperation(string operation, string content = "{}")
    {
        if (account is null)
            return null;
        await account.profileOperationSemaphore.WaitAsync();
        try
        {
            return await PerformOperationUnsafe(operation, content);
        }
        finally
        {
            account.profileOperationSemaphore.Release();
        }
    }

    public void MarkItemsSeen(GameItem[] items)
    {
        var filteredItems = items.Where(i => i.profile == this && i.template is not null).ToArray();
        if (
            filteredItems.Length == 0 ||
            !(
                profileId == FnProfileTypes.AccountItems ||
                profileId == FnProfileTypes.Common ||
                profileId == FnProfileTypes.CosmeticInventory
            )
        )
            return;
        for (int i = 0; i < filteredItems.Length; i++)
        {
            filteredItems[i].SetSeenLocal(true);
        }
        string content = @$"{{""itemIds"": [{string.Join(", ", filteredItems.Select(i => @$"""{i.uuid}"""))}]}}";
        PerformOperation("MarkItemSeen", content).StartTask();
    }

    #region TempHeroLoadoutCopy

    public async Task CopyLoadoutToItem(GameItem sourceLoadout, string destinationLoadoutId)
    {

        if (account is null)
            return;
        //this may not work with low level accounts
        //await account.profileOperationSemaphore.WaitAsync();
        try
        {
            var targetLoadout = GetItem(destinationLoadoutId);
            await Task.WhenAll(
                CopyHero(sourceLoadout, targetLoadout, "commanderslot"),
                CopyTeamPerk(sourceLoadout, targetLoadout),
                CopyHero(sourceLoadout, targetLoadout, "followerslot1"),
                CopyHero(sourceLoadout, targetLoadout, "followerslot2"),
                CopyHero(sourceLoadout, targetLoadout, "followerslot3"),
                CopyHero(sourceLoadout, targetLoadout, "followerslot4"),
                CopyHero(sourceLoadout, targetLoadout, "followerslot5"),
                CopyGadget(sourceLoadout, targetLoadout, 0),
                CopyGadget(sourceLoadout, targetLoadout, 1)
            );
            //foreach (var att in sourceLoadout.attributes)
            //{
            //    targetLoadout.attributes[att.Key] = att.Value.DeepClone();
            //}
            //var keys = targetLoadout.attributes.Select(kvp => kvp.Key).ToArray();
            //foreach (var att in keys)
            //{
            //    if (!sourceLoadout.attributes.ContainsKey(att))
            //        targetLoadout.attributes.Remove(att);
            //}
            //targetLoadout.NotifyChanged();
            //OnItemUpdated?.Invoke(targetLoadout);
        }
        finally
        {
            //account.profileOperationSemaphore.Release();
        }
        //await Query(true, true);
    }

    private async Task CopyHero(GameItem sourceLoadout, GameItem destLoadout, string slotName)
    {
        string hid = sourceLoadout.attributes["crew_members"][slotName]?.ToString();
        if (hid == destLoadout.attributes["crew_members"][slotName]?.ToString())
            return;
        await PerformOperation("AssignHeroToLoadout", $$"""
        {
            "heroId": "{{hid}}",
            "loadoutId": "{{destLoadout.uuid}}",
            "slotName": "{{slotName}}"
        }
        """);
    }

    private async Task CopyTeamPerk(GameItem sourceLoadout, GameItem destLoadout)
    {
        string tpid = sourceLoadout.attributes["team_perk"]?.ToString();
        if (tpid == destLoadout.attributes["team_perk"]?.ToString())
            return;
        await PerformOperation("AssignTeamPerkToLoadout", $$"""
        {
            "teamPerkId": "{{tpid}}",
            "loadoutId": "{{destLoadout.uuid}}"
        }
        """);
    }

    private async Task CopyGadget(GameItem sourceLoadout, GameItem destLoadout, int slotIdx)
    {
        string gadgetId = "";
        if (sourceLoadout.attributes["gadgets"] is JsonArray gadgetsArray && gadgetsArray.Count > slotIdx)
            gadgetId = gadgetsArray[slotIdx]?["gadget"]?.ToString();
        if (destLoadout.attributes["gadgets"] is JsonArray destArray && destArray.Count > slotIdx)
        {
            if (destArray[slotIdx]?["gadget"]?.ToString() == gadgetId)
                return;
        }
        await PerformOperation("AssignGadgetToLoadout", $$"""
        {
            "gadgetId": "{{gadgetId}}",
            "loadoutId": "{{destLoadout.uuid}}",
            "slotIndex": {{slotIdx}}
        }
        """);
    }
    #endregion


    DateTime lastClientQuestLoginAttempt = DateTime.MinValue;
    public JsonObject lastOp { get; private set; }
    async Task<JsonArray> PerformOperationUnsafe(string operation, string content = "{}", bool isRetry = false, bool silent = false, bool witholdChanges = false)
    {
        if (!account.isOwned)
        {
            if (operation == "QueryProfile")
                operation = "QueryPublicProfile";

            if (operation != "QueryPublicProfile")
            {
                GD.Print($"cannot perform \"{operation}\" on unowned profile");
                return null;
            }

            if (profileId != FnProfileTypes.AccountItems && profileId != FnProfileTypes.Common)
            {
                GD.Print($"cannot access unowned profile of type \"{profileId}\"");
                return null;
            }
        }

        var actingAccount = account.isOwned ? account : GameAccount.ActiveAccount;
        //if (!await actingAccount.Authenticate())
        //    return null;

        if (operation == "MarkItemSeen")
        {
            var targetItemIDs = JsonNode.Parse(content)["itemIds"].AsArray().Select(n => n.ToString());
            var targetItems = targetItemIDs.Select(uuid => items.TryGetValue(uuid, out var item) ? item : null).Where(x => x != null);
            bool hasUnseen = false;
            foreach (var item in targetItems)
            {
                if (item.attributes["item_seen"] is null)
                {
                    item.SetSeenLocal();
                    OnItemUpdated?.Invoke(item);
                    hasUnseen = true;
                }
            }
            if (!hasUnseen)
                return null;
        }
        if(operation == "ClientQuestLogin")
        {
            if ((DateTime.UtcNow - lastClientQuestLoginAttempt).TotalSeconds < 10)
                return null;
            lastClientQuestLoginAttempt = DateTime.UtcNow;
        }

        var opResponse = await FnWebAddresses.FortGame
            .MakeRequest(
                "fortnite/api/game/v2/profile/" +
                $"{account.accountId}/{(account.isOwned ? "client" : "public")}/" +
                $"{operation}?profileId={profileId}&rvn={rvn}",
                HttpMethod.Post
            )
            .SetJsonContent(content)
            .SetAccount(actingAccount)
            .Send();

        JsonObject result = null;
        try
        {
            result = await opResponse.ReadJson<JsonObject>();
        }
        catch { }
        lastOp = result;


        if (await opResponse.CheckForError(!silent))
            return null;

        if (witholdChanges)
            return [];

        var changes = result["profileChanges"]?.AsArray();

        bool fullUpdate = false;
        if (changes is null)
        {
            GD.Print($"unknown profile op result: {result}");
            return null;
        }
        if (changes.Count == 0) { }
        else if (changes[0]["changeType"].ToString() == "fullProfileUpdate")
        {
            //GD.Print("FULLUPDATE: " + profileId);
            fullUpdate = true;
            var resultItems = result["profileChanges"][0]["profile"]["items"].AsObject();
            var resultStats = result["profileChanges"][0]["profile"]["stats"]["attributes"].AsObject();
            if (hasProfile)
                ApplyProfileChanges(GenerateChanges(resultItems, resultStats));
            else
            {
                items = resultItems.Select(kvp => new GameItem(this, kvp.Key, kvp.Value.AsObject())).ToDictionary(item => item.uuid);
                groupedItems = new(
                    items.GroupBy(kvp => kvp.Value.templateId.Split(":")[0].ToLower())
                    .Select(grouping => KeyValuePair.Create(grouping.Key, grouping.Select(kvp => kvp.Value).ToList())),
                    StringComparer.OrdinalIgnoreCase
                );
                statAttributes = resultStats;
            }
        }
        else
        {
            if (operation == "UpgradeItemBulk" || operation == "UpgradeItemRarity")
            {
                JsonNode additionChange = changes.FirstOrDefault(n => n["changeType"].ToString() == "itemAdded");
                JsonNode removalChange = changes.FirstOrDefault(n => n["changeType"].ToString() == "itemRemoved");
                if (additionChange is not null && removalChange is not null)
                {
                    var additionIndex = changes.IndexOf(additionChange);
                    var removingId = removalChange["itemId"].ToString();
                    List<JsonNode> irrelevantChanges = [];
                    for (int i = additionIndex; i < changes.Count; i++)
                    {
                        var change = changes[i];
                        if (change["itemId"].ToString() == removingId)
                            irrelevantChanges.Add(change);
                    }
                    foreach (var change in irrelevantChanges)
                    {
                        changes.Remove(change);
                    }
                    additionChange["newItemId"] = additionChange["itemId"].ToString();
                    additionChange["itemId"] = removingId;
                    additionChange["changeType"] = "itemUpgraded";
                }
            }
            else if (operation == "")
            {

            }
            ApplyProfileChanges(changes);
        }
        rvn = result["profileRevision"].GetValue<int>();

        if (result.ContainsKey("multiUpdate"))
        {
            if (printChanges)
                GD.Print("multiupdate");
            foreach (var profileUpdate in result["multiUpdate"].AsArray())
            {
                string profileId = profileUpdate["profileId"].ToString();
                if (printChanges)
                    GD.Print("multiupdating: " + profileId);
                if (account.HasProfile(profileId))
                {
                    if (printChanges)
                        GD.Print("has profile");
                    var profile = account.GetProfile(profileId);
                    profile.ApplyProfileChanges(profileUpdate["profileChanges"].AsArray());
                    profile.rvn = profileUpdate["profileRevision"].GetValue<int>();
                }
            }
        }
        printChanges = false;

        string fullText = fullUpdate ? " (Full Update)" : "";
        if (actingAccount != account)
            GD.Print($"operation complete ({operation} in {profileId} of {account.DisplayName} as {actingAccount.DisplayName}){fullText}");
        else
            GD.Print($"operation complete ({operation} in {profileId} as {account.DisplayName}){fullText}");

        if (result.ContainsKey("notifications"))
        {
            var notifs = result["notifications"].AsArray();
            var toRemove = notifs.Where(n => n["type"].ToString() == "redeemStwTokensNotification").ToArray();
            foreach (var notif in toRemove)
            {
                notifs.Remove(notif);
            }
            if (notifs.Count > 0)
                GD.Print("Notifications: " + notifs.ToString());
            return notifs;
        }
        return [];
    }

    JsonArray GenerateChanges(JsonObject newItems, JsonObject newStats = null)
    {
        var oldKeys = items.Keys.ToArray();
        var newKeys = newItems.Select(x => x.Key).ToArray();

        var addedKeys = newKeys.Except(oldKeys);
        var removedKeys = oldKeys.Except(newKeys);
        var possiblyChangedKeys = oldKeys.Intersect(newKeys);

        JsonArray profileChanges = [];

        foreach (var itemKey in removedKeys)
        {
            profileChanges.Add(new JsonObject()
            {
                ["changeType"] = "itemRemoved",
                ["itemId"] = itemKey
            });
        }
        foreach (var itemKey in possiblyChangedKeys)
        {
            var from = items[itemKey].SimpleRawData.ToString();
            var to = newItems[itemKey].ToString();
            if (from != to)
            {
                GD.Print($"FROM ({from}) >>> ({to})");
                profileChanges.Add(new JsonObject()
                {
                    ["changeType"] = "itemFullyChanged",
                    ["itemId"] = itemKey,
                    ["item"] = newItems[itemKey].SafeDeepClone()
                });
            }
        }
        foreach (var itemKey in addedKeys)
        {
            profileChanges.Add(new JsonObject()
            {
                ["changeType"] = "itemAdded",
                ["itemId"] = itemKey,
                ["item"] = newItems[itemKey].SafeDeepClone()
            });
        }
        foreach (var statKVP in newStats)
        {
            if (!statAttributes.TryGetPropertyValue(statKVP.Key, out var statVal) || statKVP.Value.ToString() != statVal.ToString())
            {
                profileChanges.Add(new JsonObject()
                {
                    ["changeType"] = "statModified",
                    ["name"] = statKVP.Key,
                    ["value"] = statVal.SafeDeepClone()
                });
            }
        }

        return profileChanges;
    }

    public static bool printChanges = false;

    public void ApplyProfileChanges(JsonArray profileChanges)
    {
        if (profileId == FnProfileTypes.AccountItems)
        {
            account.GetFORTStats(true);
            account.GetVentureFORTStats(true);
        }
        else if (profileId == FnProfileTypes.Backpack)
        {
            account.GetFORTStats(true);
        }
        else if (profileId == FnProfileTypes.VentureBackpack)
        {
            account.GetVentureFORTStats(true);
        }

        List<GameItem> itemsToNotify = [];
        List<GameItem> itemsToIgnore = [];
        Dictionary<string, GameItem> reassociations = [];
        if (printChanges)
            GD.Print($"Applying {profileChanges.Count} changes");
        foreach (var change in profileChanges)
        {
            string changeType = change["changeType"].ToString();
            string uuid = change["itemId"]?.ToString();
            if (printChanges)
                GD.Print($"Applying \"{changeType}\"");
            //ProfileItemId profileItemId = new(profileId, uuid);
            GameItem targetItem;
            switch (changeType)
            {
                case "itemAdded":
                    targetItem = new(this, uuid, change["item"].AsObject());
                    lock (items)
                    {
                        items[uuid] = targetItem;
                    }
                    lock (groupedItems)
                    {
                        groupedItems.TryAdd(targetItem.templateId.Split(":")[0], []);
                        groupedItems[targetItem.templateId.Split(":")[0]].Add(targetItem);
                    }
                    if (printChanges)
                        GD.Print($"ADDED: {items[uuid]}");
                    OnItemAdded?.Invoke(targetItem);
                    break;
                case "itemRemoved":
                    if (!items.ContainsKey(uuid))
                        continue;
                    targetItem = items[uuid];
                    if (printChanges)
                        GD.Print($"REMOVING: {targetItem}");
                    targetItem.NotifyRemoving();
                    lock (items)
                    {
                        items.Remove(uuid);
                    }
                    lock (groupedItems)
                    {
                        if (groupedItems.TryGetValue(targetItem.templateId.Split(":")[0], out var list) && list.Contains(targetItem))
                            list.Remove(targetItem);
                    }
                    itemsToIgnore.Add(targetItem);
                    OnItemRemoved?.Invoke(targetItem);
                    targetItem.DisconnectFromProfile();
                    break;
                case "statModified":
                    var statName = change["name"].ToString();
                    var statVal = change["value"].SafeDeepClone();
                    var hadStatBefore = statAttributes.TryGetPropertyValue(statName, out var oldStatVal);
                    var hasStatNow = statVal is not null;

                    string statValText = "[Removed]";
                    if (hasStatNow)
                    {
                        statAttributes[statName] = statVal;
                        statValText = statVal.ToString();
                        if (statValText.Length > 500)
                            statValText = "[500+ chars]";
                    }
                    else
                        statAttributes.Remove(statName);


                    string oldStatValText = "";
                    if (hadStatBefore)
                    {
                        oldStatValText = oldStatVal.ToString();
                        if (oldStatValText.Length > 500)
                            oldStatValText = "[500+]";
                        oldStatValText += "\n=>\n";
                    }

                    if (printChanges)
                        GD.Print($"STAT CHANGED: {statName}: {oldStatValText}{statValText}");
                    OnStatChanged?.Invoke(statName);
                    OnStatsChanged?.Invoke();

                    break;
                case "itemQuantityChanged":
                    targetItem = items[uuid];
                    var oldQuantity = targetItem.quantity;
                    targetItem.SetLocalQuantity(change["quantity"].GetValue<int>());
                    if (printChanges)
                        GD.Print($"CHANGED (quantity): {uuid} ({oldQuantity} => {targetItem.quantity})");
                    itemsToNotify.Add(targetItem);
                    break;
                case "itemAttrChanged":
                    targetItem = items[uuid];
                    var oldValue = targetItem.attributes[change["attributeName"].ToString()]?.ToString();
                    targetItem.attributes[change["attributeName"].ToString()] = change["attributeValue"].SafeDeepClone();
                    if (printChanges)
                        GD.Print($"CHANGED (attribute): {uuid}[{change["attributeName"]}] \n{oldValue}\n=>\n{change["attributeValue"]}");
                    itemsToNotify.Add(targetItem);
                    break;
                case "itemUpgraded":
                    //reassociates an item that was removed and readded as part of an upgrade
                    targetItem = items[uuid];
                    targetItem.Reassociate(change["newItemId"]?.ToString(), change["item"].AsObject());
                    if (printChanges)
                        GD.Print($"UPGRADED: {items[uuid]}");
                    itemsToNotify.Add(targetItem);
                    reassociations.Add(uuid, targetItem);
                    break;
                case "itemFullyChanged":
                    //autogenerated item changes
                    targetItem = items[uuid];
                    targetItem.SetRawData(change["item"].AsObject());
                    if (printChanges)
                        GD.Print($"CHANGED (full): {targetItem}");
                    itemsToNotify.Add(targetItem);
                    break;
            }
        }
        itemsToNotify.RemoveAll(i => itemsToIgnore.Contains(i));
        foreach (var item in itemsToNotify)
        {
            if (printChanges)
                GD.Print($"Notifying : {item.uuid}");
            item.NotifyChanged();
            OnItemUpdated?.Invoke(item);
        }
        foreach (var kvp in reassociations)
        {
            if (printChanges)
                GD.Print($"Reassociating : {kvp.Key} => {kvp.Value.uuid}");
            OnItemReassociated?.Invoke(kvp.Key, kvp.Value);
        }
        if (profileChanges.Count > 0)
            OnProfileChanged?.Invoke();
    }
}

