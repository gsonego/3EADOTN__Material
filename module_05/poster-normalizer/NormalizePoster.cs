using System;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace PosterNormalizer;

public class NormalizePoster
{
    private readonly ILogger<NormalizePoster> _logger;

    public NormalizePoster(ILogger<NormalizePoster> logger)
    {
        _logger = logger;
    }

    [Function("NormalizePoster")]
    public async Task Run([QueueTrigger("poster-jobs", Connection = "AzureWebJobsStorage")] string message)
    {
        _logger.LogInformation("Raw queue message: {Message}", message);

        // Event Grid → Storage Queue delivery base64-encodes the payload. Detect and decode
        // rather than assume — this is the one piece of wiring we're validating live.
        string json = message;
        try
        {
            var decodedBytes = Convert.FromBase64String(message);
            json = Encoding.UTF8.GetString(decodedBytes);
            _logger.LogInformation("Message was base64-encoded. Decoded: {Json}", json);
        }
        catch (FormatException)
        {
            _logger.LogInformation("Message was plain text, not base64.");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string eventType = root.GetProperty("eventType").GetString()!;
        if (eventType != "Microsoft.Storage.BlobCreated")
        {
            _logger.LogInformation("Ignoring event type {EventType}", eventType);
            return;
        }

        // subject looks like: /blobServices/default/containers/posters/blobs/inception.jpg
        string subject = root.GetProperty("subject").GetString()!;
        string blobName = subject[(subject.LastIndexOf('/') + 1)..];
        _logger.LogInformation("Processing blob: {BlobName}", blobName);

        var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
        var blobClient = new BlobContainerClient(connectionString, "posters").GetBlobClient(blobName);

        var props = await blobClient.GetPropertiesAsync();
        if (props.Value.Metadata.TryGetValue("normalized", out var flag) && flag == "true")
        {
            _logger.LogInformation("{BlobName} already normalized — skipping (idempotency guard).", blobName);
            return;
        }

        var download = await blobClient.DownloadStreamingAsync();
        using var image = await Image.LoadAsync(download.Value.Content);

        // Normalize to a 2:3 poster aspect ratio, cropping to fill.
        int targetWidth = image.Width;
        int targetHeight = (int)(targetWidth * 1.5);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new SixLabors.ImageSharp.Size(targetWidth, targetHeight),
            Mode = ResizeMode.Crop
        }));

        using var output = new MemoryStream();
        await image.SaveAsJpegAsync(output);
        output.Position = 0;

        await blobClient.UploadAsync(output, overwrite: true);
        await blobClient.SetMetadataAsync(new Dictionary<string, string> { ["normalized"] = "true" });
        await blobClient.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = "image/jpeg" });

        _logger.LogInformation("{BlobName} normalized and re-uploaded.", blobName);
    }
}