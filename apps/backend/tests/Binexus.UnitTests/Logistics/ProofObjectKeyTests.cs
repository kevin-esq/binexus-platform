using Binexus.Modules.Logistics.Domain;
using Binexus.Modules.Logistics.Infrastructure;
using FluentAssertions;

namespace Binexus.UnitTests.Logistics;

public sealed class ProofObjectKeyTests
{
    [Theory]
    [InlineData("image/jpeg", "jpg")]
    [InlineData("image/png", "png")]
    [InlineData("image/webp", "webp")]
    public void BuildProofObjectKey_uses_tenant_stop_scope(string contentType, string extension)
    {
        var tenantId = Guid.CreateVersion7();
        var stopId = Guid.CreateVersion7();
        var objectId = Guid.CreateVersion7();

        var key = LogisticsCommandSupport.BuildProofObjectKey(tenantId, stopId, "photo", objectId, contentType);

        key.Should().Be($"tenants/{tenantId:D}/delivery-proofs/{stopId:D}/photo-{objectId:D}.{extension}");
        LogisticsCommandSupport.ValidateProofObjectKey(tenantId, stopId, key);
    }

    [Fact]
    public void ValidateProofObjectKey_rejects_traversal_and_cross_tenant_keys()
    {
        var tenantId = Guid.CreateVersion7();
        var stopId = Guid.CreateVersion7();
        var otherTenantId = Guid.CreateVersion7();

        Action traversal = () => LogisticsCommandSupport.ValidateProofObjectKey(tenantId, stopId, $"tenants/{tenantId:D}/delivery-proofs/{stopId:D}/../x.jpg");
        Action crossTenant = () => LogisticsCommandSupport.ValidateProofObjectKey(tenantId, stopId, $"tenants/{otherTenantId:D}/delivery-proofs/{stopId:D}/photo-x.jpg");

        traversal.Should().Throw<LogisticsDomainException>().Which.Code.Should().Be(LogisticsError.InvalidProofObjectKey);
        crossTenant.Should().Throw<LogisticsDomainException>().Which.Code.Should().Be(LogisticsError.InvalidProofObjectKey);
    }
}
