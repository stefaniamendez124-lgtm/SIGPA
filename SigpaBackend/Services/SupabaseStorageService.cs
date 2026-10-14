using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace SigpaBackend.Services;

public sealed class SupabaseStorageService
{
    public const long MaxFileSizeBytes = 25 * 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SupabaseStorageService> _logger;

    public SupabaseStorageService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<SupabaseStorageService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromMinutes(2);
    }

    public async Task<string> UploadAsync(
        Guid projectId,
        Guid deliverableId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length is <= 0 or > MaxFileSizeBytes)
            throw new InvalidDataException("El archivo debe tener entre 1 byte y 25 MB.");

        var extension = Path.GetExtension(Path.GetFileName(file.FileName)).ToLowerInvariant();
        if (extension is not (".pdf" or ".zip"))
            throw new InvalidDataException("Solo se permiten archivos PDF o ZIP.");

        var contentType = extension == ".pdf" ? "application/pdf" : "application/zip";
        await using (var validationStream = file.OpenReadStream())
        {
            var header = new byte[8];
            var bytesRead = await validationStream.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
            var validSignature = extension == ".pdf"
                ? bytesRead >= 5 && header.AsSpan(0, 5).SequenceEqual("%PDF-"u8)
                : bytesRead >= 4 &&
                  header[0] == 0x50 && header[1] == 0x4B &&
                  (header[2] == 0x03 && header[3] == 0x04 ||
                   header[2] == 0x05 && header[3] == 0x06 ||
                   header[2] == 0x07 && header[3] == 0x08);
            if (!validSignature)
                throw new InvalidDataException("El contenido del archivo no coincide con su extensión.");
        }

        var (baseUrl, bucket, serviceKey) = GetConfiguration();
        var objectKey = $"{projectId:D}/{deliverableId:D}/{Guid.NewGuid():N}{extension}";
        var uploadUri = BuildObjectUri(baseUrl, bucket, objectKey);
        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUri);
        request.Headers.Add("apikey", serviceKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceKey);
        request.Headers.Add("x-upsert", "false");

        await using var uploadStream = file.OpenReadStream();
        request.Content = new StreamContent(uploadStream);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        request.Content.Headers.ContentLength = file.Length;

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Storage rechazó la carga con estado HTTP {StatusCode}.", response.StatusCode);
            throw new HttpRequestException("El almacenamiento no pudo guardar el archivo.");
        }

        return objectKey;
    }

    public async Task<string> CreateSignedUrlAsync(string objectKey, CancellationToken cancellationToken)
    {
        var (baseUrl, bucket, serviceKey) = GetConfiguration();
        var endpoint = BuildObjectUri(baseUrl, bucket, objectKey, "sign");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new { expiresIn = 300 })
        };
        request.Headers.Add("apikey", serviceKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Storage no pudo firmar la descarga; estado HTTP {StatusCode}.", response.StatusCode);
            throw new HttpRequestException("No fue posible preparar la descarga.");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("signedURL", out var signedUrlElement) ||
            signedUrlElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("Storage devolvió una respuesta de firma no válida.");
        }

        var signedUrl = signedUrlElement.GetString();
        if (string.IsNullOrWhiteSpace(signedUrl))
            throw new InvalidOperationException("Storage devolvió una URL firmada vacía.");

        return Uri.TryCreate(signedUrl, UriKind.Absolute, out _)
            ? signedUrl
            : $"{baseUrl.TrimEnd('/')}/storage/v1/{signedUrl.TrimStart('/')}";
    }

    private (string BaseUrl, string Bucket, string ServiceKey) GetConfiguration()
    {
        var baseUrl = _configuration["Supabase:Url"] ??
                      Environment.GetEnvironmentVariable("SUPABASE_URL");
        var serviceKey = _configuration["Supabase:ServiceKey"] ??
                         Environment.GetEnvironmentVariable("SUPABASE_SERVICE_ROLE_KEY");
        var bucket = _configuration["Supabase:DeliverablesBucket"] ?? "entregables";

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _) || string.IsNullOrWhiteSpace(serviceKey))
            throw new InvalidOperationException("Configure Supabase:Url y Supabase:ServiceKey para usar el almacenamiento.");
        if (bucket.Contains('/') || bucket.Contains('\\'))
            throw new InvalidOperationException("Supabase:DeliverablesBucket debe ser el nombre simple de un bucket.");

        return (baseUrl, bucket, serviceKey);
    }

    private static string BuildObjectUri(string baseUrl, string bucket, string objectKey, string? operation = null)
    {
        var path = string.Join("/", objectKey.Split('/').Select(Uri.EscapeDataString));
        var action = operation is null ? "object" : $"object/{operation}";
        return $"{baseUrl.TrimEnd('/')}/storage/v1/{action}/{Uri.EscapeDataString(bucket)}/{path}";
    }
}
