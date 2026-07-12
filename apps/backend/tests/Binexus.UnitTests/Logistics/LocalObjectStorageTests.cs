using Binexus.Modules.Logistics.Application;
using Binexus.Modules.Logistics.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Binexus.UnitTests.Logistics;

public sealed class LocalObjectStorageTests
{
    [Fact]
    public async Task TryAcceptPut_rejects_unissued_wrong_type_oversized_and_overwrite()
    {
        var storage = new LocalObjectStorage(
            Options.Create(new LogisticsStorageOptions
            {
                Provider = LogisticsStorageProviders.Local,
                Endpoint = "http://localhost:5102",
                MaxProofBytes = 1024,
            }),
            TimeProvider.System);

        var key = $"tenants/{Guid.CreateVersion7():D}/delivery-proofs/{Guid.CreateVersion7():D}/photo-{Guid.CreateVersion7():D}.jpg";
        await storage.PresignPutAsync(new PresignPutObjectRequest(key, "image/jpeg", 100, TimeSpan.FromMinutes(5)), CancellationToken.None);

        storage.TryAcceptPut("tenants/other/key.jpg", "image/jpeg", 10).Should().Be(LocalPutAcceptance.Unissued);
        storage.TryAcceptPut(key, "image/png", 10).Should().Be(LocalPutAcceptance.WrongContentType);
        storage.TryAcceptPut(key, "image/jpeg", 2048).Should().Be(LocalPutAcceptance.Oversized);
        storage.TryAcceptPut(key, "image/jpeg", 50).Should().Be(LocalPutAcceptance.Accepted);
        storage.TryAcceptPut(key, "image/jpeg", 50).Should().Be(LocalPutAcceptance.AlreadyUploaded);
        (await storage.ExistsAsync(key, CancellationToken.None)).Should().BeTrue();
    }
}
