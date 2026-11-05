using BookingAPI.BLL.Interfaces;
using BookingAPI.BLL.Options;
using Grpc.Net.Client;
using Grupo6.Payment.Contracts.V1;

namespace BookingAPI.BLL.Gateways
{
    // [DUPLA 3 - CLIENTE gRPC DE PAYMENT CON RESILIENCIA (PUNTOS 3.e Y 3.j)]
    // Registro de todo lo que necesita el cobro a Payment. Está en un solo método para que
    // Program.cs y la demo de resiliencia (proyecto DemoResiliencia) usen exactamente el mismo.
    public static class ClientePaymentExtensions
    {
        public static IServiceCollection AddClientePayment(this IServiceCollection services, IConfiguration configuration)
        {
            // Canal y cliente gRPC Singleton: un GrpcChannel mantiene abierta la conexión HTTP/2 y
            // es seguro compartirlo entre hilos. Crear uno por cobro abriría una conexión nueva cada vez.
            services.AddSingleton(_ =>
            {
                // En Docker: http://payment:8080 (HTTP/2 sin TLS dentro de la red interna)
                var paymentUrl = configuration["GrpcSettings:PaymentServiceUrl"];
                return GrpcChannel.ForAddress(
                    string.IsNullOrWhiteSpace(paymentUrl) ? "http://localhost:5260" : paymentUrl,
                    new GrpcChannelOptions
                    {
                        // Una llamada cancelada (por ejemplo, por el timeout de Polly) llega como
                        // OperationCanceledException, que es lo que reconoce el timeout de Polly. Sin
                        // esto llegaría como RpcException(Cancelled) y el intento vencido no se reintentaría.
                        ThrowOperationCanceledOnCancellation = true,

                        // Con Payment caído, el canal reintenta conectar en segundo plano con esperas
                        // que crecen hasta 2 minutos por defecto, y mientras espera rechaza las llamadas
                        // sin intentar. Con un tope de 5 s, cuando Payment vuelve el canal reconecta
                        // antes de que termine el corte de 15 s del circuit breaker.
                        InitialReconnectBackoff = TimeSpan.FromSeconds(1),
                        MaxReconnectBackoff = TimeSpan.FromSeconds(5)
                    });
            });
            services.AddSingleton(sp => new PaymentService.PaymentServiceClient(sp.GetRequiredService<GrpcChannel>()));

            // Pipeline de Polly Singleton: el circuit breaker necesita recordar las fallas entre cobros
            services.Configure<ResilienciaPagoOptions>(configuration.GetSection(ResilienciaPagoOptions.Seccion));
            services.AddSingleton<ResilienciaPago>();

            // Token de Keycloak (client credentials) guardado y reutilizado hasta que vence
            services.AddSingleton(TimeProvider.System);
            services.AddHttpClient(KeycloakTokenProvider.NombreHttpClient, http => http.Timeout = TimeSpan.FromSeconds(5));
            services.AddSingleton<ITokenProvider, KeycloakTokenProvider>();

            services.AddScoped<IPaymentGateway, PaymentGrpcGateway>();
            return services;
        }

        // Crea el canal y el pipeline al arrancar. Una URL o un valor de ResilienciaPago inválido
        // corta el arranque con un mensaje claro, en lugar de romper recién en el primer cobro.
        public static void ValidarClientePayment(this IServiceProvider services)
        {
            services.GetRequiredService<GrpcChannel>();
            services.GetRequiredService<ResilienciaPago>();
        }
    }
}
