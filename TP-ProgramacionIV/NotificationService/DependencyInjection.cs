using NotificationService.Delivery;
using NotificationService.Handling;
using NotificationService.Mapping;
using NotificationService.Messaging;
using NotificationService.Messaging.Fake;
using NotificationService.Messaging.ServiceBus;
using NotificationService.Templates;

namespace NotificationService;

public static class DependencyInjection
{
    public const string EventSourceTypeKey = "EventSource:Type";
    public const string FakeSource = "Fake";
    public const string ServiceBusSource = "ServiceBus";

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

    public static IServiceCollection AddEventSource(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ServiceBusOptions>(configuration.GetSection(ServiceBusOptions.SectionName));

        var type = configuration[EventSourceTypeKey];

        if (string.IsNullOrWhiteSpace(type) || type.Equals(FakeSource, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IEventSource>(provider => new FakeEventSource(
                SampleEvents.BookingStyle(),
                provider.GetRequiredService<ILogger<FakeEventSource>>(),
                TimeSpan.FromSeconds(1)));
        }
        else if (type.Equals(ServiceBusSource, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IEventSource, ServiceBusEventSource>();
        }
        else
        {
            throw new InvalidOperationException(
                $"{EventSourceTypeKey} no es válido: '{type}'. Los valores posibles son {FakeSource} y {ServiceBusSource}.");
        }

        return services;
    }
}
