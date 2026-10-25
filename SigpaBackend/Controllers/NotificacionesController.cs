using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SigpaBackend.Models;

namespace SigpaBackend.Controllers;

[ApiController]
[Route("api/notificaciones")]
[Authorize]
public sealed class NotificacionesController : ControllerBase
{
    [HttpGet]
    public IActionResult ObtenerNotificaciones()
    {
        return Ok(Array.Empty<Notificacion>());
    }
}
