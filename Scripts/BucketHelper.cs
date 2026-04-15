using Amazon.S3;
using Godot;
using System;
using System.Threading.Tasks;

public static class BucketHelper
{
	public static bool CanUseBucket => TryGetBucketCreds(out _, out _, out _);

	static bool TryGetBucketCreds(out string bucketAccessID, out string bucketAccessSecret, out string bucketURL)
	{
		bucketAccessID = null;
		bucketAccessSecret = null;
		bucketURL = null;
		return
			GameAccount.ActiveAccount.isOwned &&
			AppConfig.TryGet("missions", "bucketAccessID", out bucketAccessID) &&
			AppConfig.TryGet("missions", "bucketAccessSecret", out bucketAccessSecret) &&
			AppConfig.TryGet("missions", "bucketURL", out bucketURL);
	}

	static AmazonS3Client BucketClient = null;
	public static async Task<bool> SendToBucket(string sourcePath, string destPath)
	{
		if (!TryGetBucketCreds(
			out string bucketAccessID, 
			out string bucketAccessSecret, 
			out string bucketURL
			))
			return false;
		BucketClient ??= new(bucketAccessID, bucketAccessSecret, new AmazonS3Config()
		{
			ServiceURL = bucketURL
		});
		var fullPath = ProjectSettings.GlobalizePath(sourcePath);
		var response = await BucketClient.PutObjectAsync(new()
		{
			BucketName = "pegleg-lite-data",
			Key = destPath,
			FilePath = fullPath,
			DisablePayloadSigning = true
		});
		var statusCode = (int)response.HttpStatusCode;
		if (statusCode < 200 || statusCode > 299)
		{
			GD.Print($"Bucket Upload Failure, status: {response.HttpStatusCode}");
			return false;
		}
		return true;
	}
}
