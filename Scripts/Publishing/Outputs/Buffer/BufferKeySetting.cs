using Godot;
using GraphQL;
using GraphQL.Client.Abstractions;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

public partial class BufferKeySetting : Node
{
	//string lastKnownBufferKey = null;
	public override void _Ready()
	{
		//lastKnownBufferKey = AppConfig.Get("buffer_publish", "key", "");
		AppConfig.OnConfigChanged += OnConfigChanged;
	}

	private void OnConfigChanged(string section, string key, JsonNode value)
	{
		//if publishing key changes, try fetch orgs and channels
		if (section == "buffer_publish" && key == "key")
			FetchOrgsAndChannels();
	}

	public static bool TryGetBufferKey(out string key)
	{
		key = AppConfig.Get("buffer_publish", "key", "");
		return !string.IsNullOrWhiteSpace(key);
	}

	public static async void FetchOrgsAndChannels()
	{
		if (!TryGetBufferKey(out var key))
			return;
		GraphQLRequests.Buffer.SetAuth(key);
		using var _ = LoadingOverlay.CreateToken();

		var orgsRequest = new GraphQLRequest
		{
			Query = """
			query GetOrganizations {
			  account {
			    organizations {
			      id
			      name
			    }
			  }
			}
			""",
			OperationName = "GetOrganizations"
		};
		var orgsResponse = await GraphQLRequests.Buffer.SendQueryAsync<JsonObject>(orgsRequest);
		if (orgsResponse.CheckForErrors())
			return;
		var data = orgsResponse.Data;

		//JsonObject data = JsonNode.Parse("""
		//	{
		//	  "account": {
		//	    "organizations": [
		//	      {
		//	        "id": "bleh",
		//	        "name": "PegLeg"
		//	      }
		//	    ]
		//	  }
		//	}

		//	""").AsObject();
		//var orgs = orgsResponse.Data;
		GD.Print(data);
		var orgs = data["account"]["organizations"].Deserialize<Organization[]>();

		var channelsRequest = new GraphQLRequest
		{
			Query = $$"""
			query GetChannels {
			  {{string.Join("\n ", orgs.Select(o => $$"""
				org_{{o.id}}: channels(input: { organizationId: "{{o.id}}" }) {
				  id
				  name
				  avatar
				  service
				}
			"""))}}
			}
			""",
			OperationName = "GetChannels"
		};
		var channelsResponse = await GraphQLRequests.Buffer.SendQueryAsync<Dictionary<string, Channel[]>>(channelsRequest);
		if (channelsResponse.CheckForErrors())
			return;
		for (int i = 0; i < orgs.Length; i++)
		{
			if (channelsResponse.Data.TryGetValue("org_" + orgs[i].id, out var channels))
				orgs[i] = orgs[i] with { channels = channels };
		}

		knownOrganizations = orgs.ToDictionary(o => o.id);
		knownChannels = Organizations.SelectMany(o => o.channels).ToDictionary(c => c.id);
		AppConfig.SetSerialised("buffer_publish", "organizations", Organizations);
	}

	public static void LoadLocalOrgsAndChannels()
	{
		var allOrgs = AppConfig.Get<Organization[]>("buffer_publish", "organizations", []);
		knownOrganizations = allOrgs.ToDictionary(o => o.id);
		knownChannels = allOrgs.SelectMany(o => o.channels).ToDictionary(c => c.id);
		if (TryGetBufferKey(out var key))
			GraphQLRequests.Buffer.SetAuth(key);
	}

	public static bool TryGetOrganizationFromId(string channelId, out Organization org) => knownOrganizations.TryGetValue(channelId, out org);
	public static bool TryGetChannelFromId(string channelId, out Channel channel) => knownChannels.TryGetValue(channelId, out channel);
	public static Organization[] Organizations => [.. knownOrganizations.Values];

	static Dictionary<string, Organization> knownOrganizations = [];
	static Dictionary<string, Channel> knownChannels = [];

	public record struct Organization
	{
		public string id { get; init; }
		public string name { get; init; }
		public Channel[] channels { get; init; }
		//[JsonIgnore]
		//public Dictionary<string, Channel> ChannelDict => channels.ToDictionary(c => c.id);
	}

	public record struct Channel
	{
		public string id { get; init; }
		public string name { get; init; }
		public string avatar { get; init; }
		public string service { get; init; }
	}
}