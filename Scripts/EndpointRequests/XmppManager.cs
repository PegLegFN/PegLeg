using Godot;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using XmppDotNet;
using XmppDotNet.Transport;
using XmppDotNet.Transport.WebSocket;
using XmppDotNet.Xml;
using XmppDotNet.Xmpp;
using XmppDotNet.Xmpp.Client;

public class XmppManager
{
    const string prodDomain = "prod.ol.epicgames.com";
    const string xmppUriString = "wss://xmpp-service-prod.ol.epicgames.com";
    public static readonly Uri xmppUri = new(xmppUriString);

    GameAccount account;
    XmppClient client;
    IDisposable? stateSubscription;
    IDisposable? elementSubscription;

    bool activeStateChanging = false;
    public bool Active { get; private set; }

    internal XmppManager(GameAccount account)
    {
        this.account = account;
        client =
        new(conf => conf
            .UseWebSocketTransport(new StaticNameResolver(xmppUri))
            .UseAutoReconnect()
        )
        {
            Jid = $"{account.accountId}@{prodDomain}",
            Tls = false,
        };

        (client.Transport as WebSocketTransport).XmlSent.Subscribe(DebugDataOut);

        stateSubscription = client
            .StateChanged
            .Subscribe(OnSessionState);
        elementSubscription = client
            .XmppXElementReceived
            .Subscribe(OnElementRecieved);
    }

    private void DebugDataOut(XmppXElement outData)
    {
        //GD.Print($"Xmpp out: >>> {outData}");
    }

    public async Task Connect()
    {
        if (Active || activeStateChanging)
            return;
        activeStateChanging = true;
        byte[] ridBytes = new byte[16];
        Random.Shared.NextBytes(ridBytes);

        var resourceId = $"V2:Fortnite:SWT::{Convert.ToHexString(ridBytes).ToUpper()}";

        try
        {
            var partyReq = await FnWebAddresses.party
                .MakeRequest($"/party/api/v1/Fortnite/user/{account.accountId}")
                .SetAuthorisation(account.AuthHeader)
                .Send();
            var partyContainer = await partyReq.Content.ReadFromJsonAsync<PartyContainer>(Helpers.JsonOptions.CamelCase);
            party = partyContainer.current.Length > 0 ? partyContainer.current[0] : null;
            if (party is not null)
            {
                GD.Print($"Party Found: {party.id} (Created: {party.created_at})");
                //resourceId = party.members[account.accountId].connections[0].id.Split("/")[^1];
                OnPartyUpdated?.Invoke();
            }
        }
        catch { }

        client.Resource = resourceId;
        client.Password = account.AuthToken;
        await client.ConnectAsync();

        //try
        //{
        //    if (party is not null)
        //    {
        //        var partyReq = await FnWebAddresses.party
        //            .MakeRequest($"/party/api/v1/Fortnite/parties/{party.id}/members/{account.accountId}/join")
        //            .SetAuthorisation(account.AuthHeader)
        //            .SetJsonContent()
        //            .Send();
        //    }
        //}
        //catch { }

        Active = true;
        activeStateChanging = false;
    }

    SessionState latestState = SessionState.Disconnected;
    void OnSessionState(SessionState newState)
    {
        if (latestState!=newState)
            GD.Print($"Xmpp state: {newState}");
        switch (newState)
        {
            case SessionState.Connected:
                //GD.Print($"Xmpp Connected");
                break;
            case SessionState.Disconnected:
                //GD.Print($"Xmpp Disconnected");
                break;
        }
        latestState = newState;
    }

