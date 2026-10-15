using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SigpaBackend.Data;
using SigpaBackend.Models;
using SigpaBackend.Services;

namespace SigpaBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class EntregablesController : ControllerBase
    {
        private readonly SigpaContext _context;
        private readonly UserAccessService _access;
        private readonly AuditService _audit;
        private readonly IConfiguration _configuration;
        private readonly SupabaseStorageService _storage;
        private readonly ILogger<EntregablesController> _logger;

        public EntregablesController(
            SigpaContext context,
            UserAccessService access,
            AuditService audit,
            IConfiguration configuration,
            SupabaseStorageService storage,
            ILogger<EntregablesController> logger)
        {
            _context = context;
            _access = access;
            _audit = audit;
            _configuration = configuration;
            _storage = storage;
            _logger = logger;
        }

        [HttpGet("/api/projects/{projectId:guid}/milestones")]
        public async Task<IActionResult> ObtenerHitosDelProyecto(Guid projectId)
        {
            var userId = UserAccessService.GetUserId(User);
            if (userId is null) return Unauthorized();
            if (!await _access.CanReadProjectAsync(projectId, userId.Value)) return NotFound();

            var milestones = await _context.Entregables
                .Where(deliverable => deliverable.ProyectoId == projectId)
                .OrderBy(deliverable => deliverable.FechaLimite)
                .ToListAsync();
            return Ok(milestones);
        }

        [HttpPost("/api/projects/{projectId:guid}/deliverables")]
        [RequestSizeLimit(26 * 1024 * 1024)]
        public async Task<IActionResult> SubirEntregaDelProyecto(
            Guid projectId,
            [FromForm] SubirEntregableProyectoDto request,
            CancellationToken cancellationToken)
        {
            var userId = UserAccessService.GetUserId(User);
            if (userId is null) return Unauthorized();
            if (!await _access.IsProjectMemberAsync(projectId, userId.Value))
                return NotFound("Proyecto no encontrado.");

            if (request.HitoId == Guid.Empty)
                return BadRequest("Selecciona el hito de entrega.");
            var deliverable = await _context.Entregables.FirstOrDefaultAsync(item =>
                item.Id == request.HitoId && item.ProyectoId == projectId && item.Estado == "Pendiente");
            if (deliverable is null)
                return Conflict("El hito seleccionado no está pendiente o no pertenece al proyecto.");

            if ((request.Archivo is null) == string.IsNullOrWhiteSpace(request.Url))
                return BadRequest("Adjunta un archivo PDF/ZIP o proporciona un enlace HTTPS.");

            var submittedAt = DateTimeOffset.UtcNow;
            var isLate = submittedAt > deliverable.FechaLimite;
            if (isLate && !deliverable.PermiteEntregaExtemporanea)
                return BadRequest("La fecha límite ha pasado. No se permiten entregas extemporáneas (RN-01).");

            string evidenceLocation;
            if (request.Archivo is not null)
            {
                try
                {
                    evidenceLocation = await _storage.UploadAsync(
                        projectId,
                        deliverable.Id,
                        request.Archivo,
                        cancellationToken);
                }
                catch (InvalidDataException exception)
                {
                    return BadRequest(exception.Message);
                }
                catch (InvalidOperationException exception)
                {
                    _logger.LogError(exception, "El almacenamiento de entregas no está configurado.");
                    return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "El almacenamiento de entregas no está disponible.");
                }
                catch (HttpRequestException exception)
                {
                    _logger.LogError(exception, "Falló la carga del hito {EntregableId}.", deliverable.Id);
                    return Problem(statusCode: StatusCodes.Status502BadGateway,
                        title: "El almacenamiento no pudo guardar el archivo.");
                }
            }
            else
            {
                var rawUrl = request.Url!.Trim();
                if (rawUrl.Length > 2048 ||
                    !Uri.TryCreate(rawUrl, UriKind.Absolute, out var url) ||
                    url.Scheme != Uri.UriSchemeHttps ||
                    !EsOrigenPermitido(url))
                    return BadRequest("Use un enlace HTTPS de GitHub, Drive o el almacenamiento privado configurado.");

                var filePath = Uri.UnescapeDataString(url.AbsolutePath);
                var allowedFile = filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
                                  filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
                if (!allowedFile && !IsAllowedWebLink(url))
                    return BadRequest("Solo se admiten archivos PDF/ZIP o enlaces HTTPS de GitHub y Drive.");

                evidenceLocation = rawUrl;
            }

            var status = isLate ? "Entregado Tardío" : "Entregado a Tiempo";
            deliverable.ArchivoUrl = evidenceLocation;
            deliverable.FechaEntrega = submittedAt.UtcDateTime;
            deliverable.Estado = status;
            deliverable.PenalizacionAplicadaPct = isLate
                ? deliverable.PenalizacionExtemporaneaPct
                : 0;
            _context.EntregasVersiones.Add(new EntregaVersion
            {
                EntregableId = deliverable.Id,
                EnviadaPor = userId.Value,
                ArchivoUrl = evidenceLocation,
                EnviadaAt = submittedAt.UtcDateTime,
                Estado = status,
                PenalizacionAplicadaPct = deliverable.PenalizacionAplicadaPct
            });
            _audit.Record(userId, "entrega.registrada", "entregable", deliverable.Id);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                hitoId = deliverable.Id,
                fecha = submittedAt,
                estado = status,
                selloTiempoUtc = submittedAt,
                penalizacionPct = deliverable.PenalizacionAplicadaPct
            });
        }

        [HttpPost]
        public async Task<IActionResult> CrearEntregable([FromBody] Entregable entregable)
        {
            var userId = UserAccessService.GetUserId(User);
            if (userId is null) return Unauthorized();
            if (entregable.FechaLimite == default)
                return BadRequest("La fecha límite es obligatoria.");
            if (entregable.PorcentajeNota is < 0 or > 100 ||
                entregable.PenalizacionExtemporaneaPct is < 0 or > 100)
                return BadRequest("Las ponderaciones deben estar entre 0 y 100.");
            if (!entregable.PermiteEntregaExtemporanea)
                entregable.PenalizacionExtemporaneaPct = 0;

            var projectExists = await _context.Proyectos.AnyAsync(project => project.Id == entregable.ProyectoId);
            if (!projectExists) return NotFound("Proyecto no encontrado.");
            if (!await _access.CanReviewProjectAsync(entregable.ProyectoId, userId.Value)) return Forbid();

            entregable.Id = Guid.NewGuid();
            entregable.ArchivoUrl = string.Empty;
            entregable.FechaEntrega = null;
            entregable.Estado = "Pendiente";
            _context.Entregables.Add(entregable);
            _audit.Record(userId, "hito.creado", "entregable", entregable.Id);
            await _context.SaveChangesAsync();
            
            return CreatedAtAction(nameof(CrearEntregable), new { id = entregable.Id }, entregable);
        }

        [HttpPost("{id}/entregar")]
        public async Task<IActionResult> SubirEntregable(Guid id, [FromBody] SubirAvanceDto avance)
        {
            var entregable = await _context.Entregables.FindAsync(id);
            if (entregable == null) return NotFound("Entregable no encontrado.");

            var userId = UserAccessService.GetUserId(User);
            if (userId is null) return Unauthorized();
            if (!await _access.IsProjectMemberAsync(entregable.ProyectoId, userId.Value))
                return NotFound("Entregable no encontrado.");
            if (string.IsNullOrWhiteSpace(avance.ArchivoUrl) || avance.ArchivoUrl.Length > 2048 ||
                !Uri.TryCreate(avance.ArchivoUrl, UriKind.Absolute, out var url) ||
                url.Scheme != Uri.UriSchemeHttps ||
                !EsOrigenPermitido(url))
            {
                return BadRequest("Use un enlace HTTPS de GitHub, Drive o el almacenamiento privado configurado.");
            }

            var path = Uri.UnescapeDataString(url.AbsolutePath);
            var isAllowedFile = path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
                               path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            var isAllowedWebLink = IsAllowedWebLink(url);
            if (!isAllowedFile && !isAllowedWebLink)
            {
                return BadRequest("Solo se admiten archivos PDF/ZIP o enlaces HTTPS de GitHub y Drive.");
            }

            var fechaActual = DateTimeOffset.UtcNow;
            var esTardia = fechaActual > entregable.FechaLimite;

            if (esTardia && !entregable.PermiteEntregaExtemporanea)
            {
                return BadRequest("La fecha límite ha pasado. No se permiten entregas extemporáneas (RN-01).");
            }

            entregable.ArchivoUrl = avance.ArchivoUrl;
            entregable.FechaEntrega = fechaActual.UtcDateTime;
            entregable.Estado = esTardia ? "Entregado Tardío" : "Entregado a Tiempo";
            entregable.PenalizacionAplicadaPct = esTardia
                ? entregable.PenalizacionExtemporaneaPct
                : 0;

            _context.EntregasVersiones.Add(new EntregaVersion
            {
                EntregableId = id,
                EnviadaPor = userId.Value,
                ArchivoUrl = avance.ArchivoUrl,
                EnviadaAt = fechaActual.UtcDateTime,
                Estado = entregable.Estado,
                PenalizacionAplicadaPct = entregable.PenalizacionAplicadaPct
            });
            _audit.Record(userId, "entrega.registrada", "entregable", id);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                mensaje = "Avance registrado correctamente.",
                fecha = fechaActual,
                estado = entregable.Estado,
                penalizacionPct = entregable.PenalizacionAplicadaPct
            });
        }

        [HttpPost("{id:guid}/archivo")]
        [RequestSizeLimit(26 * 1024 * 1024)]
        public async Task<IActionResult> SubirArchivo(
            Guid id,
            [FromForm] IFormFile archivo,
            CancellationToken cancellationToken)
        {
            var deliverable = await _context.Entregables.FindAsync(id);
            if (deliverable is null) return NotFound("Entregable no encontrado.");

            var userId = UserAccessService.GetUserId(User);
            if (userId is null) return Unauthorized();
            if (!await _access.IsProjectMemberAsync(deliverable.ProyectoId, userId.Value))
                return NotFound("Entregable no encontrado.");
            if (archivo is null) return BadRequest("Debe adjuntar un archivo PDF o ZIP.");

            var fechaActual = DateTimeOffset.UtcNow;
            var esTardia = fechaActual > deliverable.FechaLimite;
            if (esTardia && !deliverable.PermiteEntregaExtemporanea)
                return BadRequest("La fecha límite ha pasado. No se permiten entregas extemporáneas (RN-01).");

            string objectKey;
            try
            {
                objectKey = await _storage.UploadAsync(
                    deliverable.ProyectoId,
                    deliverable.Id,
                    archivo,
                    cancellationToken);
            }
            catch (InvalidDataException exception)
            {
                return BadRequest(exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                _logger.LogError(exception, "El almacenamiento de entregas no está configurado.");
                return Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "El almacenamiento de entregas no está disponible.");
            }
            catch (HttpRequestException exception)
            {
                _logger.LogError(exception, "Falló la carga del entregable {EntregableId}.", id);
                return Problem(
                    statusCode: StatusCodes.Status502BadGateway,
                    title: "El almacenamiento no pudo guardar el archivo.");
            }

            deliverable.ArchivoUrl = objectKey;
            deliverable.FechaEntrega = fechaActual.UtcDateTime;
            deliverable.Estado = esTardia ? "Entregado Tardío" : "Entregado a Tiempo";
            deliverable.PenalizacionAplicadaPct = esTardia
                ? deliverable.PenalizacionExtemporaneaPct
                : 0;
            _context.EntregasVersiones.Add(new EntregaVersion
            {
                EntregableId = id,
                EnviadaPor = userId.Value,
                ArchivoUrl = objectKey,
                EnviadaAt = fechaActual.UtcDateTime,
                Estado = deliverable.Estado,
                PenalizacionAplicadaPct = deliverable.PenalizacionAplicadaPct
            });
            _audit.Record(userId, "entrega.archivo_subido", "entregable", id);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                mensaje = "Archivo recibido correctamente.",
                fecha = fechaActual,
                estado = deliverable.Estado,
                penalizacionPct = deliverable.PenalizacionAplicadaPct
            });
        }

        [HttpGet("{id:guid}/archivo")]
        public async Task<IActionResult> ObtenerEnlaceArchivo(Guid id, CancellationToken cancellationToken)
        {
            var deliverable = await _context.Entregables.FindAsync(id);
            if (deliverable is null || string.IsNullOrWhiteSpace(deliverable.ArchivoUrl))
                return NotFound("No hay un archivo asociado a este entregable.");

            var userId = UserAccessService.GetUserId(User);
            if (userId is null) return Unauthorized();
            if (!await _access.CanReadProjectAsync(deliverable.ProyectoId, userId.Value))
                return NotFound();

            if (Uri.TryCreate(deliverable.ArchivoUrl, UriKind.Absolute, out var externalUrl) &&
                externalUrl.Scheme == Uri.UriSchemeHttps &&
                EsOrigenPermitido(externalUrl))
            {
                return Ok(new { url = externalUrl.ToString(), expiresInSeconds = (int?)null });
            }

            try
            {
                var signedUrl = await _storage.CreateSignedUrlAsync(deliverable.ArchivoUrl, cancellationToken);
                return Ok(new { url = signedUrl, expiresInSeconds = 300 });
            }
            catch (InvalidOperationException exception)
            {
                _logger.LogError(exception, "El almacenamiento de entregas no está configurado.");
                return Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (HttpRequestException exception)
            {
                _logger.LogError(exception, "Falló la firma de descarga del entregable {EntregableId}.", id);
                return Problem(statusCode: StatusCodes.Status502BadGateway);
            }
        }

        [HttpGet("{id:guid}/historial")]
        public async Task<IActionResult> ObtenerHistorial(Guid id)
        {
            var deliverable = await _context.Entregables.FindAsync(id);
            if (deliverable is null) return NotFound("Entregable no encontrado.");

            var userId = UserAccessService.GetUserId(User);
            if (userId is null) return Unauthorized();
            if (!await _access.CanReadProjectAsync(deliverable.ProyectoId, userId.Value))
                return NotFound();

            var history = await _context.EntregasVersiones
                .Where(version => version.EntregableId == id)
                .OrderByDescending(version => version.EnviadaAt)
                .ToListAsync();
            return Ok(history);
        }

        private bool EsOrigenPermitido(Uri url)
        {
            var host = url.Host.ToLowerInvariant();
            if (IsAllowedWebLink(url)) return true;

            var storageUrl = _configuration["Supabase:Url"] ??
                             Environment.GetEnvironmentVariable("SUPABASE_URL");
            return Uri.TryCreate(storageUrl, UriKind.Absolute, out var configuredStorage) &&
                   string.Equals(host, configuredStorage.Host, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAllowedWebLink(Uri url)
        {
            var host = url.Host.ToLowerInvariant();
            return host == "github.com" || host.EndsWith(".github.com", StringComparison.Ordinal) ||
                   host == "drive.google.com" || host.EndsWith(".drive.google.com", StringComparison.Ordinal) ||
                   host == "docs.google.com" || host.EndsWith(".docs.google.com", StringComparison.Ordinal);
        }
    }
}