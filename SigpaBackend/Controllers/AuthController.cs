using Microsoft.AspNetCore.Mvc;
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

        public AuthController(SigpaContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [HttpPost("registrar")]
        public async Task<IActionResult> RegistrarUsuario([FromBody] RegisterDto request)
        {
            var rolesPermitidos = new List<string> { "Estudiante", "Profesor", "Coordinador", "Administrador" };
            if (!rolesPermitidos.Contains(request.Rol))
                return BadRequest("Rol no válido. Use: Estudiante, Profesor, Coordinador o Administrador.");

            try
            {
                var supabaseUrl = _configuration["Supabase:Url"];
                var serviceKey = _configuration["Supabase:ServiceKey"];

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
                    var error = await response.Content.ReadAsStringAsync();
                    return StatusCode(500, $"Error al crear usuario en Supabase: {error}");
                }

                var responseString = await response.Content.ReadAsStringAsync();
                var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseString);
                var userId = jsonResponse.GetProperty("id").GetString();

                var nuevoUsuario = new Usuario
                {
                    Id = Guid.Parse(userId!),
                    NombreCompleto = request.NombreCompleto,
                    CorreoInstitucional = request.Email,
                    Rol = request.Rol,
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
                    usuarioExistente.Rol = nuevoUsuario.Rol;
                }

                await _context.SaveChangesAsync();

                return Ok(new { mensaje = "Usuario registrado exitosamente", usuarioId = nuevoUsuario.Id });
            }
            catch (Exception ex)
            {
                var mensajeError = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return StatusCode(500, $"Error interno del servidor al guardar en DB: {mensajeError}");
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto request)
        {
            try
            {
                var supabaseUrl = _configuration["Supabase:Url"];
                var serviceKey = _configuration["Supabase:ServiceKey"];

                // Petición directa para validar credenciales y obtener el JWT
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("apikey", serviceKey);

                var payload = new
                {
                    email = request.Email,
                    password = request.Password
                };

                var jsonPayload = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync($"{supabaseUrl}/auth/v1/token?grant_type=password", jsonPayload);

                if (!response.IsSuccessStatusCode)
{
    var errorSupabase = await response.Content.ReadAsStringAsync();
    Console.WriteLine($"---> RESPUESTA REAL DE SUPABASE: {errorSupabase}"); // <-- Añade esta línea
    return Unauthorized("Correo o contraseña incorrectos.");
}

                var responseString = await response.Content.ReadAsStringAsync();
                var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseString);
                var token = jsonResponse.GetProperty("access_token").GetString();

                // Buscamos al usuario en PostgreSQL
                var usuarioDb = _context.Usuarios.FirstOrDefault(u => u.CorreoInstitucional == request.Email);

                return Ok(new 
                { 
                    Mensaje = "Inicio de sesión exitoso",
                    Token = token,
                    Usuario = new {
                        Id = usuarioDb?.Id,
                        Nombre = usuarioDb?.NombreCompleto,
                        Rol = usuarioDb?.Rol
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno: {ex.Message}");
            }
        }
    }
}