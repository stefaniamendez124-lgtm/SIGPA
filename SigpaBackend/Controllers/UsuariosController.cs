using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SigpaBackend.Models;
using SigpaBackend.Services;

namespace SigpaBackend.Controllers;

[ApiController]
[Route("api/v1/usuarios")]
[Authorize(Policy = "Coordinador")]
public sealed class UsuariosController : ControllerBase
{
    private const long MaxUploadSize = 10 * 1024 * 1024;
    private readonly IUsuarioService _usuarioService;
    private readonly ILogger<UsuariosController> _logger;

    public UsuariosController(IUsuarioService usuarioService, ILogger<UsuariosController> logger)
    {
        _usuarioService = usuarioService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanoPagina = 20,
        [FromQuery] string? rol = null,
        [FromQuery] string? programa = null,
        [FromQuery] Guid? cursoId = null,
        [FromQuery] bool? activo = null,
        [FromQuery] string? busqueda = null,
        CancellationToken cancellationToken = default)
    {
        if (pagina < 1 || tamanoPagina is < 1 or > 100)
            return BadRequest("La página debe ser mayor que cero y el tamaño debe estar entre 1 y 100.");
        if (rol is not null && rol is not ("Todos" or "Estudiante" or "Profesor"))
            return BadRequest("El rol debe ser Estudiante, Profesor o Todos.");

        var result = await _usuarioService.ListarAsync(
            pagina, tamanoPagina, rol, programa, cursoId, activo, busqueda, cancellationToken);
        return Ok(result);
    }

