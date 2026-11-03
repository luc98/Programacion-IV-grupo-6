using BookingAPI.BLL.Gateways;
using BookingAPI.BLL.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace BookingAPI.Tests;

// Levanta Booking con su Program.cs real, sin los procesos en segundo plano (Service Bus y el
// worker de vencimientos), y revisa cómo quedó registrado el cliente de Payment.
public class RegistroEnProgramTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RegistroEnProgramTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            // Development activa la validación de DI al arrancar: detecta un registro faltante
            // o un Scoped usado desde un Singleton
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
        });
    }

    [Fact]
    public void ElGatewaySeResuelveConTodasSusDependencias()
    {
        using var scope = _factory.Services.CreateScope();

        Assert.IsType<PaymentGrpcGateway>(scope.ServiceProvider.GetRequiredService<IPaymentGateway>());
    }

    [Fact]
    public void ElPipelineYElTokenSonLosMismosEnTodosLosCobros()
    {
        // Si fueran uno por request, el circuit breaker nunca acumularía fallas
        // y el token se pediría en cada cobro
        using var primero = _factory.Services.CreateScope();
        using var segundo = _factory.Services.CreateScope();

        Assert.Same(
            primero.ServiceProvider.GetRequiredService<ResilienciaPago>(),
            segundo.ServiceProvider.GetRequiredService<ResilienciaPago>());
        Assert.Same(
            primero.ServiceProvider.GetRequiredService<ITokenProvider>(),
            segundo.ServiceProvider.GetRequiredService<ITokenProvider>());
    }
}
