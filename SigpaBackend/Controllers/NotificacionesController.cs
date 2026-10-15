using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SigpaBackend.Data;
using SigpaBackend.Services;

namespace SigpaBackend.Controllers;

[ApiController]
[Route("api/notificaciones")]
[Authorize]
public sealed class NotificacionesController : ControllerBase
{
    private readonly SigpaContext _context;

    public NotificacionesController(SigpaContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> ObtenerNotificaciones()
    {
        var userId = UserAccessService.GetUserId(User);
        if (userId is null) return Unauthorized();

        var notifications = await _context.Notificaciones
            .Where(notification => notification.UsuarioId == userId.Value)
            .OrderByDescending(notification => notification.CreadaAt)
            .Take(100)
            .ToListAsync();

        return Ok(notifications);
    }
}
