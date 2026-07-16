using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Binexus.IntegrationTests.Infrastructure;

/// <summary>
/// Normal API fixtures declare Cloud explicitly. Validation tests must not use this factory.
/// </summary>
public class CloudApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Binexus:RuntimeMode", "Cloud");
        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-with-more-than-32-bytes");
        builder.UseSetting("CloudActivation:CodePepper", "integration-test-cloud-activation-pepper-32b");
    }
}