    void OnElementRecieved(XmppXElement element)
    {
        if (element is Message msg)
        {
            if (
                msg.Type == MessageType.Normal &&
                msg.From == $"xmpp-admin@{prodDomain}"
            )
            {
                //todo: create a class to deserialise into
                bool printData = false;
                try
                {
                    JsonElement jElement = JsonDocument.Parse(msg.Body).RootElement;
                    string type = jElement.GetProperty("type").GetString();
                    XmppEpicMsg parsedMsg = type switch
                    {
                        "com.epicgames.friends.core.apiobjects.Friend" => jElement.Deserialize<XmppEpicMsg.FriendRequest>(Helpers.JsonOptions.CamelCase),
                        "FRIENDSHIP_REMOVE" => jElement.Deserialize<XmppEpicMsg.FriendshipRemoval>(Helpers.JsonOptions.CamelCase),
                        "USER_BLOCKLIST_UPDATE" => jElement.Deserialize<XmppEpicMsg.UserBlockState>(Helpers.JsonOptions.CamelCase),

                        "com.epicgames.social.party.notification.v0.PING" => jElement.Deserialize<XmppEpicMsg.PartyInvite>(Helpers.JsonOptions.CamelCase),
                        "com.epicgames.social.party.notification.v0.INITIAL_INTENTION" => jElement.Deserialize<XmppEpicMsg.PartyJoinRequest>(Helpers.JsonOptions.CamelCase),
                        "com.epicgames.social.party.notification.v0.INTENTION_EXPIRED" => jElement.Deserialize<XmppEpicMsg.ExpiredJoinRequest>(Helpers.JsonOptions.CamelCase),

                        "com.epicgames.social.party.notification.v0.MEMBER_CONNECTED" => jElement.Deserialize<XmppEpicMsg.PartyMemberConnected>(Helpers.JsonOptions.CamelCase),
                        "com.epicgames.social.party.notification.v0.MEMBER_JOINED" => jElement.Deserialize<XmppEpicMsg.PartyMemberJoined>(Helpers.JsonOptions.CamelCase),
                        "com.epicgames.social.party.notification.v0.MEMBER_STATE_UPDATED" => jElement.Deserialize<XmppEpicMsg.PartyMemberUpdate>(Helpers.JsonOptions.CamelCase),
                        "com.epicgames.social.party.notification.v0.MEMBER_NEW_CAPTAIN" => jElement.Deserialize<XmppEpicMsg.PartyMemberPromoted>(Helpers.JsonOptions.CamelCase),
                        "com.epicgames.social.party.notification.v0.MEMBER_REQUIRE_CONFIRMATION" => jElement.Deserialize<XmppEpicMsg.PartyMemberConfirmation>(Helpers.JsonOptions.CamelCase),
                        "com.epicgames.social.party.notification.v0.MEMBER_EXPIRED" => jElement.Deserialize<XmppEpicMsg.PartyMemberTimeout>(Helpers.JsonOptions.CamelCase),
                        "com.epicgames.social.party.notification.v0.MEMBER_LEFT" => jElement.Deserialize<XmppEpicMsg.PartyMemberLeft>(Helpers.JsonOptions.CamelCase),
                        "com.epicgames.social.party.notification.v0.MEMBER_KICKED" => jElement.Deserialize<XmppEpicMsg.PartyMemberKicked>(Helpers.JsonOptions.CamelCase),
                        "com.epicgames.social.party.notification.v0.MEMBER_DISCONNECTED" => jElement.Deserialize<XmppEpicMsg.PartyMemberDisconnected>(Helpers.JsonOptions.CamelCase),

                        "com.epicgames.social.party.notification.v0.PARTY_UPDATED" => jElement.Deserialize<XmppEpicMsg.PartyUpdate>(Helpers.JsonOptions.CamelCase),
                        "com.epicgames.social.interactions.notification.v2" => jElement.Deserialize<XmppEpicMsg.InteractionNotif>(Helpers.JsonOptions.CamelCase),
                        _ => jElement.Deserialize<XmppEpicMsg.GenericXmppEpicMessage>()
                    };
                    OnEpicMsgRecieved(parsedMsg);
                }
                catch (Exception e)
                {
                    GD.Print($"ex: {e}");
                    printData = true;
                }
                try
                {
                    if (JsonNode.Parse(msg.Body)?.AsObject() is not JsonObject obj)
                        return;
                    if (printData)
                        GD.Print($"Message Data: {obj}");
                }
                catch
                {
                    return;
                }
                return;
            }
            else
            {
                //JsonObject msgData = JsonNode.Parse(presence.Status ?? "{}").AsObject();
                var acc = GameAccount.GetOrCreateAccount(msg.From.Local);
                GD.Print($"msg from {acc.DisplayName}: {msg.Body}");
            }
        }
        else if (element is Presence presence)
        {
            try
            {
                JsonObject presenceData = JsonNode.Parse(presence.Status ?? "{}").AsObject();
                var acc = GameAccount.GetOrCreateAccount(presence.From.Local);
                //GD.Print($"Xmpp presence of {presence.From.Local}: \n{presenceData}");
                GD.Print($"Xmpp presence of {acc.DisplayName}: {presenceData["Status"]}");
                OnUserStatusChanged?.Invoke(presence.From.Local, presenceData["Status"]?.ToString());
                if (presence.From.Local == client.Jid.Local)
                {
                    GD.Print(presenceData.ToJsonString(Helpers.JsonOptions.Fields));
                }
                //set presence of friend
            }
            catch(Exception e) 
            { 
                GD.PushError(e.ToString()); 
            }
        }
    }
    public static event Action<string, string> OnUserStatusChanged;

