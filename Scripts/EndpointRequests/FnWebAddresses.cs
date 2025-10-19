using System;
using System.Net.Http;

static class FnWebAddresses
{
    public static readonly HttpClient service = new()
    {
        BaseAddress = new Uri("https://fortnite-public-service-prod11.ol.epicgames.com")
    };

    public static readonly HttpClient content = new()
    {
        BaseAddress = new Uri("https://fortnitecontent-website-prod07.ol.epicgames.com")
    };

    public static readonly HttpClient game = new()
    {
        BaseAddress = new Uri("https://fngw-mcp-gc-livefn.ol.epicgames.com")
    };

    public static readonly HttpClient account = new()
    {
        BaseAddress = new Uri("https://account-public-service-prod.ol.epicgames.com")
    };

    public static readonly HttpClient friends = new()
    {
        BaseAddress = new Uri("https://friends-public-service-prod.ol.epicgames.com")
    };

    public static readonly HttpClient avatar = new()
    {
        BaseAddress = new Uri("https://avatar-service-prod.identity.live.on.epicgames.com")
    };

    public static readonly HttpClient userSearch = new()
    {
        BaseAddress = new Uri("https://user-search-service-prod.ol.epicgames.com")
    };

    public static readonly HttpClient epicCDN = new()
    {
        BaseAddress = new Uri("https://cdn2.unrealengine.com")
    };

    public static readonly HttpClient party = new()
    {
        BaseAddress = new Uri("https://party-service-prod.ol.epicgames.com")
    };
}

static class WebClients
{
    public static readonly HttpClient fnApi = new()
    {
        BaseAddress = new Uri("https://fortnite-api.com")
    };
    public static readonly HttpClient fnApiJamTrakcs = new()
    {
        BaseAddress = new Uri("https://cdn.fortnite-api.com")
    };
    public static readonly HttpClient dillyApi = new()
    {
        BaseAddress = new Uri("https://export-service.dillyapis.com")
    };
    public static readonly HttpClient fnCentral = new()
    {
        BaseAddress = new Uri("https://fortnitecentral.genxgames.gg")
    };
}
