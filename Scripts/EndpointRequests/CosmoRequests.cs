using Godot;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public partial class CosmoRequests
{
	// Based off Krowe Mohs RE work (and an Ai summary document Marlon made)

	public record struct CosmoConfig(
		string gameVer = "41.20",
		string key = "fYm7gPh1KVzF1iWkD1rqGQBhAb7FHmJO4CNBCfYlZBk=",
		string baseURL = "https://cosmo.fdeb.live.use1a.on.epicgames.com/v1/item/"
	)
	{

		public static CosmoConfig PLRConfig
		{
			get
			{
				//if (PegLegResourceManager.MiscData["Cosmo"] is JsonObject cosmoData)
				//	return new(
				//		cosmoData["Version"].ToString(),
				//		cosmoData["Key"].ToString(),
				//		cosmoData["BaseURL"].ToString()
				//	);
				return FallbackConfig;
			}
		}

		public static readonly CosmoConfig FallbackConfig =
			new(
				"41.20",
				"fYm7gPh1KVzF1iWkD1rqGQBhAb7FHmJO4CNBCfYlZBk=",
				"https://cosmo.fdeb.live.use1a.on.epicgames.com/v1/item/"
			);
		public static readonly CosmoConfig LegacyConfig =
			new(
				"41.00",
				"BhmLB0jhpLStVemxcXODVXCbCdmAVHozdTbrG4+R+4E=",
				"https://cosmo.fdeb.live.use1a.on.epicgames.com/v1/item/"
			);
	}

	private static byte[] B64ToBytes(string base64)
	{
		base64 = base64.Replace("-", "+").Replace("_", "/");
		return Convert.FromBase64String(base64);
	}

	
	private static string BytesToB64(byte[] bytes)
	{
		var base64 = Convert.ToBase64String(bytes);
		base64 = base64.Replace("+", "-").Replace("/", "_");
		return B64Ending().Replace(base64, "");
		//return base64;
	}

	[GeneratedRegex("=+$")]
	private static partial Regex B64Ending();

	public static CosmoImageData GetDisplayAsset(string displayAssetName, int index = 0)
	{
		if (displayAssetName is null)
			return default;
		return GetImageData(
			$"AthenaItemShopOfferDisplayData:{displayAssetName.ToLower()}",
			"store_image",
			[index],
			"2048x2048"
		);
	}
		

	public static CosmoImageData GetItemIcon(string templateId) => 
		GetImageData(
			templateId,
			"preview_image",
			[],
			"1024x1024"
		);

	public static CosmoImageData GetItemStyleIcon(string templateId, int channel, int style) => 
		GetImageData(
			templateId,
			"preview_image",
			[channel, style],
			"1024x1024"
		);

	public static CosmoImageData GetItemPreview(string templateId, int[] stylePermutations = null) => 
		GetImageData(
			templateId,
			"locker_preview_image",
			stylePermutations,
			"1024x1024"
		);

	public static CosmoImageData GetImageData(
		string templateId,
		string descriptorSuffix,
		int[] styles = null,
		string urlSuffix = "png",
		//string templateIdExtra = null,
		CosmoConfig? config = null
	)
	{
		config ??= CosmoConfig.PLRConfig;
		if (!templateId.Contains(':'))
			return default;
		var splitTemplate = templateId.Split(':');
		if (splitTemplate.Length == 2)
			templateId = $"{splitTemplate[0]}:{splitTemplate[1].ToLower()}";
		if ((styles?.Length ?? 0) > 0)
			descriptorSuffix += $"[{string.Join(",", styles)}]";
		//if (templateIdExtra is not null)
		//	templateId += $"[{templateIdExtra}]";

		string baseDescriptor = $"fn/{config?.gameVer}/{templateId}/{descriptorSuffix}";

		byte[] hashDescriptorBytes = baseDescriptor.ToUtf8Buffer();
		byte[] releaseKeyBytes = B64ToBytes(config?.key);
		byte[] projectKeyBytes = [];// projectKey is null ? [] : B64ToBytes(projectKey);
		byte[] mergedBytes = [.. hashDescriptorBytes, .. releaseKeyBytes, .. projectKeyBytes];

		var hashDescriptor = BytesToB64(SHA256.HashData(new MemoryStream(mergedBytes)));
		//var publicDescriptor = BytesToB64(SHA256.HashData(new MemoryStream($"{baseDescriptor}/{config.key[..4]}nullnull".ToUtf8Buffer())));

		return new(
			$"{config?.baseURL}{hashDescriptor}/{urlSuffix}", //url
			$"{templateId.Replace(":","__")}-{descriptorSuffix}" //unique name for local caching
		);
	}

	public readonly record struct CosmoImageData(string url, string uniqueName)
	{
		public ImageTexture GetCachedTexture()
		{
			if (uniqueName is null)
				return null;
			if (CatalogRequests.TryGetCosmeticTexture(uniqueName, cacheOnly: true) is ImageTexture existingTexture)
				return existingTexture;
			return null;
		}

		public ImageTexture GetLocalTexture(float resolutionScale = 128)
		{
			if (uniqueName is null)
				return null;
			if (CatalogRequests.TryGetCosmeticTexture(uniqueName, resolutionScale) is ImageTexture existingTexture)
				return existingTexture;
			return null;
		}

		public Image GetLocalImage(float resolutionScale = 128)
		{
			if (uniqueName is null)
				return null;
			if (CatalogRequests.TryGetCosmeticImage(uniqueName, resolutionScale) is Image existingImage)
				return existingImage;
			return null;
		}

		public Image ReadLocalImageDirect()
		{
			if (uniqueName is null)
				return null;
			var path = CatalogRequests.LocalCosmeticResourcePathFromId(uniqueName);
			if (path is null)
				return null;
			return Image.LoadFromFile(path);
		}

		public async Task<ImageTexture> FetchTexture(float resolutionScale = 128)
		{
			if (url is null || uniqueName is null)
				return null;
			if (CatalogRequests.TryGetCosmeticTexture(uniqueName, resolutionScale) is ImageTexture existingTexture)
				return existingTexture;
			await FetchImage();
			return CatalogRequests.TryGetCosmeticTexture(uniqueName);
		}

		public async Task<Image> FetchImage(float resolutionScale = 128)
		{
			if (url is null || uniqueName is null)
				return null;
			if (CatalogRequests.TryGetCosmeticImage(uniqueName, resolutionScale) is Image existingTexture)
				return existingTexture;

			using var result = await WebHelpers.MakeRequest(url).Accepts(WebMedia.Image.Any).Send();
			if (await result.CheckForError())
				return null;

			(Image image, byte[] buffer, string type) = await result.ReadImageWithBuffer();
			//Image image = await result.ReadDownloadImage(testStream);
			if (image is null)
				return null;

			CatalogRequests.RegisterCosmeticImageWithBuffer(ref image, buffer, type, uniqueName, resolutionScale);
			return CatalogRequests.TryGetCosmeticImage(uniqueName);
		}

		public void ShellOpenRemote() => OS.ShellOpen(url);
		public void ShellOpenLocal() => OS.ShellOpen(ProjectSettings.GlobalizePath(CatalogRequests.LocalCosmeticResourcePathFromId(uniqueName)));
	}
}
