using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SigpaBackend.Services;

public sealed class SupabaseAdminUserService : ISupabaseAdminUserService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public SupabaseAdminUserService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<Guid> CreateAsync(
        string email,
        string temporaryPassword,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Post,
            "admin/users",
            new
            {
                email,
                password = temporaryPassword,
                email_confirm = true,
                user_metadata = new { must_change_password = true }
            });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Supabase Auth no pudo crear la cuenta (HTTP {(int)response.StatusCode}).",
                null,
                response.StatusCode);
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var payload = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        var id = payload.RootElement.TryGetProperty("id", out var idElement)
            ? idElement.GetString()
            : null;
        return Guid.TryParse(id, out var userId)
            ? userId
            : throw new InvalidDataException("Supabase Auth devolvió un identificador de usuario inválido.");
    }

    public async Task UpdateAsync(
        Guid userId,
        string? email,
        bool active,
        bool mustChangePassword,
        CancellationToken cancellationToken)
    {
        var changes = new Dictionary<string, object?>
        {
            ["ban_duration"] = active ? "none" : "876000h",
            ["user_metadata"] = new { must_change_password = mustChangePassword }
        };
        if (!string.IsNullOrWhiteSpace(email))
        {
            changes["email"] = email;
            changes["email_confirm"] = true;
        }

        using var request = CreateRequest(HttpMethod.Put, $"admin/users/{userId}", changes);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Supabase Auth no pudo actualizar la cuenta (HTTP {(int)response.StatusCode}).",
                null,
                response.StatusCode);
        }
    }

    public async Task ChangePasswordAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Put,
            $"admin/users/{userId}",
            new
            {
                password,
                user_metadata = new { must_change_password = false }
            });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Supabase Auth no pudo actualizar la contraseña (HTTP {(int)response.StatusCode}).",
                null,
                response.StatusCode);
        }
    }

    public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Delete, $"admin/users/{userId}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            throw new HttpRequestException(
                $"Supabase Auth no pudo revertir la cuenta (HTTP {(int)response.StatusCode}).",
                null,
                response.StatusCode);
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, object? body = null)
    {
        var baseUrl = _configuration["Supabase:Url"] ?? Environment.GetEnvironmentVariable("SUPABASE_URL");
        var serviceKey = _configuration["Supabase:ServiceKey"] ??
                         Environment.GetEnvironmentVariable("SUPABASE_SERVICE_ROLE_KEY");
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var supabaseUri) ||
            string.IsNullOrWhiteSpace(serviceKey))
        {
            throw new InvalidOperationException(
                "La administración de usuarios requiere Supabase:Url y Supabase:ServiceKey configurados en el servidor.");
        }

        var request = new HttpRequestMessage(method, $"{supabaseUri.ToString().TrimEnd('/')}/auth/v1/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceKey);
        request.Headers.Add("apikey", serviceKey);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return request;
    }
}
