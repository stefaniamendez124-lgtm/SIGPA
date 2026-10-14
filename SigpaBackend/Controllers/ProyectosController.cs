using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SigpaBackend.Data;
using SigpaBackend.Models;
using SigpaBackend.Services;
using System.Data;

namespace SigpaBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ProyectosController : ControllerBase
    {
        private readonly SigpaContext _context;
        private readonly UserAccessService _access;
        private readonly AuditService _audit;

        public ProyectosController(SigpaContext context, UserAccessService access, AuditService audit)
        {
            _context = context;
            _access = access;
            _audit = audit;
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerProyectos()
        {
            var userId = UserAccessService.GetUserId(User);
            if (userId is null) return Unauthorized();

            var esPersonal = await _access.IsStaffAsync(userId.Value);
            var proyectos = await _context.Proyectos
                .Where(project => esPersonal ||
                    project.CreadorId == userId.Value ||
                    _context.ProyectoIntegrantes.Any(member =>
                        member.ProyectoId == project.Id &&
                        member.UsuarioId == userId.Value &&
                        member.Estado == "Aceptado"))
                .OrderByDescending(p => p.FechaInicio ?? DateTime.UtcNow)
                .ToListAsync();

            return Ok(proyectos);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> ObtenerProyecto(Guid id)
        {
            var userId = UserAccessService.GetUserId(User);
            if (userId is null) return Unauthorized();
            if (!await _access.CanReadProjectAsync(id, userId.Value)) return NotFound();

            var project = await _context.Proyectos.FindAsync(id);
            if (project is null) return NotFound();

            project.ObjetivosEspecificos = await _context.ObjetivosEspecificos
                .Where(objective => objective.ProyectoId == id)
                .OrderBy(objective => objective.Orden)
                .Select(objective => objective.Descripcion)
                .ToListAsync();
            return Ok(project);
        }

        [HttpGet("mis-proyectos")]
        public async Task<IActionResult> ObtenerMisProyectos()
        {
            var userId = UserAccessService.GetUserId(User);
            if (userId is null) return Unauthorized();

            var proyectos = await _context.Proyectos
                .Where(project => project.CreadorId == userId.Value ||
                    _context.ProyectoIntegrantes.Any(member =>
                        member.ProyectoId == project.Id &&
                        member.UsuarioId == userId.Value &&
                        member.Estado == "Aceptado"))
                .OrderByDescending(p => p.FechaInicio ?? DateTime.UtcNow)
                .ToListAsync();

            return Ok(proyectos);
        }

        [HttpGet("invitaciones/pendientes")]
        public async Task<IActionResult> ObtenerInvitacionesPendientes()
        {
            var userId = UserAccessService.GetUserId(User);
            if (userId is null) return Unauthorized();

            var invitations = await _context.ProyectoIntegrantes
                .Where(member => member.UsuarioId == userId.Value && member.Estado == "Invitado")
                .Join(_context.Proyectos,
                    member => member.ProyectoId,
                    project => project.Id,
                    (member, project) => new
                    {
                        InvitacionId = member.Id,
                        ProyectoId = project.Id,
                        Titulo = project.Nombre,
                        Asignatura = project.Curso,
                        InvitadaAt = member.FechaRegistro
                    })
                .OrderByDescending(invitation => invitation.InvitadaAt)
                .ToListAsync();
            return Ok(invitations);
        }

        [HttpPost]
        public async Task<IActionResult> CrearProyecto([FromBody] Proyecto proyecto)
        {
            var userId = UserAccessService.GetUserId(User);
            if (userId is null) return Unauthorized();
            if (!await _access.HasRoleAsync(userId.Value, "Estudiante"))
                return Forbid();

            proyecto.Id = Guid.NewGuid();
            proyecto.CreadorId = userId.Value;
            proyecto.Estado = "Pendiente";
            proyecto.Curso = string.IsNullOrWhiteSpace(proyecto.Curso) ? "Ingeniería de requisitos" : proyecto.Curso;
            proyecto.Progreso = 0;
            proyecto.ComentariosEvaluacion = null;
            proyecto.Problema = string.IsNullOrWhiteSpace(proyecto.Problema)
                ? proyecto.Descripcion.Trim()
                : proyecto.Problema.Trim();
            proyecto.ObjetivoGeneral = proyecto.ObjetivoGeneral.Trim();

            if (string.IsNullOrWhiteSpace(proyecto.Nombre) ||
                string.IsNullOrWhiteSpace(proyecto.Problema) ||
                string.IsNullOrWhiteSpace(proyecto.ObjetivoGeneral) ||
                proyecto.ObjetivosEspecificos is null ||
                proyecto.ObjetivosEspecificos.Count == 0 ||
                proyecto.ObjetivosEspecificos.Any(string.IsNullOrWhiteSpace))
            {
                return BadRequest("El título, planteamiento del problema, objetivo general y al menos un objetivo específico son obligatorios.");
            }
            if (proyecto.ObjetivosEspecificos.Count > 20)
                return BadRequest("Se permiten hasta 20 objetivos específicos por propuesta.");
            if (proyecto.ObjetivosEspecificos.Any(objective => objective.Trim().Length > 2000))
                return BadRequest("Cada objetivo específico debe tener como máximo 2000 caracteres.");

            var now = DateTime.UtcNow;
            proyecto.FechaInicio ??= now;
            proyecto.FechaFin ??= now.AddDays(14);
            proyecto.FechaEntrega ??= DateOnly.FromDateTime(proyecto.FechaFin.Value);

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            _context.Proyectos.Add(proyecto);
            await _context.SaveChangesAsync();

            _context.ProyectoIntegrantes.Add(new ProyectoIntegrante
            {
                ProyectoId = proyecto.Id,
                UsuarioId = userId.Value
            });
            _context.ObjetivosEspecificos.AddRange(proyecto.ObjetivosEspecificos.Select((objective, index) =>
                new ObjetivoEspecifico
                {
                    ProyectoId = proyecto.Id,
                    Descripcion = objective.Trim(),
                    Orden = index + 1
                }));
            _audit.Record(userId, "proyecto.creado", "proyecto", proyecto.Id);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return CreatedAtAction(nameof(ObtenerProyectos), new { id = proyecto.Id }, proyecto);
        }

        [HttpGet("{id:guid}/objetivos")]
        public async Task<IActionResult> ObtenerObjetivos(Guid id)
        {
            var userId = UserAccessService.GetUserId(User);
            if (userId is null) return Unauthorized();
            if (!await _access.CanReadProjectAsync(id, userId.Value)) return NotFound();

            var objectives = await _context.ObjetivosEspecificos
                .Where(objective => objective.ProyectoId == id)
                .OrderBy(objective => objective.Orden)
                .ToListAsync();
            return Ok(objectives);
        }

        [HttpPost("{id:guid}/correcciones")]
        public async Task<IActionResult> CorregirPropuesta(Guid id, [FromBody] CorregirPropuestaDto request)
        {
            var userId = UserAccessService.GetUserId(User);
            if (userId is null) return Unauthorized();
            if (!await _access.HasRoleAsync(userId.Value, "Estudiante")) return Forbid();

            var project = await _context.Proyectos.FindAsync(id);
            if (project is null || project.CreadorId != userId.Value) return NotFound();
            if (project.Estado != "Rechazado" ||
                project.FechaCorreccionHasta is null ||
                project.FechaCorreccionHasta <= DateTime.UtcNow)
                return Conflict("La propuesta no está dentro del plazo de corrección de tres días.");
            if (string.IsNullOrWhiteSpace(request.Titulo) ||
                string.IsNullOrWhiteSpace(request.Problema) ||
                string.IsNullOrWhiteSpace(request.ObjetivoGeneral) ||
                request.ObjetivosEspecificos is null ||
                request.ObjetivosEspecificos.Count == 0 ||
                request.ObjetivosEspecificos.Any(string.IsNullOrWhiteSpace) ||
                request.ObjetivosEspecificos.Count > 20)
                return BadRequest("Complete el título, problema, objetivo general y entre 1 y 20 objetivos específicos.");

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            project.Nombre = request.Titulo.Trim();
            project.Descripcion = request.Problema.Trim();
            project.Problema = request.Problema.Trim();
            project.ObjetivoGeneral = request.ObjetivoGeneral.Trim();
            project.Estado = "Pendiente";
            project.ComentariosEvaluacion = null;
            project.FechaCorreccionHasta = null;

            var oldObjectives = await _context.ObjetivosEspecificos
                .Where(objective => objective.ProyectoId == id)
                .ToListAsync();
            _context.ObjetivosEspecificos.RemoveRange(oldObjectives);
            _context.ObjetivosEspecificos.AddRange(request.ObjetivosEspecificos.Select((objective, index) =>
                new ObjetivoEspecifico
                {
                    ProyectoId = id,
                    Descripcion = objective.Trim(),
                    Orden = index + 1
                }));
            _audit.Record(userId, "proyecto.corregido", "proyecto", id);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new { project.Id, project.Estado, project.FechaCorreccionHasta });
        }

        [HttpPost("{id}/integrantes")]
        public async Task<IActionResult> AgregarIntegrante(Guid id, [FromBody] AgregarIntegranteDto request)
        {
            var currentUserId = UserAccessService.GetUserId(User);
            if (currentUserId is null) return Unauthorized();

            var proyecto = await _context.Proyectos.FindAsync(id);
            if (proyecto is null) return NotFound("Proyecto no encontrado.");
            if (proyecto.CreadorId != currentUserId.Value) return Forbid();
            if (request.UsuarioId == Guid.Empty) return BadRequest("El usuario invitado no es válido.");

            var estudianteValido = await _context.Usuarios
                .AnyAsync(user => user.Id == request.UsuarioId && user.Rol == "Estudiante");
            if (!estudianteValido)
            {
                return BadRequest("Solo se pueden invitar cuentas con rol de estudiante.");
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var cantidadIntegrantes = await _context.ProyectoIntegrantes
                .CountAsync(pi => pi.ProyectoId == id &&
                    (pi.Estado == "Aceptado" || pi.Estado == "Invitado"));
            if (!await _context.ProyectoIntegrantes.AnyAsync(pi =>
                    pi.ProyectoId == id && pi.UsuarioId == proyecto.CreadorId && pi.Estado == "Aceptado"))
                cantidadIntegrantes++;

            if (cantidadIntegrantes >= 5)
            {
                return BadRequest("El equipo ya alcanzó el límite máximo de 5 integrantes (RN-02).");
            }

            var yaAsignadoEnCurso = await _context.ProyectoIntegrantes
                .AnyAsync(member => member.UsuarioId == request.UsuarioId &&
                    _context.Proyectos.Any(otherProject =>
                        otherProject.Id == member.ProyectoId &&
                        otherProject.Curso == proyecto.Curso));
            if (yaAsignadoEnCurso)
            {
                return Conflict("El estudiante ya está asignado a un equipo de esta asignatura (RN-02).");
            }

            var nuevoIntegrante = new ProyectoIntegrante
            {
                ProyectoId = id,
                UsuarioId = request.UsuarioId,
                Estado = "Invitado",
                InvitadoPor = currentUserId
            };

            _context.ProyectoIntegrantes.Add(nuevoIntegrante);
            _audit.Record(currentUserId, "equipo.integrante_agregado", "proyecto", id);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Accepted(new { mensaje = "Invitación enviada; el estudiante debe aceptarla.", estado = nuevoIntegrante.Estado });
        }

        [HttpPost("{id}/invitaciones")]
        public async Task<IActionResult> InvitarIntegrante(Guid id, [FromBody] AgregarIntegranteDto request)
        {
            var currentUserId = UserAccessService.GetUserId(User);
            if (currentUserId is null) return Unauthorized();

            var project = await _context.Proyectos.FindAsync(id);
            if (project is null) return NotFound("Proyecto no encontrado.");
            if (project.CreadorId != currentUserId.Value) return Forbid();

            var isStudent = await _context.Usuarios.AnyAsync(user =>
                user.Id == request.UsuarioId && user.Rol == "Estudiante");
            if (!isStudent) return BadRequest("Solo se pueden invitar estudiantes.");

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var memberCount = await _context.ProyectoIntegrantes.CountAsync(member =>
                member.ProyectoId == id && member.Estado != "Rechazado" && member.Estado != "Retirado");
            if (!await _context.ProyectoIntegrantes.AnyAsync(member =>
                    member.ProyectoId == id && member.UsuarioId == project.CreadorId &&
                    member.Estado == "Aceptado"))
                memberCount++;
            if (memberCount >= 5) return Conflict("El equipo ya alcanzó el límite máximo de 5 integrantes (RN-02).");

            var assignedToCourse = await _context.ProyectoIntegrantes.AnyAsync(member =>
                member.UsuarioId == request.UsuarioId &&
                member.Estado != "Rechazado" &&
                member.Estado != "Retirado" &&
                _context.Proyectos.Any(otherProject =>
                    otherProject.Id == member.ProyectoId && otherProject.Curso == project.Curso));
            if (assignedToCourse)
                return Conflict("El estudiante ya está asignado o tiene una invitación activa en esta asignatura (RN-02).");

            var invitation = new ProyectoIntegrante
            {
                ProyectoId = id,
                UsuarioId = request.UsuarioId,
                Estado = "Invitado",
                InvitadoPor = currentUserId.Value
            };
            _context.ProyectoIntegrantes.Add(invitation);
            _audit.Record(currentUserId, "equipo.invitacion_enviada", "proyecto", id);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Accepted(new { invitacionId = invitation.Id, estado = invitation.Estado });
        }

        [HttpPost("{id}/invitaciones/aceptar")]
        public async Task<IActionResult> AceptarInvitacion(Guid id)
        {
            var currentUserId = UserAccessService.GetUserId(User);
            if (currentUserId is null) return Unauthorized();

            var project = await _context.Proyectos.FindAsync(id);
            if (project is null) return NotFound("Proyecto no encontrado.");
            if (!await _access.HasRoleAsync(currentUserId.Value, "Estudiante")) return Forbid();

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var invitation = await _context.ProyectoIntegrantes.FirstOrDefaultAsync(member =>
                member.ProyectoId == id &&
                member.UsuarioId == currentUserId.Value &&
                member.Estado == "Invitado");
            if (invitation is null) return NotFound("No existe una invitación pendiente para este proyecto.");

            var assignedToOtherProject = await _context.ProyectoIntegrantes.AnyAsync(member =>
                member.UsuarioId == currentUserId.Value &&
                member.ProyectoId != id &&
                (member.Estado == "Invitado" || member.Estado == "Aceptado") &&
                _context.Proyectos.Any(otherProject =>
                    otherProject.Id == member.ProyectoId && otherProject.Curso == project.Curso));
            if (assignedToOtherProject)
                return Conflict("Ya perteneces a otro equipo de esta asignatura (RN-02).");

            var memberCount = await _context.ProyectoIntegrantes.CountAsync(member =>
                member.ProyectoId == id &&
                (member.Estado == "Invitado" || member.Estado == "Aceptado"));
            if (!await _context.ProyectoIntegrantes.AnyAsync(member =>
                    member.ProyectoId == id && member.UsuarioId == project.CreadorId &&
                    member.Estado == "Aceptado"))
                memberCount++;
            if (memberCount > 5)
                return Conflict("El equipo ya alcanzó el límite máximo de 5 integrantes (RN-02).");

            invitation.Estado = "Aceptado";
            invitation.RespondidoAt = DateTime.UtcNow;
            _audit.Record(currentUserId, "equipo.invitacion_aceptada", "proyecto", id);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new { estado = invitation.Estado });
        }

        [HttpPost("{id:guid}/invitaciones/rechazar")]
        public async Task<IActionResult> RechazarInvitacion(Guid id)
        {
            var currentUserId = UserAccessService.GetUserId(User);
            if (currentUserId is null) return Unauthorized();

            var invitation = await _context.ProyectoIntegrantes.FirstOrDefaultAsync(member =>
                member.ProyectoId == id &&
                member.UsuarioId == currentUserId.Value &&
                member.Estado == "Invitado");
            if (invitation is null) return NotFound("No existe una invitación pendiente para este proyecto.");

            invitation.Estado = "Rechazado";
            invitation.RespondidoAt = DateTime.UtcNow;
            _audit.Record(currentUserId, "equipo.invitacion_rechazada", "proyecto", id);
            await _context.SaveChangesAsync();
            return Ok(new { estado = invitation.Estado });
        }

        [HttpPatch("{id}/evaluar")]
        public async Task<IActionResult> EvaluarProyecto(Guid id, [FromBody] EvaluarProyectoDto request)
        {
            var userId = UserAccessService.GetUserId(User);
            if (userId is null) return Unauthorized();

            var proyecto = await _context.Proyectos.FindAsync(id);
            if (proyecto == null) return NotFound("Proyecto no encontrado.");
            if (!await _access.CanReviewProjectAsync(id, userId.Value)) return Forbid();

            var estado = request.Estado.Trim();
            if (!string.Equals(estado, "Aprobado", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(estado, "Rechazado", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("Estado de evaluación no válido. Solo se permite 'Aprobado' o 'Rechazado'.");
            }

            if (proyecto.Estado is not ("Pendiente" or "En revisión"))
            {
                return Conflict("La propuesta ya fue dictaminada.");
            }

            if (string.Equals(estado, "Rechazado", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(request.Comentarios))
            {
                return BadRequest("Debe indicar observaciones para rechazar la propuesta.");
            }

            proyecto.Estado = string.Equals(estado, "Aprobado", StringComparison.OrdinalIgnoreCase)
                ? "Aprobado"
                : "Rechazado";
            proyecto.ComentariosEvaluacion = request.Comentarios;
            proyecto.FechaCorreccionHasta = proyecto.Estado == "Rechazado"
                ? DateTime.UtcNow.AddDays(3)
                : null;

            _audit.Record(userId, "proyecto.dictaminado", "proyecto", id);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                mensaje = $"El proyecto ha sido {proyecto.Estado.ToLowerInvariant()}.",
                estadoActual = proyecto.Estado,
                fechaCorreccionHasta = proyecto.FechaCorreccionHasta
            });
        }
    }

    public class AgregarIntegranteDto
    {
        public Guid UsuarioId { get; set; }
    }

    public class EvaluarProyectoDto
    {
        public string Estado { get; set; } = string.Empty;
        public string? Comentarios { get; set; }
    }

    public sealed class CorregirPropuestaDto
    {
        public string Titulo { get; set; } = string.Empty;
        public string Problema { get; set; } = string.Empty;
        public string ObjetivoGeneral { get; set; } = string.Empty;
        public List<string> ObjetivosEspecificos { get; set; } = [];
    }
}