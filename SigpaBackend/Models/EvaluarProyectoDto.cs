namespace SigpaBackend.Models
{
    public class EvaluarProyectoDto
    {
        public string Estado { get; set; } = string.Empty; // "Aprobado" o "Rechazado"
        public string Comentarios { get; set; } = string.Empty; // Justificación del profesor
    }
}