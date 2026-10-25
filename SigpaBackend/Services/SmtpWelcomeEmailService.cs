using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;

namespace SigpaBackend.Services;

public sealed class SmtpWelcomeEmailService : IWelcomeEmailService
{
    private readonly IConfiguration _configuration;

    public SmtpWelcomeEmailService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task SendTemporaryPasswordAsync(
        string email,
        string fullName,
        string temporaryPassword,
        CancellationToken cancellationToken)
    {
        var host = _configuration["Smtp:Host"];
        var from = _configuration["Smtp:From"];
        var username = _configuration["Smtp:Username"];
        var password = _configuration["Smtp:Password"];
        var port = _configuration.GetValue<int?>("Smtp:Port") ?? 587;
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from) ||
            string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "El correo de bienvenida fue solicitado, pero falta configurar Smtp:Host, From, Username y Password.");
        }

        using var message = new MailMessage(from, email)
        {
            Subject = "Acceso inicial a SIGPA",
            IsBodyHtml = true,
            Body = $"""
                <p>Hola {HtmlEncoder.Default.Encode(fullName)},</p>
                <p>El coordinador académico creó tu cuenta en SIGPA.</p>
                <p>Contraseña temporal: <strong>{HtmlEncoder.Default.Encode(temporaryPassword)}</strong></p>
                <p>Por seguridad, deberás cambiarla antes de acceder a tus cursos.</p>
                """
        };
        using var client = new SmtpClient(host, port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(username, password),
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        await client.SendMailAsync(message, cancellationToken);
    }
}
