using BookingAPI.BLL.DTOs;
using BookingAPI.DAL.Entities;

namespace BookingAPI.BLL.Interfaces
{
    // Contrato que abstrae la llamada sincrona gRPC hacia Payment gRPC API
    // implementado con las politicas de resiliencia de Polly
    // Abierto a modificaciones (temporal)
    public interface IPaymentGateway
    {
        Task<ResultadoCobroDto> CobrarReservaAsync(Reserva reserva, decimal monto);
    }
}
