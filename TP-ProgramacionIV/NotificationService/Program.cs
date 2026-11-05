using NotificationService;
using NotificationService.Http;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = NotificationEndpoint.MaxBodyBytes);
builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services));
builder.Services.AddNotificationCore(builder.Configuration);

var app = builder.Build();
app.MapNotificationEndpoint();
app.Run();

public partial class Program;
