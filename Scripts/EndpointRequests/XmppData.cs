using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
public record class XmppPresence
{
    public XmppPresence CreateGameStatus(GameAccount account, string? status) =>
        //fill in party data if one exists
        new()
        {
            Status = status ?? "Save The World",
            IsPlaying = false,
            IsJoinable = false,
            HasVoiceSupport = false,
            SessionId = "",
            ProductName = "Fortnite",
            Properties = new()
        };

    public required string Status;
    [JsonPropertyName("bIsPlaying")]
    public bool? IsPlaying;
    [JsonPropertyName("bIsJoinable")]
    public bool? IsJoinable;
    [JsonPropertyName("bHasVoiceSupport")]
    public bool? HasVoiceSupport;
    public string? SessionId;
    public string? ProductName;
    public Props? Properties;
    public record struct Props()
    {
        [JsonPropertyName("FortBasicInfo_j")]
        public Basic FortBasicInfo = new();
        [JsonInclude]
        string FortLFG_I = "0";
        [JsonIgnore]
        public int FortLFG
        {
            readonly get => int.TryParse(FortLFG_I, out int res) ? res : 0;
            set => FortLFG_I = value.ToString();
        }
        [JsonPropertyName("FortPartySize_i")]
        public int FortPartySize = 1;
        [JsonPropertyName("FortSubGame_i")]
        public int FortSubGame = 1;
        [JsonPropertyName("IslandCode_s")]
        public string? IslandCode;
        [JsonPropertyName("IsInZone_b")]
        public bool IsInZone = false;
        [JsonPropertyName("SocialStatus_j")]
        public Social SocialStatus = new();
        [JsonPropertyName("InUnjoinableMatch_b")]
        public bool InUnjoinableMatch = false;
        [JsonPropertyName("party.joininfodata.286331153_j")]
        public PartyInfo? partyInfo;
    }
    public record struct Basic()
    {
        public int homeBaseRating = 0;
    }
    public record struct Social()
    {
        public string[] attendingSocialEventIds = []; //I assume this is a string array due to the naming, could very well be an object array...
    }
    public record struct Gameplay()
    {
        public string state = "";
        public string playlist = "None";
        public int numKills = 0;
        [JsonPropertyName("bFellToDeath")]
        public bool FellToDeath = false;
    }

    public record class PartyInfo
    {
        //in private parties, this is the only displayed property, and is set to true.
        //property is not present when party is not private
        [JsonInclude]
        public bool? isPrivate { get; protected set; } = true;
    }
    public record class PublicPartyInfo : PartyInfo
    {
        public PublicPartyInfo()
        {
            isPrivate = false;
        }
        public required string sourceId;
        public required string sourceDisplayName;
        public required string sourcePlatform;
        public required string partyId;
        public int partyTypeId = 286331153;
        public string key = "k";
        public required string buildId;
        public int partyFlags; // ??? what do these flags map to? replace with enum when more info is known
        public int notAcceptingReason = 0;
        [JsonPropertyName("pc")]
        public required int playerCount;
    }
}

public abstract record class XmppEpicMsg
{
    public required string type;
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownData { get; set; }

    public record class GenericXmppEpicMessage : XmppEpicMsg { }

    public abstract record class XmppEpicMsgV0 : XmppEpicMsg
    {
        public required DateTime sent;
        [JsonPropertyName("ns")]
        public required string Namespace;
        public int revision = 0;
    }

    //com.epicgames.friends.core.apiobjects.Friend
    //untested
    public record class FriendRequest : XmppEpicMsg
    {
        public required Payload payload;
        public record struct Payload
        {
            [JsonConverter(typeof(JsonStringEnumConverter<Status>))]
            public required Status status;
            public required string accountId;
            public required bool favorite;
            public required DateTime created;
            [JsonConverter(typeof(JsonStringEnumConverter<Direction>))]
            public required Direction direction;
        }

        public enum Status
        {
            ACCEPTED,
            PENDING
        }
        public enum Direction
        {
            INBOUND,
            OUTBOUND,
        }
    }

    //FRIENDSHIP_REMOVE
    //untested
    public record class FriendshipRemoval : XmppEpicMsg
    {
        public required string from;
        public required string to;
        [JsonConverter(typeof(JsonStringEnumConverter<Reason>))]
        public required Reason reason;

        public enum Reason
        {
            ABORTED,
            REJECTED,
            DELETED
        }
    }

    //USER_BLOCKLIST_UPDATE
    //untested
    public record class UserBlockState : XmppEpicMsg
    {
        public required string accountId;
        [JsonConverter(typeof(JsonStringEnumConverter<Status>))]
        public required Status status;

