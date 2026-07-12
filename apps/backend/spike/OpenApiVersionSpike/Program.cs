var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
var app = builder.Build();
app.MapGet("/health", () => Results.Ok()).WithName("Health");
app.Run();
