using BookingAPI.BLL.DTOs;

namespace BookingAPI.BLL.Interfaces
{
    // Define los contratos de operaciones de negocio que van procesar solicitudes y obtener el estado de la solicitud
    // Que luego van a ser consumidas por la capa de Presentacion
    public interface IBookingService
    {
        Task ProcesarSolicitudReservaAsync(SolicitudReservaDto solicitud);
        Task<ReservaResultadoDto?> ObtenerEstadoPorSolicitudIdAsync(Guid solicitudId);
    }
}