        public enum Status
        {
            BLOCKED,
            UNBLOCKED,
        }
    }

    //com.epicgames.social.party.notification.v0.PING
    public record class PartyInvite : XmppEpicMsgV0
    {
        public required string pinger_id;
        public required string pinger_dn;
        public required DateTime expires;
        public Dictionary<string, string>? meta;
    }

    //com.epicgames.social.party.notification.v0.INITIAL_INTENTION
    public record class PartyJoinRequest : XmppEpicMsgV0
    {
        public required string requester_id;
        public required string requester_dn;
        public required DateTime expires_at;
        public Dictionary<string, string>? meta;
    }

    //com.epicgames.social.party.notification.v0.INTENTION_EXPIRED
    public record class ExpiredJoinRequest : XmppEpicMsgV0
    {
        public required string requester_id;
        public required string requester_dn;
        public required DateTime sent_at;
        public required DateTime expires_at;
        public Dictionary<string, string>? meta;
    }

    public abstract record class PartyMemberMsg : XmppEpicMsgV0
    {
        public required string party_id;
        public required string account_id;
        public string? account_dn;

        public PartyData.Connection? connection;

        public required Dictionary<string, string> member_state_updated;

        public DateTime? joined_at;
        public DateTime? updated_at;
    }

    //com.epicgames.social.party.notification.v0.MEMBER_CONNECTED
    public record class PartyMemberConnected : PartyMemberMsg { }

    //com.epicgames.social.party.notification.v0.MEMBER_JOINED
    public record class PartyMemberJoined : PartyMemberMsg { }

    //com.epicgames.social.party.notification.v0.MEMBER_STATE_UPDATED
    public record class PartyMemberUpdate : PartyMemberMsg
    {
        public required string[] member_state_removed;
        public required Dictionary<string, string> member_state_overridden;
    }

    //com.epicgames.social.party.notification.v0.MEMBER_NEW_CAPTAIN
    public record class PartyMemberPromoted : PartyMemberMsg { }

    //com.epicgames.social.party.notification.v0.MEMBER_LEFT
    public record class PartyMemberLeft : PartyMemberMsg { }

    //com.epicgames.social.party.notification.v0.MEMBER_EXPIRED
    //untested
    public record class PartyMemberTimeout : PartyMemberMsg { }

    //com.epicgames.social.party.notification.v0.MEMBER_KICKED
    public record class PartyMemberKicked : PartyMemberMsg { }

    //com.epicgames.social.party.notification.v0.MEMBER_DISCONNECTED
    public record class PartyMemberDisconnected : PartyMemberMsg { }

    //com.epicgames.social.party.notification.v0.MEMBER_REQUIRE_CONFIRMATION
    //untested, could be Ready Ups?
    public record class PartyMemberConfirmation : PartyMemberMsg { }

    //com.epicgames.social.party.notification.v0.PARTY_UPDATED
    public record class PartyUpdate : XmppEpicMsgV0
    {
        public required string party_id;
        public required string captain_id;
        public string[] party_state_removed = [];
        public Dictionary<string, string> party_state_updated = []; //custom dictionary for containing party state
        public Dictionary<string, string> party_state_overridden = [];
        [JsonConverter(typeof(JsonStringEnumConverter<Privacy>))]
        public required Privacy party_privacy_type;
        [JsonConverter(typeof(JsonStringEnumConverter<Discoverability>))]
        public Discoverability? discoverability;
        public required string party_type;
        public required string party_sub_type;
        public required int max_number_of_members;
        public required int invite_ttl_seconds;
        public required int intention_ttl_seconds;
        public required DateTime created_at;
        public required DateTime updated_at;

        public enum Privacy
        {
            OPEN,
            INVITE_AND_FORMER,
        }
        public enum Discoverability
        {
            ALL,
            INVITED_ONLY,
        }
    }

    //com.epicgames.social.interactions.notification.v2
    public record class InteractionNotif : XmppEpicMsg
    {
        public required Interaction[] interactions;

        public record struct Interaction
        {
            public required string _type;
            public required string fromAccountId;
            public required string toAccountId;
            [JsonPropertyName("namespace")]
            public required string Namespace;
            public string? app;
            [JsonConverter(typeof(JsonStringEnumConverter<InteractionType>))]
            public required InteractionType interactionType;
            public required long happenedAt;//parse as datetime somehow?
            public required bool isFriend;
        }

        public enum InteractionType
        {
            PingSent,
            PartyInviteSent,
            PartyJoined
        }
    }

}

//move following to party/friend  data file

