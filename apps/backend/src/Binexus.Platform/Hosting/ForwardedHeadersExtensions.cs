using System.Net;
using Binexus.Platform.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.Platform.Hosting;

public static class ForwardedHeadersServiceCollectionExtensions
{
    public static IServiceCollection AddBinexusForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 2;
            options.RequireHeaderSymmetry = false;

            var security = configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>()
                ?? new SecurityOptions();

            foreach (var proxy in security.TrustedProxies)
            {
                if (IPAddress.TryParse(proxy, out var ip))
                {
                    options.KnownProxies.Add(ip);
                }
            }

            foreach (var network in security.TrustedNetworks)
            {
                if (TryParseCidr(network, out var parsed))
                {
                    options.KnownIPNetworks.Add(parsed);
                }
            }
        });

        return services;
    }

    private static bool TryParseCidr(string cidr, out System.Net.IPNetwork network)
    {
        network = default!;
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var ip) || !int.TryParse(parts[1], out var prefix))
        {
            return false;
        }

        network = new System.Net.IPNetwork(ip, prefix);
        return true;
    }
}

public static class ForwardedHeadersApplicationBuilderExtensions
{
    public static WebApplication UseBinexusForwardedHeaders(this WebApplication app)
    {
        app.UseForwardedHeaders();
        return app;
    }
}
