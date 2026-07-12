using System.Diagnostics;
using System.Globalization;
using Binexus.Platform.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Binexus.Platform.Hosting;

public static class BinexusHostingExtensions
{
    public static WebApplicationBuilder AddBinexusSerilog(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((context, services, configuration) =>
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "Binexus.Api")
                .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

        return builder;
    }

    public static WebApplication UseBinexusProblemDetails(this WebApplication app)
    {
        app.UseExceptionHandler(exceptionApp =>
        {
            exceptionApp.Run(async context =>
            {
                var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
                var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("ExceptionHandler");

                if (exception is not null)
                {
                    PlatformLog.UnhandledException(logger, exception, context.Request.Method, context.Request.Path);
                }

                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/problem+json";

                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "An unexpected error occurred.",
                    Type = "https://httpstatuses.com/500",
                    Instance = context.Request.Path,
                };

                if (app.Environment.IsDevelopment())
                {
                    problem.Detail = exception?.Message;
                }

                await context.Response.WriteAsJsonAsync(problem);
            });
        });

        return app;
    }

    public static WebApplication UseBinexusSecurityDefaults(this WebApplication app)
    {
        app.UseBinexusForwardedHeaders();

        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        return app;
    }

    public static string GetCorrelationId(this HttpContext context) =>
        Activity.Current?.Id ?? context.TraceIdentifier;
}
