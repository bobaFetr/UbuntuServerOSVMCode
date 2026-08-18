var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseMiddleware<ApiKeyAuthenticationMiddleware>();

app.MapMessageEndpoints();

app.Run();
