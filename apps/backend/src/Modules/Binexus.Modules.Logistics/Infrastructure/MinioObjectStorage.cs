using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Binexus.Modules.Logistics.Application;
using Microsoft.Extensions.Options;

namespace Binexus.Modules.Logistics.Infrastructure;

/// <summary>
/// S3-compatible object storage for MinIO. Ops use InternalEndpoint; presigned URLs use PublicEndpoint.
/// Credentials come from configuration only — never from source.
/// </summary>
public sealed class MinioObjectStorage : IObjectStorage, IDisposable
{
    private readonly AmazonS3Client _opsClient;
    private readonly AmazonS3Client _presignClient;
    private readonly bool _ownsPresignClient;
    private readonly LogisticsStorageOptions _options;
    private readonly TimeProvider _clock;

    public MinioObjectStorage(IOptions<LogisticsStorageOptions> options, TimeProvider clock)
    {
        _options = options.Value;
        _clock = clock;
        if (string.IsNullOrWhiteSpace(_options.Bucket))
        {
            throw new InvalidOperationException("Logistics:Storage:Bucket must be configured for MinIO.");
        }

        var internalEndpoint = _options.ResolveInternalEndpoint();
        var publicEndpoint = _options.ResolvePublicEndpoint();

        _opsClient = CreateClient(internalEndpoint, _options);
        if (string.Equals(internalEndpoint, publicEndpoint, StringComparison.OrdinalIgnoreCase))
        {
            _presignClient = _opsClient;
            _ownsPresignClient = false;
        }
        else
        {
            _presignClient = CreateClient(publicEndpoint, _options);
            _ownsPresignClient = true;
        }
    }

    public Task<PresignedPutObject> PresignPutAsync(PresignPutObjectRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var publicEndpoint = _options.ResolvePublicEndpoint();
        var expiresAt = _clock.GetUtcNow().Add(request.ExpiresIn);
        var useHttp = publicEndpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        var url = _presignClient.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = request.ObjectKey,
            Verb = HttpVerb.PUT,
            Expires = expiresAt.UtcDateTime,
            ContentType = request.ContentType,
            Protocol = useHttp ? Protocol.HTTP : Protocol.HTTPS,
        });

        return Task.FromResult(new PresignedPutObject(new Uri(url), expiresAt));
    }

    public async Task<bool> ExistsAsync(string objectKey, CancellationToken ct)
    {
        try
        {
            await _opsClient.GetObjectMetadataAsync(_options.Bucket, objectKey, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task EnsureBucketExistsAsync(CancellationToken ct)
    {
        var buckets = await _opsClient.ListBucketsAsync(ct);
        if (buckets.Buckets.Any(b => string.Equals(b.BucketName, _options.Bucket, StringComparison.Ordinal)))
        {
            return;
        }

        await _opsClient.PutBucketAsync(new PutBucketRequest { BucketName = _options.Bucket }, ct);
    }

    public void Dispose()
    {
        _opsClient.Dispose();
        if (_ownsPresignClient)
        {
            _presignClient.Dispose();
        }
    }

    private static AmazonS3Client CreateClient(string endpoint, LogisticsStorageOptions options)
    {
        var useHttp = endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        var config = new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = true,
            UseHttp = useHttp,
            AuthenticationRegion = string.IsNullOrWhiteSpace(options.Region) ? "us-east-1" : options.Region,
        };
        return new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKey, options.SecretKey),
            config);
    }
}
