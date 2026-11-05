using NotificationService.Delivery;
using NotificationService.Handling;
using NotificationService.Mapping;
using NotificationService.Messaging;
using NotificationService.Templates;

namespace NotificationService;

public static class DependencyInjection
{
    public static IServiceCollection AddNotificationCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<NotificationOptions>(configuration.GetSection(NotificationOptions.SectionName));
        services.Configure<MappingOptions>(configuration.GetSection(MappingOptions.SectionName));

        services.AddSingleton<TemplateRenderer>();
        services.AddSingleton<INotificationSender, ConsoleNotificationSender>();
        services.AddSingleton<IDeduplicationStore, InMemoryDeduplicationStore>();
        services.AddSingleton<IEventMapper, JsonEventMapper>();
        services.AddSingleton<NotificationHandler>();
        services.AddSingleton<IEventProcessor, EventProcessor>();

        return services;
    }
}