    PartyData? party;
    PartyInviteData[] invites = [];
    PartyPingData[] pings = [];

    public PartyData? Party => party;
    public event Action OnPartyUpdated;
    public event Action<PartyData.Member> OnPartyMemberUpdated;
    void OnEpicMsgRecieved(XmppEpicMsg msg)
    {
        if (msg is XmppEpicMsg.PartyUpdate pUpdate)
        {
            //if (pUpdate.captain_id != account.accountId)
            //    return;
            if (pUpdate.party_id != party?.id)
                return;
            var partyState = party.meta;
            foreach (var kvp in pUpdate.party_state_updated)
            {
                partyState[kvp.Key] = kvp.Value;
            }
            foreach (var key in pUpdate.party_state_removed)
            {
                partyState.Remove(key);
            }
            GD.Print($"Party Update: {pUpdate.party_id} (Rev: {pUpdate.revision})");
            party.meta = partyState;
            party.revision = pUpdate.revision;
            party.updated_at = pUpdate.updated_at;
            OnPartyUpdated?.Invoke();
        }
        else if (msg is XmppEpicMsg.PartyMemberJoined mConnect)
        {
            if (party.members.TryGetValue(mConnect.account_id, out var memberData))
            {
                memberData.connections = [.. memberData.connections, mConnect.connection];
            }
            else
            {
                memberData = new()
                {
                    account_id = mConnect.account_id,
                    joined_at = mConnect.joined_at.Value,
                    updated_at = mConnect.updated_at.Value,
                    connections = [mConnect.connection]
                };
                party.members.Add(mConnect.account_id, memberData);
            }
            var memberState = memberData.meta;
            foreach (var kvp in mConnect.member_state_updated)
            {
                memberState[kvp.Key] = kvp.Value;
            }
            memberData.meta = memberState;
            GD.Print("Connected: " + mConnect.account_dn ?? mConnect.account_id);
            OnPartyMemberUpdated?.Invoke(memberData);
        }
        else if (msg is XmppEpicMsg.PartyMemberJoined mJoin)
        {
            if (!party.members.TryGetValue(mJoin.account_id, out var memberData))
            {
                memberData = new()
                {
                    account_id = mJoin.account_id,
                    joined_at = mJoin.joined_at.Value,
                    updated_at = mJoin.updated_at.Value,
                    connections = [mJoin.connection]
                };
                party.members.Add(mJoin.account_id, memberData);
            }
            var memberState = memberData.meta;
            foreach (var kvp in mJoin.member_state_updated)
            {
                memberState[kvp.Key] = kvp.Value;
            }
            memberData.meta = memberState;
            GD.Print("Joined: " + mJoin.account_dn ?? mJoin.account_id);
            OnPartyMemberUpdated?.Invoke(memberData);
        }
        else if (msg is XmppEpicMsg.PartyMemberUpdate mUpdate)
        {
            if (mUpdate.party_id != party?.id)
                return;
            if (!party.members.TryGetValue(mUpdate.account_id, out var memberData))
                return;
            var memberState = memberData.meta;
            foreach (var kvp in mUpdate.member_state_updated)
            {
                memberState[kvp.Key] = kvp.Value;
            }
            foreach (var key in mUpdate.member_state_removed)
            {
                memberState.Remove(key);
            }
            memberData.meta = memberState;
            GD.Print("Updated: " + mUpdate.account_dn ?? mUpdate.account_id);
            OnPartyMemberUpdated?.Invoke(memberData);
        }
        else if (msg is XmppEpicMsg.PartyMemberPromoted mPromo)
        {
            if (mPromo.party_id != party?.id)
                return;
            foreach (var member in party.members.Values)
            {
                member.role = member.account_id == mPromo.account_id ? PartyData.Member.Role.MEMBER : PartyData.Member.Role.CAPTAIN;
            }
            GD.Print("Promoted: " + mPromo.account_dn ?? mPromo.account_id);
        }
        else if (msg is XmppEpicMsg.PartyMemberConfirmation mConfirm)
        {
            //unsure how to handle
            GD.Print("Confirm: "+mConfirm.account_dn ?? mConfirm.account_id);
        }
        else if (msg is XmppEpicMsg.PartyMemberTimeout mExpire)
        {
            //unsure how to handle
            GD.Print("Expired: " + mExpire.account_dn ?? mExpire.account_id);
        }
        else if (msg is XmppEpicMsg.PartyMemberMsg mLeave)
        {
            if (mLeave.party_id != party?.id)
                return;
            //covers leaves, kicks, disconnects
            //party.members.Remove(mLeave.account_id);
            if(!party.members.TryGetValue(mLeave.account_id, out var m))
            {
                GD.Print("member missing");
                GD.Print(JsonSerializer.Serialize(mLeave.UnknownData));
                return;
            }
            m.connections = [..m.connections.Where(c => c.id != mLeave.connection.id)];
            if (m.connections.Length>0)
            {
                GD.Print($"Connection Terminated: {mLeave.account_dn ?? mLeave.account_id} ({mLeave.connection.id})");
            }
            else
            {
                party.members.Remove(mLeave.account_id);
                GD.Print($"Final Connection Terminated: {mLeave.account_dn ?? mLeave.account_id} ({mLeave.connection.id})");
            }
        }
        else if (msg is XmppEpicMsg.InteractionNotif interaction)
        {
            GD.Print($"Interactions: [\n{string.Join("", interaction.interactions.Select(i=>$"{{{i.interactionType}, {i.fromAccountId} => {i.toAccountId} ({i._type})}}\n"))}]");
        }
        else if(msg is XmppEpicMsg.GenericXmppEpicMessage gen)
        {
            GD.Print($"message: {msg.type}, data:[\n{string.Join("", gen.UnknownData.Select(kvp => $"{kvp.Key} = {kvp.Value}\n"))}]");
        }
        else
        {
            GD.Print($"message: {msg.type}");
        }
    }

