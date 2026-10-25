using System.Security.Cryptography;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using SigpaBackend.Data;
using SigpaBackend.Models;
using SigpaBackend.Repositories;

namespace SigpaBackend.Services;

public sealed class UsuarioService : IUsuarioService
{
    private readonly SigpaContext _context;
    private readonly IUsuarioRepository _repository;
    private readonly ISupabaseAdminUserService _supabaseAdmin;
    private readonly IWelcomeEmailService _welcomeEmail;
    private readonly AuditService _audit;
    private readonly ILogger<UsuarioService> _logger;

    public UsuarioService(
        SigpaContext context,
        IUsuarioRepository repository,
        ISupabaseAdminUserService supabaseAdmin,
        IWelcomeEmailService welcomeEmail,
        AuditService audit,
        ILogger<UsuarioService> logger)
    {
        _context = context;
        _repository = repository;
        _supabaseAdmin = supabaseAdmin;
        _welcomeEmail = welcomeEmail;
        _audit = audit;
        _logger = logger;
    }

    public async Task<PaginaUsuariosDto> ListarAsync(
        int pagina,
        int tamanoPagina,
        string? rol,
        string? programa,
        Guid? cursoId,
        bool? activo,
        string? busqueda,
        CancellationToken cancellationToken)
    {
        var query = _repository.Usuarios
            .Where(user => user.Rol == "Estudiante" || user.Rol == "Profesor");

        if (!string.IsNullOrWhiteSpace(rol) && rol != "Todos")
            query = query.Where(user => user.Rol == rol);
        if (!string.IsNullOrWhiteSpace(programa))
            query = query.Where(user => user.ProgramaAcademico == programa);
        if (activo.HasValue)
            query = query.Where(user => user.Activo == activo.Value);
        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            var term = busqueda.Trim().ToLowerInvariant();
            query = query.Where(user =>
                user.NombreCompleto.ToLower().Contains(term) ||
                user.CorreoInstitucional.ToLower().Contains(term) ||
                (user.CodigoIdentificacion != null && user.CodigoIdentificacion.ToLower().Contains(term)) ||
                (user.NumeroIdentificacion != null && user.NumeroIdentificacion.ToLower().Contains(term)));
        }
        if (cursoId.HasValue)
        {
            var id = cursoId.Value;
            query = query.Where(user =>
                (user.Rol == "Estudiante" && _context.Matriculas.Any(enrollment =>
                    enrollment.EstudianteId == user.Id &&
                    enrollment.CursoId == id &&
                    enrollment.Estado == "Activa")) ||
                (user.Rol == "Profesor" &&
                 (_context.DocentesCursos.Any(assignment =>
                      assignment.UsuarioId == user.Id && assignment.CursoId == id) ||
                  _context.Cursos.Any(course => course.Id == id && course.DocenteId == user.Id))));
        }

        var total = await query.CountAsync(cancellationToken);
        var users = await query
            .OrderBy(user => user.NombreCompleto)
            .Skip((pagina - 1) * tamanoPagina)
            .Take(tamanoPagina)
            .ToListAsync(cancellationToken);
        var items = new List<UsuarioResponseDto>(users.Count);
        foreach (var user in users)
            items.Add(await ToResponseAsync(user, cancellationToken));

