using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SigpaBackend.Data;
using SigpaBackend.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SigpaBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly SigpaContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            SigpaContext context,
            IConfiguration configuration,
            ILogger<AuthController> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpPost("registrar")]
        public async Task<IActionResult> RegistrarUsuario([FromBody] RegisterDto request)
        {
            if (!string.IsNullOrWhiteSpace(request.Rol) &&
                !string.Equals(request.Rol, "Estudiante", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("El registro público solo permite crear cuentas de estudiante. Los roles institucionales los asigna un administrador.");
            }

            try
            {
                var supabaseUrl = _configuration["Supabase:Url"] ??
                                  Environment.GetEnvironmentVariable("SUPABASE_URL");
                var serviceKey = _configuration["Supabase:ServiceKey"] ??
                                 Environment.GetEnvironmentVariable("SUPABASE_SERVICE_ROLE_KEY");
                if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(serviceKey))
                    return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "El registro de cuentas no está configurado.");

                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", serviceKey);
                httpClient.DefaultRequestHeaders.Add("apikey", serviceKey);

                var payload = new
                {
                    email = request.Email,
                    password = request.Password,
                    email_confirm = true 
                };

                var jsonPayload = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync($"{supabaseUrl}/auth/v1/admin/users", jsonPayload);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Supabase rechazó el registro con estado HTTP {StatusCode}.", response.StatusCode);
                    return Problem(
                        statusCode: StatusCodes.Status502BadGateway,
                        title: "El proveedor de identidad no pudo crear la cuenta.");
                }

                var responseString = await response.Content.ReadAsStringAsync();
                var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseString);
                var userId = jsonResponse.GetProperty("id").GetString();

                var nuevoUsuario = new Usuario
                {
                    Id = Guid.Parse(userId!),
                    NombreCompleto = request.NombreCompleto,
                    CorreoInstitucional = request.Email,
                    Rol = "Estudiante",
                    FechaCreacion = DateTime.UtcNow
                };

                var usuarioExistente = await _context.Usuarios.FindAsync(nuevoUsuario.Id);
                if (usuarioExistente == null)
                {
                    _context.Usuarios.Add(nuevoUsuario);
                }
                else
                {
                    usuarioExistente.NombreCompleto = nuevoUsuario.NombreCompleto;
                    usuarioExistente.CorreoInstitucional = nuevoUsuario.CorreoInstitucional;
                    usuarioExistente.Rol = "Estudiante";
                }

                await _context.SaveChangesAsync();

                return Ok(new { mensaje = "Usuario registrado exitosamente", usuarioId = nuevoUsuario.Id });
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Falló el registro del usuario en SIGPA.");
                return Problem("No fue posible completar el registro.");
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto request)
        {
            try
            {
                var supabaseUrl = _configuration["Supabase:Url"] ??
                                  Environment.GetEnvironmentVariable("SUPABASE_URL");
                var serviceKey = _configuration["Supabase:ServiceKey"] ??
                                 Environment.GetEnvironmentVariable("SUPABASE_SERVICE_ROLE_KEY");
                var apiKey = _configuration["Supabase:AnonKey"] ??
                             Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY") ??
                             serviceKey;
                if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(apiKey))
                    return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "La autenticación no está configurada.");

                // Petición directa para validar credenciales y obtener el JWT
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("apikey", apiKey);

                var payload = new
                {
                    email = request.Email,
                    password = request.Password
                };

                var jsonPayload = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync($"{supabaseUrl}/auth/v1/token?grant_type=password", jsonPayload);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Supabase rechazó el inicio de sesión con estado HTTP {StatusCode}.", response.StatusCode);
                    return Unauthorized("Correo o contraseña incorrectos.");
                }

                var responseString = await response.Content.ReadAsStringAsync();
                var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseString);
                var token = jsonResponse.GetProperty("access_token").GetString();

                // Buscamos al usuario en PostgreSQL
                var usuarioDb = await _context.Usuarios
                    .FirstOrDefaultAsync(u => u.CorreoInstitucional == request.Email);
                if (usuarioDb is null)
                {
                    return Unauthorized("La cuenta no tiene un perfil activo en SIGPA.");
                }

                return Ok(new 
                { 
                    Mensaje = "Inicio de sesión exitoso",
                    Token = token,
                    Usuario = new {
                        Id = usuarioDb.Id,
                        Nombre = usuarioDb.NombreCompleto,
                        Rol = usuarioDb.Rol
                    }
                });
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Falló el inicio de sesión en SIGPA.");
                return Problem("No fue posible completar el inicio de sesión.");
            }
        }
    }
}