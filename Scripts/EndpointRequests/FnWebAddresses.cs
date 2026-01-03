using System;

class FnWebAddresses
{
    Uri fortService = new("https://fortnite-public-service-prod11.ol.epicgames.com");
    Uri fortContent = new("https://fortnitecontent-website-prod07.ol.epicgames.com");
    Uri fortGame = new("https://fngw-mcp-gc-livefn.ol.epicgames.com");
    Uri epicAccount = new("https://account-public-service-prod.ol.epicgames.com");
    Uri epicFriends = new("https://friends-public-service-prod.ol.epicgames.com");
    Uri epicAvatar = new("https://avatar-service-prod.identity.live.on.epicgames.com");
    Uri epicUserSearch = new("https://user-search-service-prod.ol.epicgames.com");
    Uri unrealCDN = new("https://cdn2.unrealengine.com");
    Uri epicParty = new("https://party-service-prod.ol.epicgames.com");

    static FnWebAddresses standardAddresses = new();
    //static FnWebAddresses testingAddresses = new()
    //{
    //    fortService = new(""),
    //    fortContent = new(""),
    //    fortGame = new(""),
    //    epicAccount = new(""),
    //    epicFriends = new(""),
    //    epicAvatar = new(""),
    //    epicUserSearch = new(""),
    //    unrealCDN = new(""),
    //    epicParty = new(""),
    //};

    static FnWebAddresses ActiveAddresses = standardAddresses;

    public static readonly Uri FortService = ActiveAddresses.fortService;
    public static readonly Uri FortContent = ActiveAddresses.fortContent;
    public static readonly Uri FortGame = ActiveAddresses.fortGame;
    public static readonly Uri EpicAccount = ActiveAddresses.epicAccount;
    public static readonly Uri EpicFriends = ActiveAddresses.epicFriends;
    public static readonly Uri EpicAvatar = ActiveAddresses.epicAvatar;
    public static readonly Uri EpicUserSearch = ActiveAddresses.epicUserSearch;
    public static readonly Uri UnrealCDN = ActiveAddresses.unrealCDN;
    public static readonly Uri EpicParty = ActiveAddresses.epicParty;
}

class ApiWebAddresses
{
    public static readonly Uri fnDashApi = new("https://fortnite-api.com");
    public static readonly Uri fnDashApiCdn = new("https://cdn.fortnite-api.com");
    public static readonly Uri fnDotApi = new("https://api.fortniteapi.com/");
}
