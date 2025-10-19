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
        client.Resource = $"V2:Fortnite:SWT::{Convert.ToHexString(ridBytes).ToUpper()}";
        client.Password = account.AuthToken;
        await client.ConnectAsync();
        try
        {
            var partyReq = await FnWebAddresses.party
                .MakeRequest($"/party/api/v1/Fortnite/user/{account.accountId}")
                .SetAuthorisation(account.AuthHeader)
                .Send();
            var partyContainer = await partyReq.Content.ReadFromJsonAsync<PartyContainer>(Helpers.JsonOptions.CamelCase);
            party = partyContainer.current.Length>0 ? partyContainer.current[0] : null;
            if(party is not null)
            {
                OnPartyUpdated?.Invoke();
            }
        }
        catch { }
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
                        _ => jElement.Deserialize<XmppEpicMsg>()
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
            if (pUpdate.captain_id != account.accountId)
                return;
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
            GD.Print("Party Update: " + pUpdate.party_id);
            party.meta = partyState;
            OnPartyUpdated?.Invoke();
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
            party.members.Remove(mLeave.account_id);
            GD.Print("Left: " + mLeave.account_dn ?? mLeave.account_id);
        }
        else
        {
            GD.Print($"message: {msg.type}");
        }
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