public record struct AccountDisplayNames()
{
    public required string id;
    public string? displayName;
    public Dictionary<ExternalType, External> externalAuths = [];

    [JsonIgnore]
    public string? DisplayName => displayName ?? externalAuths.Select(kvp => kvp.Value.externalDisplayName).FirstOrDefault(s => s is not null);
    public record struct External()
    {
        [JsonConverter(typeof(JsonStringEnumConverter<ExternalType>))]
        public ExternalType type;
        public string externalAuthId;
        public string externalAuthIdType;
        public string externalAuthSecondaryId;
        public string externalDisplayName;
        public Id[] authIds = [];

        public record struct Id
        {
            public string id;
            public string type;
        }
    }

    public enum ExternalType
    {
        Steam,
        PSN,
        Nintendo,
        Twitch,
        XBL
        //??? idk lego or something
    }
}

public record class FriendSummary
{
    public FriendData[] friends;
    public FriendRequestData[] incoming;
    public FriendRequestData[] outgoing;
    public FriendRequestData[] suggested;
    public BlockedAccountData[] blocklist;
    public Settings settings;
    public Limits limitsReached;

    public record struct Settings
    {
        public string acceptInvites;
        public string mutualPrivacy;
    }
    public record struct Limits
    {
        public bool incoming;
        public bool outgoing;
        public bool accepted;
    }
}

public record struct BlockedAccountData()
{
    public required string accountId;
    public required DateTime created;
}
public record struct FriendRequestData()
{
    public required string accountId;
    public required int mutual;
    public bool favourite = false;
    public required DateTime created;
}
public record struct FriendData()
{
    public required string accountId;
    public JsonArray groups = [];
    public required int mutual;
    public string alias = "";
    public string note = "";
    public bool favourite = false;
    public required DateTime created;
}

public record class PartyContainer
{
    public PartyData[] current = [];
    public JsonArray pending = [];
    public PartyInviteData[] invites = [];
    public PartyPingData[] pings = [];
}

public record class PartyData
{
    public required string id;
    public required DateTime created_at;
    public required DateTime updated_at;
    public required Config config;
    [JsonConverter(typeof(PartyMemberConverter))]
    public required Dictionary<string, Member> members; //deserialise as dictionary
    public required JsonArray applicants;
    public Dictionary<string, string> meta = [];
    public required JsonArray invites;
    public required JoinRequest[] intentions;
    public int revision;

    public record struct Config
    {
        public required string type;
        public required string joinability;
        public required string discoverability;
        public required string sub_type;
        public required int max_size;
        public required int invite_ttl;
        public required bool join_confirmation;
        public required int intention_ttl;
    }

    public record class Member
    {
        public required string account_id;
        public Dictionary<string, string> meta = [];
        public Connection[] connections = [];
        public int revision;
        public required DateTime joined_at;
        public required DateTime updated_at;
        [JsonConverter(typeof(JsonStringEnumConverter<Role>))]
        public Role role = Role.MEMBER;

        public enum Role
        {
            CAPTAIN,
            MEMBER
        }
    }

    public record class JoinRequest
    {
        public required string requester_id;
        public required string requester_dn;
        public required string requester_pl;//party leader?
        public required string requester_pl_dn;
        public required DateTime sent_at;
        public required DateTime expires_at;
        public Dictionary<string, string>? meta;
    }

    public record class Connection
    {
        public required string id;
        public required DateTime connected_at;
        public required DateTime updated_at;
        public DateTime? disconnected_at = null;
        public required bool yield_leadership;
        public Dictionary<string, string>? meta = [];
    }

    class PartyMemberConverter : JsonConverter<Dictionary<string, Member>>
    {
        public override Dictionary<string, Member> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var memberArray = JsonSerializer.Deserialize<Member[]>(ref reader, options);
            return memberArray.ToDictionary(m => m.account_id);
        }

        public override void Write(Utf8JsonWriter writer, Dictionary<string, Member> value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value.Values.ToArray(), options);
        }
    }
}

public record class PartyInviteData
{
    public required string party_id;
    public required string sent_by;
    public Dictionary<string, string>? meta;
    public required string sent_to;
    public required DateTime sent_at;
    public required DateTime updated_at;
    public required DateTime expires_at;
    [JsonConverter(typeof(JsonStringEnumConverter<Status>))]
    public required Status status;

    public enum Status
    {
        SENT
    }
}

public record class PartyPingData
{
    public required string sent_by;
    public required string sent_to;
    public required DateTime sent_at;
    public required DateTime expires_at;
    public Dictionary<string, string>? meta;
}
#pragma warning restore CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.