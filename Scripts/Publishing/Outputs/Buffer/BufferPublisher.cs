using Godot;
using GraphQL;
using GraphQL.Client.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
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
			//imgURLs = await ImgbbURLs(publisherContent.images);
			imgURLs = await CloudinaryURLs(publisherContent.images);
			if(imgURLs is null)
			{
				GD.Print("Buffer Images failed to upload");
				return;
			}
			//imgURLs = ["https://pbs.twimg.com/media/HSnkQcrXoAAyGzodhdh"];
		}

		if (imgURLs.Length == 0 && string.IsNullOrWhiteSpace(publisherContent.content))
			return;

		GraphQLRequests.Buffer.SetAuth(key);
		await Task.WhenAll(channels.Select(c => AttemptPublishToChannel(key, c, publisherContent.content, imgURLs)));
	}

	static async Task<string[]> ImgbbURLs(Image[] images)
	{
		string imgbbSauce = AppConfig.Get("buffer_publish", "imgbbKey", "");
		if (string.IsNullOrWhiteSpace(imgbbSauce))
			imgbbSauce = "babd90038d568f4ec4d51d88351376b4";
		var imgTasks = images.Select(img =>
			WebHelpers.MakeRequest($"https://api.imgbb.com/1/upload?expiration=300&key={imgbbSauce}", HttpMethod.Post)
			.BuildFormContent(f => f.AddImageContent("image", img))
			.Send()
		).ToArray();
		await Task.WhenAll(imgTasks);
		List<string> imageUrlList = [];
		foreach (var responseTask in imgTasks)
		{
			var response = await responseTask;
			if (await response.CheckForError())
				return null;
			var urlData = await response.ReadJson<ImgBBResponse>();
			imageUrlList.Add(urlData.data.url);
		}
		await Helpers.WaitForTimer(1);
		return [.. imageUrlList];
	}

	record struct ImgBBResponse
	{
		public Data data { get; init; }
		public record struct Data
		{
			public string url { get; init; }
		}
	}

	static async Task<string[]> CloudinaryURLs(Image[] images)
	{
		var imgTasks = images.Select(img =>
			WebHelpers.MakeRequest("https://api.cloudinary.com/v1_1/a1prkxd8/image/upload", HttpMethod.Post)
			.BuildFormContent(f => f
				.AddImageContent("file", img)
				.AddStringContent("upload_preset", "publishable_image")
			).Send()
		).ToArray();
		await Task.WhenAll(imgTasks);
		List<string> imageUrlList = [];
		foreach (var responseTask in imgTasks)
		{
			var response = await responseTask;
			if (await response.CheckForError())
				return null;
			var urlData = await response.ReadJson<CloudinaryResponse>();
			imageUrlList.Add(urlData.url);
		}
		await Helpers.WaitForTimer(1);
		return [.. imageUrlList];
	}

	record struct CloudinaryResponse
	{
		public string url { get; init; }
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
			var mutation = await GraphQLRequests.Buffer.SendMutationAsync<JsonObject>(orgsRequest);
			if (mutation.Errors is not null)
			{
				var log = $"Buffer Errors: [\n{mutation.Errors.Select(e => 
					$"{e.Message} (\"{e.Path}\", [{e.Locations?.Select(l => 
						$"(l:{l.Line},c:{l.Column})").JoinString()
					}], [{e.Extensions.Select(kvp => 
						$"{kvp.Key}:({kvp.Value})").JoinString()
					}])").JoinString(",\n")
				}\n]".FixNewlines();
				GD.Print(log);
			} else if (Bootstrap.IsEditor)
			{
				GD.Print("Buffer Response: " + mutation.Data.ToString().FixNewlines());
			}
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
