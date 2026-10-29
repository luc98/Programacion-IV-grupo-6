using BookingAPI.DAL.Entities;

namespace BookingAPI.BLL.Interfaces
{
    // Contrato para publicar eventos asincronos en Azure Service Bus hacia el Servicio de Notificaciones
    // cuando la reserva es creada (Retenida) y cuando es confirmada tras el pago exitoso (Confirmada).
    // Abierto a modificaciones (temporal)
    public interface IEventPublisher
    {
        Task PublicarReservaCreadaAsync(Reserva reserva);
        Task PublicarReservaConfirmadaAsync(Reserva reserva);
    }
}