    record struct PartyPatch
    {
        public PartyPatch(int revision, Dictionary<string, string> metaUpdate)
        {
            this.revision = revision;
            meta = new() { update = metaUpdate };
        }

        public int revision;
        public MetaPatch meta;
        public struct MetaPatch
        {
            public required Dictionary<string, string> update;
        }
    }

    public async Task SendPartyPatch(Dictionary<string, string> toPatch)
    {
        if (party is null)
            return;
        PartyPatch patch = new(party.revision, toPatch);
        GD.Print("Patching: " + JsonSerializer.Serialize(patch, Helpers.JsonOptions.Fields));

        var patchReq = await FnWebAddresses.party
            .MakeRequest($"/party/api/v1/Fortnite/parties/{party.id}", System.Net.Http.HttpMethod.Patch)
            .SetAuthorisation(account.AuthHeader)
            .SetJsonContent(JsonSerializer.Serialize(patch, Helpers.JsonOptions.Fields))
            .Send();

        if (!patchReq.IsSuccessStatusCode)
        {
            //fetch party?
            return;
        }
        GD.Print("Patch success");
    }

    record struct PartyMemberPatch(int revision, Dictionary<string, string> update);
    public async Task SendPartyMemberPatch(Dictionary<string, string> toPatch)
    {
        if (party is null)
            return;
        PartyMemberPatch patch = new(party.revision, toPatch);
        GD.Print("Patching: " + JsonSerializer.Serialize(patch, Helpers.JsonOptions.Fields));

        var patchReq = await FnWebAddresses.party
            .MakeRequest($"/party/api/v1/Fortnite/parties/{party.id}/members/{account.accountId}", System.Net.Http.HttpMethod.Patch)
            .SetAuthorisation(account.AuthHeader)
            .SetJsonContent(JsonSerializer.Serialize(patch, Helpers.JsonOptions.Fields))
            .Send();

        if (!patchReq.IsSuccessStatusCode)
        {
            //fetch party?
            return;
        }
        GD.Print("Patch success");
    }

    public async Task SendStatus(string? status, string? toAccount = null) =>
        await SendStatus(status is null ? null : new JsonObject() { ["Status"] = status }, toAccount);
    //todo: create struct for status object
    public async Task SendStatus(JsonObject? status, string? toAccount = null)
    {
        GD.Print($"Active: {Active}");
        if (!Active)
            return;
        GD.Print($"Setting Status: {status}");
        if (status is null)
        {
            await client.SendAsync(new Presence());
            return;
        }
        await client.SendAsync(new Presence()
        {
            To = toAccount,
            Status = status.ToString()
        });
    }

    public enum Appearance
    {
        Away,
        Online,
        DoNotDisturb,
        ExtendedAway
    }

    public async Task Disconnect()
    {
        if (!Active || activeStateChanging)
            return;
        activeStateChanging = true;
        await client.DisconnectAsync();
        activeStateChanging = false;
        Active = false;
        stateSubscription?.Dispose();
        elementSubscription?.Dispose();
    }
}
