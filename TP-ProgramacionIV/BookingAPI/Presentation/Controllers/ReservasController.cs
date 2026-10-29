using BookingAPI.BLL.DTOs;
using BookingAPI.BLL.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingAPI.Presentation.Controllers
{
    // Controlador RESTful que expone recursos con url (/api/reservas) y verbos HTTP
    // Depende de la interfaz de la capa BLL (IBookingService),
    // respetando la direccion lineal de dependencias (Presentation --> BLL --> DAL)
    [Authorize]
    [ApiController]
    [Route("api/reservas")]
    public class ReservasController : ControllerBase
    {
        private readonly IBookingService _bookingService;

        public ReservasController(IBookingService bookingService)
        {
            _bookingService = bookingService;
        }

        // Permite a Availability Site consultar el resultado de una solicitud de reserva
        // con sus respectivos estados: retenida, confirmada, rechazada y su motivo o vencida
        [HttpGet("solicitudes/{solicitudId:guid}")]
        [ProducesResponseType(typeof(ReservaResultadoDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ObtenerEstadoSolicitud(Guid solicitudId)
        {
            // Extraer el TraceId / CorrelationId de los headers HTTP entrantes si corresponde
            // y registrar la consulta de seguimiento.

            ReservaResultadoDto? resultado = await _bookingService.ObtenerEstadoPorSolicitudIdAsync(solicitudId);

            if (resultado is null)
            {
                return NotFound(new
                {
                    mensaje = "No se encontro una reserva asociada a la solicitud indicada.",
                    solicitudId
                });
            }

            return Ok(resultado);
        }
    }
}
