using System.Net.Http.Headers;
using Binexus.Modules.Logistics.Application;
using Binexus.Modules.Logistics.Domain;
using Binexus.Modules.Logistics.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Testcontainers.Minio;

namespace Binexus.IntegrationTests.Logistics;

[Collection("minio")]
public sealed class MinioProofStorageTests : IAsyncLifetime, IDisposable
{
    private readonly MinioContainer _minio = new MinioBuilder()
        .WithUsername("binexus")
        .WithPassword("binexus12345")
        .Build();

    private MinioObjectStorage? _storage;
    private readonly string _bucket = "binexus-proofs";
    private bool _disposed;

    public async Task InitializeAsync()
    {
        await _minio.StartAsync();
        var options = Options.Create(new LogisticsStorageOptions
        {
            Endpoint = _minio.GetConnectionString(),
            Bucket = _bucket,
            Region = "us-east-1",
            AccessKey = _minio.GetAccessKey(),
            SecretKey = _minio.GetSecretKey(),
            Provider = LogisticsStorageProviders.MinIO,
            PresignTtl = TimeSpan.FromMinutes(5),
            MaxProofBytes = 1024 * 1024,
        });
        _storage = new MinioObjectStorage(options, TimeProvider.System);
        await _storage.EnsureBucketExistsAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        Dispose();
        await _minio.DisposeAsync();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _storage?.Dispose();
        _storage = null;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Presign_put_then_head_exists_for_tenant_scoped_key()
    {
        var tenantId = Guid.CreateVersion7();
        var stopId = Guid.CreateVersion7();
        var objectId = Guid.CreateVersion7();
        var key = LogisticsCommandSupport.BuildProofObjectKey(tenantId, stopId, "photo", objectId, "image/jpeg");
        LogisticsCommandSupport.ValidateProofObjectKey(tenantId, stopId, key);

        var presigned = await _storage!.PresignPutAsync(
            new PresignPutObjectRequest(key, "image/jpeg", 12, TimeSpan.FromMinutes(5)),
            CancellationToken.None);

        presigned.UploadUrl.IsAbsoluteUri.Should().BeTrue();
        // Assert path components only — never log AbsoluteUri (includes signature query).
        presigned.UploadUrl.AbsolutePath.Should().Contain(_bucket);
        presigned.UploadUrl.AbsolutePath.Should().Contain($"tenants/{tenantId:D}/delivery-proofs/{stopId:D}/");
        presigned.UploadUrl.Query.Should().NotBeNullOrEmpty();

        (await _storage.ExistsAsync(key, CancellationToken.None)).Should().BeFalse();

        using var http = new HttpClient();
        using var content = new ByteArrayContent("proof-bytes"u8.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        var put = await http.PutAsync(presigned.UploadUrl, content);
        put.IsSuccessStatusCode.Should().BeTrue(await put.Content.ReadAsStringAsync());

        (await _storage.ExistsAsync(key, CancellationToken.None)).Should().BeTrue();
        (await _storage.ExistsAsync(key + "-missing", CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public void Repo_ships_minio_cors_json_for_localhost_web_origin()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", ".."));
        var corsPath = Path.Combine(repoRoot, "infra", "minio", "cors.json");
        File.Exists(corsPath).Should().BeTrue(corsPath);
        var json = File.ReadAllText(corsPath);
        json.Should().Contain("http://localhost:3000");
        json.Should().Contain("PUT");
    }

    [Fact]
    public async Task Cross_tenant_key_validation_rejects_before_storage_call()
    {
        var tenantId = Guid.CreateVersion7();
        var stopId = Guid.CreateVersion7();
        var otherTenant = Guid.CreateVersion7();
        var foreignKey = LogisticsCommandSupport.BuildProofObjectKey(otherTenant, stopId, "photo", Guid.CreateVersion7(), "image/png");

        var act = () => LogisticsCommandSupport.ValidateProofObjectKey(tenantId, stopId, foreignKey);
        act.Should().Throw<LogisticsDomainException>();
    }
}
