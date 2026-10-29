using BookingAPI.DAL.Entities;

namespace BookingAPI.DAL.Repositories
{
    // Patron Repository
    // Se abstrae el acceso de los datos para que BLL no tenga que manejar consultas SQL
    // De forma que con llamar al metodo de IReservaRepository (luego sera subclase con implementacion concreta) se hace la operacion SQL
    // como si llamaras a un metodo de una clase mas
    public interface IReservaRepository
    {
        Task<Asiento?> ObtenerAsientoPorIdAsync(Guid asientoId);
        Task<bool> ExisteReservaActivaParaAsientoAsync(Guid asientoId);
        Task GuardarReservaConcurrenteAsync(Reserva reserva, Asiento asiento);
        Task CrearReservaRechazadaAsync(Reserva reserva);
        Task ActualizarAsync(Reserva reserva);
        Task<Reserva?> ObtenerReservaPorSolicitudIdAsync(Guid solicitudId);
        Task<List<Reserva>> ObtenerReservasRetenidasVencidasAsync(DateTime ahoraUtc);
    }
}