    [HttpGet("cursos")]
    public async Task<IActionResult> ObtenerCursos(CancellationToken cancellationToken) =>
        Ok(await _usuarioService.ObtenerCursosAsync(cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Crear(
        [FromBody] CrearUsuarioDto request,
        CancellationToken cancellationToken)
    {
        var coordinatorId = UserAccessService.GetUserId(User);
        if (coordinatorId is null) return Unauthorized();
        try
        {
            var result = await _usuarioService.CrearAsync(request, coordinatorId.Value, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (Exception exception) when (TryMapExpectedError(exception, out _))
        {
            return MapExpectedError(exception);
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Actualizar(
        Guid id,
        [FromBody] ActualizarUsuarioDto request,
        CancellationToken cancellationToken)
    {
        var coordinatorId = UserAccessService.GetUserId(User);
        if (coordinatorId is null) return Unauthorized();
        try
        {
            var updated = await _usuarioService.ActualizarAsync(id, request, coordinatorId.Value, cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (Exception exception) when (TryMapExpectedError(exception, out _))
        {
            return MapExpectedError(exception);
        }
    }

    [HttpPost("{id:guid}/asignar-cursos")]
    public async Task<IActionResult> AsignarCursos(
        Guid id,
        [FromBody] AsignarCursosDto request,
        CancellationToken cancellationToken)
    {
        var coordinatorId = UserAccessService.GetUserId(User);
        if (coordinatorId is null) return Unauthorized();
        try
        {
            var updated = await _usuarioService.AsignarCursosAsync(id, request, coordinatorId.Value, cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (Exception exception) when (TryMapExpectedError(exception, out _))
        {
            return MapExpectedError(exception);
        }
    }

    [HttpGet("plantilla.csv")]
    public IActionResult DescargarPlantilla()
    {
        const string csv =
            "nombreCompleto,tipoDocumento,numeroIdentificacion,correoInstitucional,rol,codigoIdentificacion,semestre,programaAcademico,departamentoAcademico,cursoIds\r\n" +
            "Ana Pérez,TI,10000001,ana@universidad.edu,Estudiante,EST-001,3,Ingeniería de Sistemas,,UUID_CURSO_1|UUID_CURSO_2\r\n" +
            "Carlos Ruiz,CC,20000001,carlos@universidad.edu,Profesor,DOC-001,,,Ingeniería de Sistemas,UUID_CURSO_1\r\n";
        return File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", "plantilla-usuarios-sigpa.csv");
    }

    [HttpPost("carga-masiva/validar")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadSize)]
    public async Task<IActionResult> ValidarCarga(
        [FromForm] IFormFile archivo,
        CancellationToken cancellationToken)
    {
        if (archivo is null || archivo.Length is 0 or > MaxUploadSize)
            return BadRequest("Adjunta un archivo de hasta 10 MB.");
        var extension = Path.GetExtension(archivo.FileName);
        if (!extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest("El archivo debe ser CSV o Excel .xlsx.");

        try
        {
            var rows = await ReadUploadAsync(archivo, cancellationToken);
            return Ok(await _usuarioService.ValidarCargaAsync(rows, cancellationToken));
        }
        catch (InvalidDataException exception)
        {
            return BadRequest(exception.Message);
        }
        catch (System.Xml.XmlException exception)
        {
            _logger.LogInformation(exception, "Se rechazó un Excel inválido en carga masiva.");
            return BadRequest("El archivo Excel está dañado o tiene un formato no compatible.");
        }
        catch (UsuarioManagementException exception)
        {
            return StatusCode(exception.StatusCode, new { detail = exception.Message });
        }
    }

    [HttpPost("carga-masiva")]
    public async Task<IActionResult> ImportarCarga(
        [FromBody] ValidarCargaDto request,
        CancellationToken cancellationToken)
    {
        var coordinatorId = UserAccessService.GetUserId(User);
        if (coordinatorId is null) return Unauthorized();
        try
        {
            var validation = await _usuarioService.ValidarCargaAsync(request.Filas, cancellationToken);
            var result = await _usuarioService.ImportarAsync(validation, coordinatorId.Value, cancellationToken);
            return Ok(result);
        }
        catch (Exception exception) when (TryMapExpectedError(exception, out _))
        {
            return MapExpectedError(exception);
        }
    }

    private IActionResult MapExpectedError(Exception exception)
    {
        if (exception is UsuarioManagementException managementException)
            return StatusCode(managementException.StatusCode, new { detail = managementException.Message });
        if (exception is DbUpdateException)
        {
            _logger.LogWarning(exception, "Conflicto al persistir un cambio de administración de usuarios.");
            return Conflict(new { detail = "Los datos entran en conflicto con un usuario o una asignación existente." });
        }
        if (exception is HttpRequestException)
        {
            _logger.LogError(exception, "No se pudo completar una operación administrativa en Supabase Auth.");
            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "El proveedor de identidad no pudo completar la operación.");
        }
        if (exception is InvalidDataException or JsonException)
        {
            _logger.LogError(exception, "El proveedor de identidad devolvió una respuesta inválida.");
            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "La respuesta del proveedor de identidad no es válida.");
        }
        if (exception is InvalidOperationException)
        {
            _logger.LogError(exception, "La administración de usuarios no está configurada correctamente.");
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "La administración de usuarios no está disponible.");
        }
        throw exception;
    }

    private static bool TryMapExpectedError(Exception exception, out bool expected)
    {
        expected = exception is UsuarioManagementException or DbUpdateException or
            HttpRequestException or InvalidOperationException or InvalidDataException or JsonException;
        return expected;
    }

    private static async Task<IReadOnlyList<CargaUsuarioFilaDto>> ReadUploadAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var records = Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? ReadXlsx(stream)
            : await ReadCsvAsync(stream, cancellationToken);
        if (records.Count < 2)
            throw new InvalidDataException("El archivo debe incluir encabezados y al menos un registro.");

        var headers = records[0].Select(NormalizeHeader).ToArray();
        var requiredHeaders = new[]
        {
            "nombrecompleto", "tipodocumento", "numeroidentificacion",
            "correoinstitucional", "rol", "codigoidentificacion", "cursoids"
        };
        var missing = requiredHeaders.Where(header => !headers.Contains(header)).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException($"Faltan columnas requeridas: {string.Join(", ", missing)}.");

        var output = new List<CargaUsuarioFilaDto>();
        for (var index = 1; index < records.Count; index++)
        {
            var record = records[index];
            if (record.All(string.IsNullOrWhiteSpace))
                continue;
            string Value(string key)
            {
                var columnIndex = Array.IndexOf(headers, key);
                return columnIndex < record.Count ? record[columnIndex].Trim() : string.Empty;
            }

            var courseIds = new List<Guid>();
            var errors = new List<string>();
            foreach (var value in Value("cursoids").Split(['|', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Guid.TryParse(value, out var courseId))
                    courseIds.Add(courseId);
                else
                    errors.Add($"Identificador de curso inválido: {value}.");
            }
            int? semester = null;
            var semesterText = Value("semestre");
            if (!string.IsNullOrWhiteSpace(semesterText))
            {
                if (int.TryParse(semesterText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSemester))
                    semester = parsedSemester;
                else
                    errors.Add("El semestre debe ser un número entero.");
            }

            output.Add(new CargaUsuarioFilaDto
            {
                Fila = index + 1,
                Usuario = new CrearUsuarioDto
                {
                    NombreCompleto = Value("nombrecompleto"),
                    TipoDocumento = Value("tipodocumento"),
                    NumeroIdentificacion = Value("numeroidentificacion"),
                    CorreoInstitucional = Value("correoinstitucional"),
                    Rol = NormalizeRole(Value("rol")),
                    CodigoIdentificacion = Value("codigoidentificacion"),
                    Semestre = semester,
                    ProgramaAcademico = Value("programaacademico"),
                    DepartamentoAcademico = Value("departamentoacademico"),
                    CursoIds = courseIds
                },
                Errores = errors
            });
        }

        return output;
    }

    private static async Task<List<List<string>>> ReadCsvAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                if (quoted && index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ',' && !quoted)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if ((character == '\r' || character == '\n') && !quoted)
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                    index++;
                row.Add(field.ToString());
                field.Clear();
                if (row.Any(value => !string.IsNullOrWhiteSpace(value)))
                    rows.Add(row);
                row = [];
            }
            else
            {
                field.Append(character);
            }
        }
        if (quoted)
            throw new InvalidDataException("El CSV contiene un campo entrecomillado sin cierre.");
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            if (row.Any(value => !string.IsNullOrWhiteSpace(value)))
                rows.Add(row);
        }
        return rows;
    }

    private static List<List<string>> ReadXlsx(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > 2_000 || archive.Entries.Sum(entry => entry.Length) > 50 * 1024 * 1024)
            throw new InvalidDataException("El archivo Excel descomprimido excede el límite permitido.");

        var sharedStrings = ReadSharedStrings(archive);
        var worksheet = archive.GetEntry(ResolveFirstWorksheet(archive))
            ?? throw new InvalidDataException("No se encontró la primera hoja del archivo Excel.");
        using var worksheetStream = worksheet.Open();
        var document = XDocument.Load(worksheetStream);
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return document.Descendants(spreadsheet + "row")
            .Select(row => ParseSpreadsheetRow(row, spreadsheet, sharedStrings))
            .ToList();
    }

    private static string ResolveFirstWorksheet(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml")
            ?? throw new InvalidDataException("El archivo Excel no contiene un libro válido.");
        using var workbookStream = workbookEntry.Open();
        var workbook = XDocument.Load(workbookStream);
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var relationshipId = (string?)workbook.Descendants(spreadsheet + "sheet")
            .FirstOrDefault()?.Attribute(officeRelationships + "id");
        if (string.IsNullOrWhiteSpace(relationshipId))
            throw new InvalidDataException("El libro Excel no contiene hojas.");

        var relationshipsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels")
            ?? throw new InvalidDataException("El libro Excel no contiene sus relaciones de hoja.");
        using var relationshipsStream = relationshipsEntry.Open();
        var relationships = XDocument.Load(relationshipsStream);
        XNamespace packageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        var target = (string?)relationships.Descendants(packageRelationships + "Relationship")
            .FirstOrDefault(item => (string?)item.Attribute("Id") == relationshipId)?.Attribute("Target");
        if (string.IsNullOrWhiteSpace(target))
            throw new InvalidDataException("No se pudo resolver la primera hoja del archivo Excel.");

        var normalized = target.StartsWith("/", StringComparison.Ordinal)
            ? target.TrimStart('/')
            : $"xl/{target}";
        var segments = new List<string>();
        foreach (var segment in normalized.Split('/'))
        {
            if (segment is "" or ".") continue;
            if (segment == "..")
            {
                if (segments.Count == 0)
                    throw new InvalidDataException("La ruta de la hoja Excel no es válida.");
                segments.RemoveAt(segments.Count - 1);
            }
            else
            {
                segments.Add(segment);
            }
        }
        var path = string.Join('/', segments);
        if (!path.StartsWith("xl/worksheets/", StringComparison.Ordinal))
            throw new InvalidDataException("La primera hoja debe ser una hoja de cálculo válida.");
        return path;
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return document.Descendants(spreadsheet + "si")
            .Select(item => string.Concat(item.Descendants(spreadsheet + "t").Select(text => text.Value)))
            .ToList();
    }

    private static List<string> ParseSpreadsheetRow(
        XElement row,
        XNamespace spreadsheet,
        IReadOnlyList<string> sharedStrings)
    {
        var cells = new SortedDictionary<int, string>();
        foreach (var cell in row.Elements(spreadsheet + "c"))
        {
            var address = (string?)cell.Attribute("r") ?? string.Empty;
            var column = 0;
            foreach (var character in address.TakeWhile(char.IsLetter))
                column = column * 26 + char.ToUpperInvariant(character) - 'A' + 1;
            column--;
            var type = (string?)cell.Attribute("t");
            var value = cell.Element(spreadsheet + "v")?.Value ?? string.Empty;
            if (type == "s" && int.TryParse(value, out var stringIndex) &&
                stringIndex >= 0 && stringIndex < sharedStrings.Count)
                value = sharedStrings[stringIndex];
            else if (type == "inlineStr")
                value = string.Concat(cell.Descendants(spreadsheet + "t").Select(text => text.Value));
            cells[column] = value;
        }

        if (cells.Count == 0) return [];
        var values = Enumerable.Repeat(string.Empty, cells.Keys.Max() + 1).ToArray();
        foreach (var (column, value) in cells)
            values[column] = value;
        return values.ToList();
    }

    private static string NormalizeHeader(string header) =>
        new(header.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string NormalizeRole(string role)
    {
        var normalized = role.Trim().ToLowerInvariant();
        return normalized switch
        {
            "estudiante" or "student" => "Estudiante",
            "profesor" or "docente" or "teacher" => "Profesor",
            _ => role.Trim()
        };
    }
}
