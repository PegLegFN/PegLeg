using Godot;
using GraphQL;
using GraphQL.Client.Abstractions;
using GraphQL.Client.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public class BufferPublisher : IPublisher
{
	string IPublisher.PlatformId => "Buffer";

	string internalName;
	public void Configure(PublisherConfig config)
	{
		internalName = config.identifier;
	}

	public async Task AttemptPublish(PublisherContent publisherContent)
	{
		if (!AppConfig.Get("buffer_publish", internalName + "_enabled", false))
			return;
		if (!BufferKeySetting.TryGetBufferKey(out var key))
			return;
		var channels = AppConfig.Get<string[]>("buffer_publish", internalName + "_channels", []);
		if (channels.Length == 0)
			return;

		string[] imgURLs = [];
		if ((publisherContent.images?.Length ?? 0) > 0)
		{
			string imgbbSauce = AppConfig.Get("buffer_publish", "imgbbKey", "");
			if (string.IsNullOrWhiteSpace(imgbbSauce))
				imgbbSauce = "babd90038d568f4ec4d51d88351376b4";
			var imgTasks = publisherContent.images.Select(img =>
				WebHelpers.MakeRequest($"https://api.imgbb.com/1/upload?expiration=60&key={imgbbSauce}", HttpMethod.Post)
				.BuildFormContent(f => f.AddImageContent("image", img))
				.Send()
			).ToArray();
			await Task.WhenAll(imgTasks);
			List<string> imageUrlList = [];
			foreach (var responseTask in imgTasks)
			{
				var response = await responseTask;
				if (await response.CheckForError())
					continue;
				var urlData = await response.ReadJson<ImgBBResponse>();
				imageUrlList.Add(urlData.data.url);
			}
			imgURLs = [.. imageUrlList];
		}

		if (imgURLs.Length == 0 && string.IsNullOrWhiteSpace(publisherContent.content))
			return;

		GraphQLRequests.Buffer.SetAuth(key);
		await Task.WhenAll(channels.Select(c => AttemptPublishToChannel(key, c, publisherContent.content, imgURLs)));
	}

	record struct ImgBBResponse
	{
		public Data data { get; init; }
		public record struct Data
		{
			public string url { get; init; }
		}
	}

	async Task AttemptPublishToChannel(string key, string channelId, string content, string[] imageURLs)
	{
		var orgsRequest = new GraphQLRequest
		{
			Query = $$"""
			mutation CreateFirstPost($textContent:String) {
			  createPost(input: {
			    text: $textContent,
			    channelId: "{{channelId}}",
			    schedulingType: automatic,
			    mode: shareNow,
				assets:[
				{{string.Join("\n", imageURLs.Select(img => $$"""{image: {url:"{{img}}"} }"""))}}
				],
				saveToDraft:false
			  }) {
			    ... on PostActionSuccess {
			      post {
			        id
			        text
			      }
			    }
			    ... on MutationError {
			      message
			    }
			  }
			}
			""",
			OperationName = "CreateFirstPost",
			Variables = new
			{
				textContent = content
			}
		};
		try
		{
			var test = await GraphQLRequests.Buffer.SendMutationAsync<JsonObject>(orgsRequest);
			if (test.Data["createPost"]["message"]?.ToString() is string errMessage)
				GD.Print("Buffer Error: " + errMessage);
		}
		catch(GraphQLHttpRequestException reqEx)
		{
			var errContent = JsonNode.Parse(reqEx.Content);
			GD.Print("Buffer HTTP Exception: " + reqEx);
		}
		catch(Exception e)
		{
			//uhhh idk
			GD.Print("Uncaught Buffer Exception: " + e);
		}
	}
	//record struct CreatedPost
	//{
	//	public string id { get; init; }
	//	public string text { get; init; }
	//	public string message { get; init; }
	//}
}
