using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SigpaBackend.Data;
using SigpaBackend.Models;
using SigpaBackend.Services;
using System.Data;
using Npgsql;

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
        public async Task<IActionResult> ObtenerProyectos(CancellationToken cancellationToken)
        {
            var userId = UserAccessService.GetUserId(User);
            if (userId is null) return Unauthorized();

            var esPersonal = await _access.IsStaffAsync(userId.Value);
            var integrantesDisponibles = await _context.Database
                .SqlQueryRaw<bool>(
                    "SELECT to_regclass('public.proyecto_integrantes') IS NOT NULL AS \"Value\"")
                .SingleAsync(cancellationToken);
            const string selectProjectsSql = """
                SELECT
                    project.id AS id,
                    project.nombre AS nombre,
                    project.descripcion AS descripcion,
                    COALESCE(to_jsonb(project)->>'problema', project.descripcion) AS problema,
                    COALESCE(to_jsonb(project)->>'objetivo_general', '') AS objetivo_general,
                    COALESCE(to_jsonb(project)->>'curso', 'Ingeniería de requisitos') AS curso,
                    project.estado AS estado,
                    COALESCE(NULLIF(to_jsonb(project)->>'progreso', '')::integer, 0) AS progreso,
                    NULLIF(to_jsonb(project)->>'fecha_inicio', '')::timestamptz AS fecha_inicio,
                    NULLIF(to_jsonb(project)->>'fecha_fin', '')::timestamptz AS fecha_fin,
                    NULLIF(to_jsonb(project)->>'fecha_entrega', '')::date AS fecha_entrega,
                    project.creador_id AS creador_id,
                    to_jsonb(project)->>'comentarios_evaluacion' AS comentarios_evaluacion
                FROM public.proyectos AS project
                WHERE @esPersonal
                   OR project.creador_id = @userId
                """;
            const string memberFilterSql = """
                   OR EXISTS (
                        SELECT 1
                        FROM public.proyecto_integrantes AS member
                        WHERE member.proyecto_id = project.id
                          AND member.usuario_id = @userId
                          AND member.estado = 'Aceptado'
                   )
                """;
            const string orderProjectsSql = """
                ORDER BY to_jsonb(project)->>'fecha_inicio' DESC NULLS LAST
                """;
            var query = selectProjectsSql +
                        (integrantesDisponibles ? memberFilterSql : string.Empty) +
                        orderProjectsSql;
            var proyectos = await _context.Database
                .SqlQueryRaw<ProyectoListadoDto>(
                    query,
                    new NpgsqlParameter("esPersonal", esPersonal),
                    new NpgsqlParameter("userId", userId.Value))
                .ToListAsync(cancellationToken);

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
        [HttpPost("/api/projects")]
        public async Task<IActionResult> CrearProyecto([FromBody] CrearProyectoDto request)
        {
            var userId = UserAccessService.GetUserId(User);
            if (userId is null) return Unauthorized();
            if (!await _access.HasRoleAsync(userId.Value, "Estudiante"))
                return Forbid();

            if (string.IsNullOrWhiteSpace(request.Nombre) ||
                string.IsNullOrWhiteSpace(request.Problema) ||
                string.IsNullOrWhiteSpace(request.ObjetivoGeneral) ||
                request.ObjetivosEspecificos is null ||
                request.ObjetivosEspecificos.Count == 0 ||
                request.ObjetivosEspecificos.Any(string.IsNullOrWhiteSpace))
            {
                return BadRequest("El título, planteamiento del problema, objetivo general y al menos un objetivo específico son obligatorios.");
            }
            if (request.ObjetivosEspecificos.Count > 20)
                return BadRequest("Se permiten hasta 20 objetivos específicos por propuesta.");
            if (request.ObjetivosEspecificos.Any(objective => objective.Trim().Length > 2000))
                return BadRequest("Cada objetivo específico debe tener como máximo 2000 caracteres.");
            if (request.CursoId == Guid.Empty)
                return BadRequest("Selecciona una asignatura con matrícula activa.");
            if (request.IntegrantesIds is null || request.IntegrantesIds.Count is < 1 or > 5 ||
                request.IntegrantesIds.Any(id => id == Guid.Empty) ||
                request.IntegrantesIds.Distinct().Count() != request.IntegrantesIds.Count ||
                request.IntegrantesIds.Contains(userId.Value))
            {
                return BadRequest("Invita entre 1 y 5 compañeros; el equipo puede tener hasta 6 integrantes contando al creador (RN-02).");
            }

            var course = await _context.Cursos
                .FirstOrDefaultAsync(item => item.Id == request.CursoId && item.Activo);
            if (course is null) return BadRequest("La asignatura seleccionada no está activa.");
            var courseTeacherIds = await _context.DocentesCursos
                .Where(assignment => assignment.CursoId == course.Id)
                .Select(assignment => assignment.UsuarioId)
                .ToListAsync();
            if (course.DocenteId.HasValue)
                courseTeacherIds.Add(course.DocenteId.Value);
            var validTeacherIds = await _context.Usuarios
                .Where(user => courseTeacherIds.Contains(user.Id) &&
                               user.Rol == "Profesor" &&
                               user.Activo)
                .Select(user => user.Id)
                .Distinct()
                .ToListAsync();
            if (validTeacherIds.Count == 0)
                return Conflict("La asignatura no tiene un docente responsable configurado para revisar proyectos.");

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var memberIds = request.IntegrantesIds.Append(userId.Value).ToArray();
            var enrolledIds = await _context.Matriculas
                .Where(enrollment => enrollment.CursoId == course.Id &&
                                     enrollment.Estado == "Activa" &&
                                     memberIds.Contains(enrollment.EstudianteId))
                .Join(_context.Usuarios.Where(student => student.Rol == "Estudiante"),
                    enrollment => enrollment.EstudianteId,
                    student => student.Id,
                    (enrollment, student) => enrollment.EstudianteId)
                .ToListAsync();
            if (enrolledIds.Count != memberIds.Length)
                return Conflict("El estudiante invitado no tiene matrícula activa en la asignatura seleccionada (RN-02).");

            var assignedIds = await _context.ProyectoIntegrantes
                .Where(member => memberIds.Contains(member.UsuarioId) &&
                                 (member.Estado == "Invitado" || member.Estado == "Aceptado"))
                .Select(member => member.UsuarioId)
                .Distinct()
                .ToListAsync();
            if (assignedIds.Count > 0)
            {
                var conflictingStudent = await _context.Usuarios
                    .Where(student => assignedIds.Contains(student.Id))
                    .Select(student => student.NombreCompleto)
                    .FirstOrDefaultAsync();
                return Conflict($"El estudiante {conflictingStudent ?? assignedIds[0].ToString()} pertenece a otro grupo o curso (RN-02).");
            }

            var now = DateTime.UtcNow;
            var project = new Proyecto
            {
                Id = Guid.NewGuid(),
                Nombre = request.Nombre.Trim(),
                Descripcion = request.Problema.Trim(),
                Problema = request.Problema.Trim(),
                ObjetivoGeneral = request.ObjetivoGeneral.Trim(),
                Curso = course.Nombre,
                CursoId = course.Id,
                Estado = "Pendiente de Revisión",
                Progreso = 0,
                CreadorId = userId.Value,
                FechaInicio = now,
                FechaFin = now.AddDays(14),
                FechaEntrega = DateOnly.FromDateTime(now.AddDays(14))
            };

            _context.Proyectos.Add(project);
            await _context.SaveChangesAsync();

            _context.ProyectoIntegrantes.Add(new ProyectoIntegrante
            {
                ProyectoId = project.Id,
                UsuarioId = userId.Value
            });
            _context.ProyectoIntegrantes.AddRange(request.IntegrantesIds.Select(studentId =>
                new ProyectoIntegrante
                {
                    ProyectoId = project.Id,
                    UsuarioId = studentId,
                    Estado = "Invitado",
                    InvitadoPor = userId.Value
                }));
            _context.ObjetivosEspecificos.AddRange(request.ObjetivosEspecificos.Select((objective, index) =>
                new ObjetivoEspecifico
                {
                    ProyectoId = project.Id,
                    Descripcion = objective.Trim(),
                    Orden = index + 1
                }));
            _audit.Record(userId, "proyecto.creado", "proyecto", project.Id);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            project.ObjetivosEspecificos = request.ObjetivosEspecificos.Select(objective => objective.Trim()).ToList();
            return CreatedAtAction(nameof(ObtenerProyecto), new { id = project.Id }, project);
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

        [HttpGet("/api/projects/{id:guid}/members")]
        public async Task<IActionResult> ObtenerIntegrantesActivos(Guid id)
        {
            var userId = UserAccessService.GetUserId(User);
            if (userId is null) return Unauthorized();
            if (!await _access.CanReadProjectAsync(id, userId.Value)) return NotFound();

            var members = await _context.ProyectoIntegrantes
                .Where(member => member.ProyectoId == id && member.Estado == "Aceptado")
                .Join(_context.Usuarios,
                    member => member.UsuarioId,
                    student => student.Id,
                    (member, student) => new
                    {
                        id = student.Id,
                        nombre = student.NombreCompleto,
                        correo = student.CorreoInstitucional
                    })
                .ToListAsync();

            var creatorId = await _context.Proyectos
                .Where(project => project.Id == id)
                .Select(project => project.CreadorId)
                .FirstAsync();
            if (!members.Any(member => member.id == creatorId))
            {
                var creator = await _context.Usuarios
                    .Where(student => student.Id == creatorId)
                    .Select(student => new { id = student.Id, nombre = student.NombreCompleto, correo = student.CorreoInstitucional })
                    .FirstOrDefaultAsync();
                if (creator is not null) members.Add(creator);
            }

            return Ok(members);
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
            if (proyecto.CursoId is Guid courseId &&
                !await _context.Matriculas.AnyAsync(enrollment =>
                    enrollment.CursoId == courseId &&
                    enrollment.EstudianteId == request.UsuarioId &&
                    enrollment.Estado == "Activa"))
                return Conflict("El estudiante no tiene matrícula activa en esta asignatura (RN-02).");

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

            if (cantidadIntegrantes >= 6)
            {
                return BadRequest("El equipo ya alcanzó el límite máximo de 6 integrantes (RN-02).");
            }

            var yaAsignado = await _context.ProyectoIntegrantes
                .AnyAsync(member => member.UsuarioId == request.UsuarioId &&
                                    (member.Estado == "Invitado" || member.Estado == "Aceptado"));
            if (yaAsignado)
            {
                return Conflict("El estudiante pertenece a otro grupo o curso (RN-02).");
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
            if (project.CursoId is Guid courseId &&
                !await _context.Matriculas.AnyAsync(enrollment =>
                    enrollment.CursoId == courseId &&
                    enrollment.EstudianteId == request.UsuarioId &&
                    enrollment.Estado == "Activa"))
                return Conflict("El estudiante no tiene matrícula activa en esta asignatura (RN-02).");

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
            if (memberCount >= 6) return Conflict("El equipo ya alcanzó el límite máximo de 6 integrantes (RN-02).");

            var assignedToCourse = await _context.ProyectoIntegrantes.AnyAsync(member =>
                member.UsuarioId == request.UsuarioId &&
                (member.Estado == "Invitado" || member.Estado == "Aceptado"));
            if (assignedToCourse)
                return Conflict("El estudiante pertenece a otro grupo o curso (RN-02).");

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
            if (project.CursoId is Guid courseId &&
                !await _context.Matriculas.AnyAsync(enrollment =>
                    enrollment.CursoId == courseId &&
                    enrollment.EstudianteId == currentUserId.Value &&
                    enrollment.Estado == "Activa"))
                return Conflict("Tu matrícula activa en esta asignatura ya no está vigente (RN-02).");

            var assignedToOtherProject = await _context.ProyectoIntegrantes.AnyAsync(member =>
                member.UsuarioId == currentUserId.Value &&
                member.ProyectoId != id &&
                (member.Estado == "Invitado" || member.Estado == "Aceptado") &&
                _context.Proyectos.Any(otherProject => otherProject.Id == member.ProyectoId));
            if (assignedToOtherProject)
                return Conflict("Ya perteneces a otro grupo o curso (RN-02).");

            var memberCount = await _context.ProyectoIntegrantes.CountAsync(member =>
                member.ProyectoId == id &&
                (member.Estado == "Invitado" || member.Estado == "Aceptado"));
            if (!await _context.ProyectoIntegrantes.AnyAsync(member =>
                    member.ProyectoId == id && member.UsuarioId == project.CreadorId &&
                    member.Estado == "Aceptado"))
                memberCount++;
            if (memberCount > 6)
                return Conflict("El equipo ya alcanzó el límite máximo de 6 integrantes (RN-02).");

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

            if (proyecto.Estado is not ("Pendiente" or "Pendiente de Revisión" or "En revisión"))
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