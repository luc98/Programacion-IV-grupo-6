using AvailabilitySite.Application.Events;
using AvailabilitySite.Web.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace AvailabilitySite.Web.Controllers
{
    public class HomeController(IListEventsUseCase listEvents) : AppController
    {
        // Selección de evento (decisión de implementación: el enunciado no define cómo se llega a un evento).
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var result = await listEvents.ExecuteAsync(cancellationToken);
            if (!result.IsSuccess)
                Response.StatusCode = StatusCodes.Status503ServiceUnavailable;

            return View(new EventListViewModel
            {
                Events = result.IsSuccess ? result.Value!.OrderBy(e => e.StartsAt).ToList() : [],
                ErrorMessage = result.Error?.Message,
            });
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
