namespace BookingAPI.BLL.Interfaces
{
    // Entrega el token de Keycloak con el que Booking se identifica ante Payment
    // (flujo client credentials, máquina a máquina)
    public interface ITokenProvider
    {
        Task<string> ObtenerTokenAsync(CancellationToken cancellationToken);

        // Descarta el token guardado: el próximo cobro pide uno nuevo.
        // Se usa cuando Payment lo rechaza (Unauthenticated).
        void Invalidar();
    }
}
