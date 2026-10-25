using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SigpaBackend.Data;
using SigpaBackend.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SigpaBackend.Services;

namespace SigpaBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly SigpaContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;
        private readonly ISupabaseAdminUserService _supabaseAdmin;
        private readonly AuditService _audit;

        public AuthController(
            SigpaContext context,
            IConfiguration configuration,
            ILogger<AuthController> logger,
            ISupabaseAdminUserService supabaseAdmin,
            AuditService audit)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
            _supabaseAdmin = supabaseAdmin;
            _audit = audit;
        }

        [HttpPost("registrar")]
        public async Task<IActionResult> RegistrarUsuario([FromBody] RegisterDto request)
        {
            return StatusCode(
                StatusCodes.Status410Gone,
                "El autorregistro está deshabilitado. Solicita la creación de cuenta al Coordinador Académico.");
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
                if (!usuarioDb.Activo)
                    return Unauthorized("La cuenta está deshabilitada. Contacta al Coordinador Académico.");

                return Ok(new 
                { 
                    Mensaje = "Inicio de sesión exitoso",
                    Token = token,
                    Usuario = new {
                        Id = usuarioDb.Id,
                        Nombre = usuarioDb.NombreCompleto,
                        Rol = usuarioDb.Rol,
                        DebeCambiarContrasena = usuarioDb.DebeCambiarContrasena
                    }
                });
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Falló el inicio de sesión en SIGPA.");
                return Problem("No fue posible completar el inicio de sesión.");
            }
        }

        [Authorize]
        [HttpPost("password-changed")]
        public async Task<IActionResult> ConfirmarCambioContrasena(
            [FromBody] CambiarContrasenaDto request,
            CancellationToken cancellationToken)
        {
            var userId = UserAccessService.GetUserId(User);
            if (userId is null) return Unauthorized();

            var user = await _context.Usuarios.FirstOrDefaultAsync(
                item => item.Id == userId.Value, cancellationToken);
            if (user is null || !user.Activo) return Forbid();
            if (!user.DebeCambiarContrasena)
                return BadRequest("La cuenta no requiere un cambio inicial de contraseña.");

            try
            {
                await _supabaseAdmin.ChangePasswordAsync(user.Id, request.Contrasena, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                _logger.LogError(exception, "Supabase Auth no pudo cambiar la contraseña inicial del usuario {UserId}.", user.Id);
                return Problem(statusCode: StatusCodes.Status502BadGateway,
                    title: "El proveedor de identidad no pudo actualizar la contraseña.");
            }

            user.DebeCambiarContrasena = false;
            _audit.Record(user.Id, "usuario.contrasena_inicial_actualizada", "usuario", user.Id);
            await _context.SaveChangesAsync(cancellationToken);
            return NoContent();
        }
    }
}