        return new PaginaUsuariosDto
        {
            Items = items,
            Pagina = pagina,
            TamanoPagina = tamanoPagina,
            Total = total
        };
    }

    public async Task<IReadOnlyList<CursoAdministracionDto>> ObtenerCursosAsync(CancellationToken cancellationToken)
    {
        return await _repository.Cursos
            .Where(course => course.Activo)
            .OrderBy(course => course.ProgramaAcademico)
            .ThenBy(course => course.Nombre)
            .Select(course => new CursoAdministracionDto
            {
                Id = course.Id,
                Codigo = course.Codigo,
                Nombre = course.Nombre,
                Semestre = course.Semestre,
                ProgramaAcademico = course.ProgramaAcademico,
                PeriodoAcademico = course.PeriodoAcademico,
                Activo = course.Activo
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<UsuarioCreadoResultadoDto> CrearAsync(
        CrearUsuarioDto request,
        Guid coordinadorId,
        CancellationToken cancellationToken,
        bool generarContrasenaSiFalta = false)
    {
        Normalize(request);
        await ValidateNewAsync(request, cancellationToken);

        if (string.IsNullOrWhiteSpace(request.Contrasena) && !generarContrasenaSiFalta)
        {
            throw new UsuarioManagementException(
                StatusCodes.Status400BadRequest,
                "El coordinador debe definir una contraseña inicial de al menos 12 caracteres con mayúscula, minúscula, número y símbolo.");
        }

        var initialPassword = string.IsNullOrWhiteSpace(request.Contrasena)
            ? GenerateTemporaryPassword()
            : request.Contrasena;
        var userId = await _supabaseAdmin.CreateAsync(
            request.CorreoInstitucional,
            initialPassword,
            cancellationToken);
        var user = CreateEntity(userId, request);

        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            var profile = await _context.Usuarios.FirstOrDefaultAsync(
                current => current.Id == userId, cancellationToken);
            if (profile is null)
            {
                _context.Usuarios.Add(user);
            }
            else
            {
                profile.NombreCompleto = user.NombreCompleto;
                profile.CorreoInstitucional = user.CorreoInstitucional;
                profile.Rol = user.Rol;
                profile.TipoDocumento = user.TipoDocumento;
                profile.NumeroIdentificacion = user.NumeroIdentificacion;
                profile.CodigoIdentificacion = user.CodigoIdentificacion;
                profile.Semestre = user.Semestre;
                profile.ProgramaAcademico = user.ProgramaAcademico;
                profile.DepartamentoAcademico = user.DepartamentoAcademico;
                profile.Activo = user.Activo;
                profile.DebeCambiarContrasena = user.DebeCambiarContrasena;
                user = profile;
            }
            await AddCourseAssignmentsAsync(user, request.CursoIds, cancellationToken);
            _audit.Record(coordinadorId, "usuario.creado", "usuario", user.Id);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            await RollbackIdentityCreateAsync(userId, exception, cancellationToken);
            throw new UsuarioManagementException(
                StatusCodes.Status409Conflict,
                "El correo, documento o código ya está registrado.");
        }
        catch (Exception exception)
        {
            await RollbackIdentityCreateAsync(userId, exception, cancellationToken);
            throw;
        }

        var emailSent = false;
        string? warning = null;
        if (request.EnviarCorreoBienvenida)
        {
            try
            {
                await _welcomeEmail.SendTemporaryPasswordAsync(
                    user.CorreoInstitucional,
                    user.NombreCompleto,
                    initialPassword,
                    cancellationToken);
                emailSent = true;
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or System.Net.Mail.SmtpException or
                OperationCanceledException)
            {
                _logger.LogError(exception, "La cuenta {UserId} se creó, pero falló el correo de bienvenida.", user.Id);
                warning = "La cuenta se creó, pero no se pudo enviar el correo de bienvenida. Configure SMTP y reenvíe el acceso.";
            }
        }

        return new UsuarioCreadoResultadoDto
        {
            Usuario = await ToResponseAsync(user, cancellationToken),
            CorreoBienvenidaEnviado = emailSent,
            Advertencia = warning
        };
    }

    public async Task<UsuarioResponseDto?> ActualizarAsync(
        Guid id,
        ActualizarUsuarioDto request,
        Guid coordinadorId,
        CancellationToken cancellationToken)
    {
        var user = await _repository.ObtenerAsync(id, cancellationToken);
        if (user is null || (user.Rol != "Estudiante" && user.Rol != "Profesor"))
            return null;

        var email = request.CorreoInstitucional.Trim().ToLowerInvariant();
        var collision = await _repository.Usuarios.AnyAsync(
            candidate => candidate.Id != id &&
                         (candidate.CorreoInstitucional.ToLower() == email ||
                          candidate.NumeroIdentificacion == request.NumeroIdentificacion ||
                          (request.CodigoIdentificacion != null &&
                           candidate.CodigoIdentificacion == request.CodigoIdentificacion)),
            cancellationToken);
        if (collision)
            throw new UsuarioManagementException(StatusCodes.Status409Conflict, "El correo, documento o código ya está registrado.");
        ValidateProfileFields(user.Rol, request.CodigoIdentificacion, request.Semestre,
            request.ProgramaAcademico, request.DepartamentoAcademico);

        var oldEmail = user.CorreoInstitucional;
        var previousActive = user.Activo;
        var previousName = user.NombreCompleto;
        var previousDocumentType = user.TipoDocumento;
        var previousDocumentNumber = user.NumeroIdentificacion;
        var previousCode = user.CodigoIdentificacion;
        var previousSemester = user.Semestre;
        var previousProgram = user.ProgramaAcademico;
        var previousDepartment = user.DepartamentoAcademico;
        try
        {
            await _supabaseAdmin.UpdateAsync(id, email == oldEmail ? null : email,
                request.Activo, user.DebeCambiarContrasena, cancellationToken);
            user.NombreCompleto = request.NombreCompleto.Trim();
            user.CorreoInstitucional = email;
            user.TipoDocumento = request.TipoDocumento;
            user.NumeroIdentificacion = request.NumeroIdentificacion.Trim();
            user.CodigoIdentificacion = Clean(request.CodigoIdentificacion);
            user.Semestre = user.Rol == "Estudiante" ? request.Semestre : null;
            user.ProgramaAcademico = user.Rol == "Estudiante" ? Clean(request.ProgramaAcademico) : null;
            user.DepartamentoAcademico = user.Rol == "Profesor" ? Clean(request.DepartamentoAcademico) : null;
            user.Activo = request.Activo;
            _audit.Record(coordinadorId, request.Activo == previousActive ? "usuario.actualizado" :
                request.Activo ? "usuario.activado" : "usuario.deshabilitado", "usuario", id);
            await _repository.GuardarAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            user.NombreCompleto = previousName;
            user.CorreoInstitucional = oldEmail;
            user.TipoDocumento = previousDocumentType;
            user.NumeroIdentificacion = previousDocumentNumber;
            user.CodigoIdentificacion = previousCode;
            user.Semestre = previousSemester;
            user.ProgramaAcademico = previousProgram;
            user.DepartamentoAcademico = previousDepartment;
            user.Activo = previousActive;
            try
            {
                await _supabaseAdmin.UpdateAsync(id, email == oldEmail ? null : oldEmail,
                    previousActive, user.DebeCambiarContrasena, cancellationToken);
            }
            catch (Exception rollbackException) when (
                rollbackException is HttpRequestException or InvalidOperationException)
            {
                _logger.LogCritical(rollbackException,
                    "No se pudo compensar Supabase Auth después del fallo al actualizar el usuario {UserId}.", id);
            }
            _logger.LogWarning(exception, "No se pudo persistir la actualización del usuario {UserId}.", id);
            throw new UsuarioManagementException(StatusCodes.Status409Conflict, "El correo, documento o código ya está registrado.");
        }

        return await ToResponseAsync(user, cancellationToken);
    }

    public async Task<UsuarioResponseDto?> AsignarCursosAsync(
        Guid id,
        AsignarCursosDto request,
        Guid coordinadorId,
        CancellationToken cancellationToken)
    {
        var user = await _repository.ObtenerAsync(id, cancellationToken);
        if (user is null || (user.Rol != "Estudiante" && user.Rol != "Profesor"))
            return null;
        if (request.CursoIds.Count is < 1 or > 50 ||
            request.CursoIds.Any(courseId => courseId == Guid.Empty) ||
            request.CursoIds.Distinct().Count() != request.CursoIds.Count)
        {
            throw new UsuarioManagementException(StatusCodes.Status400BadRequest, "Selecciona entre 1 y 50 cursos activos sin duplicados.");
        }

        var courses = await _context.Cursos
            .Where(course => request.CursoIds.Contains(course.Id) && course.Activo)
            .ToListAsync(cancellationToken);
        if (courses.Count != request.CursoIds.Count)
            throw new UsuarioManagementException(StatusCodes.Status400BadRequest, "Una o más asignaturas no existen o están inactivas.");

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        if (user.Rol == "Estudiante")
        {
            var existing = await _context.Matriculas
                .Where(enrollment => enrollment.EstudianteId == id)
                .ToListAsync(cancellationToken);
            var toRemove = existing.Where(enrollment =>
                enrollment.Estado == "Activa" && !request.CursoIds.Contains(enrollment.CursoId)).ToList();
            foreach (var enrollment in toRemove)
            {
                var hasProject = await _context.ProyectoIntegrantes.AnyAsync(member =>
                    member.UsuarioId == id &&
                    (member.Estado == "Invitado" || member.Estado == "Aceptado") &&
                    _context.Proyectos.Any(project =>
                        project.Id == member.ProyectoId && project.CursoId == enrollment.CursoId),
                    cancellationToken);
                if (hasProject)
                    throw new UsuarioManagementException(
                        StatusCodes.Status409Conflict,
                        "No se puede retirar una matrícula mientras el estudiante pertenezca a un equipo activo en esa asignatura.");
                enrollment.Estado = "Finalizada";
            }
            foreach (var courseId in request.CursoIds)
            {
                var enrollment = existing.FirstOrDefault(item => item.CursoId == courseId);
                if (enrollment is null)
                {
                    _context.Matriculas.Add(new Matricula { EstudianteId = id, CursoId = courseId });
                }
                else
                {
                    enrollment.Estado = "Activa";
                }
            }
        }
        else
        {
            var existing = await _context.DocentesCursos
                .Where(assignment => assignment.UsuarioId == id)
                .ToListAsync(cancellationToken);
            var remove = existing.Where(assignment => !request.CursoIds.Contains(assignment.CursoId)).ToList();
            foreach (var assignment in remove)
            {
                var assignedCourse = await _context.Cursos.FirstAsync(
                    course => course.Id == assignment.CursoId, cancellationToken);
                if (assignedCourse.DocenteId == id)
                {
                    var replacement = await _context.DocentesCursos
                        .Where(candidate => candidate.CursoId == assignment.CursoId &&
                                            candidate.UsuarioId != id &&
                                            _context.Usuarios.Any(teacher =>
                                                teacher.Id == candidate.UsuarioId &&
                                                teacher.Rol == "Profesor" &&
                                                teacher.Activo))
                        .OrderBy(candidate => candidate.AsignadoAt)
                        .FirstOrDefaultAsync(cancellationToken);
                    if (replacement is null)
                    {
                        assignedCourse.DocenteId = null;
                    }
                    else
                    {
                        assignedCourse.DocenteId = replacement.UsuarioId;
                        replacement.RolDocente = "Titular";
                    }
                }
                _context.DocentesCursos.Remove(assignment);
            }
            foreach (var course in courses)
            {
                if (!existing.Any(assignment => assignment.CursoId == course.Id))
                {
                    var isTitular = course.DocenteId is null;
                    if (isTitular)
                    {
                        var previousAssignments = await _context.DocentesCursos
                            .Where(assignment => assignment.CursoId == course.Id)
                            .ToListAsync(cancellationToken);
                        foreach (var previousAssignment in previousAssignments)
                            previousAssignment.RolDocente = "Apoyo";
                    }
                    _context.DocentesCursos.Add(new DocenteCurso
                    {
                        UsuarioId = id,
                        CursoId = course.Id,
                        RolDocente = isTitular ? "Titular" : "Apoyo"
                    });
                    course.DocenteId ??= id;
                }
            }
        }

        _audit.Record(coordinadorId, "usuario.cursos_asignados", "usuario", id);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ToResponseAsync(user, cancellationToken);
    }

    public async Task<ResultadoCargaUsuariosDto> ImportarAsync(
        IReadOnlyList<CargaUsuarioFilaDto> filas,
        Guid coordinadorId,
        CancellationToken cancellationToken)
    {
        if (filas.Count is 0 or > 500)
            throw new UsuarioManagementException(StatusCodes.Status400BadRequest, "La carga debe contener entre 1 y 500 filas.");

        var errors = new List<ErrorCargaUsuarioDto>();
        var pendingEmails = new List<ErrorCargaUsuarioDto>();
        var created = 0;
        foreach (var row in filas.OrderBy(item => item.Fila))
        {
            if (row.Errores.Count > 0)
            {
                errors.Add(new ErrorCargaUsuarioDto
                {
                    Fila = row.Fila,
                    Correo = row.Usuario.CorreoInstitucional,
                    Error = string.Join(" ", row.Errores)
                });
                continue;
            }

            try
            {
                var result = await CrearAsync(
                    row.Usuario,
                    coordinadorId,
                    cancellationToken,
                    generarContrasenaSiFalta: true);
                created++;
                if (row.Usuario.EnviarCorreoBienvenida && !result.CorreoBienvenidaEnviado)
                {
                    pendingEmails.Add(new ErrorCargaUsuarioDto
                    {
                        Fila = row.Fila,
                        Correo = row.Usuario.CorreoInstitucional,
                        Error = result.Advertencia ?? "El correo de bienvenida está pendiente."
                    });
                }
            }
            catch (UsuarioManagementException exception)
            {
                errors.Add(new ErrorCargaUsuarioDto
                {
                    Fila = row.Fila,
                    Correo = row.Usuario.CorreoInstitucional,
                    Error = exception.Message
                });
            }
            catch (HttpRequestException exception)
            {
                _logger.LogError(exception, "Falló la creación externa del usuario de la fila {Row}.", row.Fila);
                errors.Add(new ErrorCargaUsuarioDto
                {
                    Fila = row.Fila,
                    Correo = row.Usuario.CorreoInstitucional,
                    Error = "El proveedor de identidad no pudo crear esta cuenta."
                });
            }
        }

        return new ResultadoCargaUsuariosDto
        {
            Creados = created,
            Fallidos = errors.Count,
            Errores = errors,
            CorreosPendientes = pendingEmails
        };
    }

    public async Task<IReadOnlyList<CargaUsuarioFilaDto>> ValidarCargaAsync(
        IReadOnlyList<CargaUsuarioFilaDto> filas,
        CancellationToken cancellationToken)
    {
        if (filas.Count is 0 or > 500)
            throw new UsuarioManagementException(StatusCodes.Status400BadRequest, "La carga debe contener entre 1 y 500 filas.");

        var output = new List<CargaUsuarioFilaDto>(filas.Count);
        var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var documentNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in filas.OrderBy(item => item.Fila))
        {
            Normalize(row.Usuario);
            var errors = new List<string>(row.Errores);
            var validationResults = new List<ValidationResult>();
            if (!Validator.TryValidateObject(
                    row.Usuario,
                    new ValidationContext(row.Usuario),
                    validationResults,
                    validateAllProperties: true))
            {
                errors.AddRange(validationResults.Select(result => result.ErrorMessage ?? "Campo inválido."));
            }

            var email = row.Usuario.CorreoInstitucional;
            var identifier = row.Usuario.NumeroIdentificacion;
            if (!string.IsNullOrWhiteSpace(email) && !emails.Add(email))
                errors.Add("El correo está duplicado en el archivo.");
            if (!string.IsNullOrWhiteSpace(identifier) && !documentNumbers.Add(identifier))
                errors.Add("El número de documento está duplicado en el archivo.");
            if (!string.IsNullOrWhiteSpace(row.Usuario.CodigoIdentificacion) &&
                !codes.Add(row.Usuario.CodigoIdentificacion))
                errors.Add("El código está duplicado en el archivo.");

            try
            {
                await ValidateNewAsync(row.Usuario, cancellationToken);
            }
            catch (UsuarioManagementException exception)
            {
                errors.Add(exception.Message);
            }

            output.Add(new CargaUsuarioFilaDto
            {
                Fila = row.Fila,
                Usuario = row.Usuario,
                Errores = errors.Distinct(StringComparer.Ordinal).ToArray()
            });
        }

        return output;
    }

    private async Task ValidateNewAsync(CrearUsuarioDto request, CancellationToken cancellationToken)
    {
        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(
                request,
                new ValidationContext(request),
                validationResults,
                validateAllProperties: true))
        {
            throw new UsuarioManagementException(
                StatusCodes.Status400BadRequest,
                string.Join(" ", validationResults.Select(result => result.ErrorMessage)));
        }
        if (request.Contrasena is not null &&
            (request.Contrasena.Length < 12 ||
             !request.Contrasena.Any(char.IsUpper) ||
             !request.Contrasena.Any(char.IsLower) ||
             !request.Contrasena.Any(char.IsDigit) ||
             !request.Contrasena.Any(character => !char.IsLetterOrDigit(character))))
        {
            throw new UsuarioManagementException(
                StatusCodes.Status400BadRequest,
                "La contraseña debe tener al menos 12 caracteres e incluir mayúscula, minúscula, número y símbolo.");
        }
        if (request.Rol is not ("Estudiante" or "Profesor"))
            throw new UsuarioManagementException(StatusCodes.Status400BadRequest, "Solo se pueden administrar estudiantes y profesores.");
        ValidateProfileFields(request.Rol, request.CodigoIdentificacion, request.Semestre,
            request.ProgramaAcademico, request.DepartamentoAcademico);
        if (request.CursoIds.Count is < 1 or > 50 ||
            request.CursoIds.Any(courseId => courseId == Guid.Empty) ||
            request.CursoIds.Distinct().Count() != request.CursoIds.Count)
        {
            throw new UsuarioManagementException(StatusCodes.Status400BadRequest, "Asigna entre 1 y 50 cursos activos sin duplicados.");
        }

        var email = request.CorreoInstitucional.Trim().ToLowerInvariant();
        var duplicate = await _repository.Usuarios.AnyAsync(user =>
            user.CorreoInstitucional.ToLower() == email ||
            user.NumeroIdentificacion == request.NumeroIdentificacion ||
            (request.CodigoIdentificacion != null &&
             user.CodigoIdentificacion == request.CodigoIdentificacion),
            cancellationToken);
        if (duplicate)
            throw new UsuarioManagementException(StatusCodes.Status409Conflict, "El correo, documento o código ya está registrado.");

        var courses = await _context.Cursos
            .Where(course => request.CursoIds.Contains(course.Id) && course.Activo)
            .Select(course => course.Id)
            .ToListAsync(cancellationToken);
        if (courses.Count != request.CursoIds.Count)
            throw new UsuarioManagementException(StatusCodes.Status400BadRequest, "Una o más asignaturas no existen o están inactivas.");
    }

    private static void ValidateProfileFields(
        string role,
        string? code,
        int? semester,
        string? program,
        string? department)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new UsuarioManagementException(StatusCodes.Status400BadRequest, "El código estudiantil o ID docente es obligatorio.");
        if (role == "Estudiante" &&
            (semester is null || string.IsNullOrWhiteSpace(program) || !string.IsNullOrWhiteSpace(department)))
            throw new UsuarioManagementException(StatusCodes.Status400BadRequest, "Semestre y programa son obligatorios para estudiantes.");
        if (role == "Profesor" &&
            (string.IsNullOrWhiteSpace(department) || semester is not null || !string.IsNullOrWhiteSpace(program)))
            throw new UsuarioManagementException(StatusCodes.Status400BadRequest, "El departamento académico es obligatorio para profesores.");
    }

    private async Task AddCourseAssignmentsAsync(
        Usuario user,
        IReadOnlyCollection<Guid> courseIds,
        CancellationToken cancellationToken)
    {
        if (user.Rol == "Estudiante")
        {
            foreach (var courseId in courseIds)
                _context.Matriculas.Add(new Matricula { EstudianteId = user.Id, CursoId = courseId });
        }
        else
        {
            foreach (var courseId in courseIds)
            {
                var course = await _context.Cursos.FirstAsync(item => item.Id == courseId, cancellationToken);
                var isTitular = course.DocenteId is null;
                if (isTitular)
                {
                    var previousAssignments = await _context.DocentesCursos
                        .Where(assignment => assignment.CursoId == courseId)
                        .ToListAsync(cancellationToken);
                    foreach (var previousAssignment in previousAssignments)
                        previousAssignment.RolDocente = "Apoyo";
                }
                _context.DocentesCursos.Add(new DocenteCurso
                {
                    UsuarioId = user.Id,
                    CursoId = courseId,
                    RolDocente = isTitular ? "Titular" : "Apoyo"
                });
                course.DocenteId ??= user.Id;
            }
        }
    }

    private async Task<UsuarioResponseDto> ToResponseAsync(Usuario user, CancellationToken cancellationToken)
    {
        List<CursoResumenDto> courses;
        if (user.Rol == "Estudiante")
        {
            courses = await (
                from enrollment in _context.Matriculas.AsNoTracking()
                join course in _context.Cursos.AsNoTracking() on enrollment.CursoId equals course.Id
                where enrollment.EstudianteId == user.Id && enrollment.Estado == "Activa"
                orderby course.Nombre
                select new CursoResumenDto
                {
                    Id = course.Id,
                    Codigo = course.Codigo,
                    Nombre = course.Nombre,
                    Semestre = course.Semestre,
                    ProgramaAcademico = course.ProgramaAcademico,
                    PeriodoAcademico = course.PeriodoAcademico
                }).ToListAsync(cancellationToken);
        }
        else
        {
            courses = await (
                from course in _context.Cursos.AsNoTracking()
                where course.DocenteId == user.Id ||
                      _context.DocentesCursos.Any(assignment =>
                          assignment.UsuarioId == user.Id && assignment.CursoId == course.Id)
                orderby course.Nombre
                select new CursoResumenDto
                {
                    Id = course.Id,
                    Codigo = course.Codigo,
                    Nombre = course.Nombre,
                    Semestre = course.Semestre,
                    ProgramaAcademico = course.ProgramaAcademico,
                    PeriodoAcademico = course.PeriodoAcademico
                }).Distinct().ToListAsync(cancellationToken);
        }

        return new UsuarioResponseDto
        {
            Id = user.Id,
            NombreCompleto = user.NombreCompleto,
            CorreoInstitucional = user.CorreoInstitucional,
            Rol = user.Rol,
            TipoDocumento = user.TipoDocumento ?? string.Empty,
            NumeroIdentificacion = user.NumeroIdentificacion ?? string.Empty,
            CodigoIdentificacion = user.CodigoIdentificacion,
            Semestre = user.Semestre,
            ProgramaAcademico = user.ProgramaAcademico,
            DepartamentoAcademico = user.DepartamentoAcademico,
            Activo = user.Activo,
            DebeCambiarContrasena = user.DebeCambiarContrasena,
            Cursos = courses
        };
    }

    private Usuario CreateEntity(Guid id, CrearUsuarioDto request) => new()
    {
        Id = id,
        NombreCompleto = request.NombreCompleto.Trim(),
        CorreoInstitucional = request.CorreoInstitucional.Trim().ToLowerInvariant(),
        Rol = request.Rol,
        FechaCreacion = DateTime.UtcNow,
        TipoDocumento = request.TipoDocumento,
        NumeroIdentificacion = request.NumeroIdentificacion.Trim(),
        CodigoIdentificacion = Clean(request.CodigoIdentificacion),
        Semestre = request.Rol == "Estudiante" ? request.Semestre : null,
        ProgramaAcademico = request.Rol == "Estudiante" ? Clean(request.ProgramaAcademico) : null,
        DepartamentoAcademico = request.Rol == "Profesor" ? Clean(request.DepartamentoAcademico) : null,
        Activo = true,
        DebeCambiarContrasena = true
    };

    private static void Normalize(CrearUsuarioDto request)
    {
        request.NombreCompleto = request.NombreCompleto.Trim();
        request.TipoDocumento = request.TipoDocumento.Trim().ToUpperInvariant();
        request.NumeroIdentificacion = request.NumeroIdentificacion.Trim();
        request.CorreoInstitucional = request.CorreoInstitucional.Trim().ToLowerInvariant();
        request.CodigoIdentificacion = Clean(request.CodigoIdentificacion);
        request.ProgramaAcademico = Clean(request.ProgramaAcademico);
        request.DepartamentoAcademico = Clean(request.DepartamentoAcademico);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string GenerateTemporaryPassword()
    {
        var random = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .Replace('+', 'A')
            .Replace('/', 'b')
            .TrimEnd('=');
        return $"{random}7!";
    }

    private async Task RollbackIdentityCreateAsync(
        Guid userId,
        Exception originalException,
        CancellationToken cancellationToken)
    {
        _logger.LogError(originalException, "No se pudo persistir el perfil para la cuenta Auth {UserId}.", userId);
        try
        {
            await _supabaseAdmin.DeleteAsync(userId, cancellationToken);
        }
        catch (Exception cleanupException)
        {
            _logger.LogCritical(cleanupException,
                "No se pudo compensar la cuenta Auth {UserId} tras un fallo al crear el perfil.", userId);
        }
    }
}
