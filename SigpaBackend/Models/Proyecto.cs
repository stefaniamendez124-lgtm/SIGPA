
using System;

namespace SigpaBackend.Models
{
    public class Proyecto
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Nombre { get; set; } = string.Empty;

        public string Descripcion { get; set; } = string.Empty;

        public DateTime FechaInicio { get; set; }

        public DateTime FechaFin { get; set; }

        // Estado inicial del proyecto
        public string Estado { get; set; } = "Pendiente";

        public Guid CreadorId { get; set; }

        // Comentarios del profesor al evaluar el proyecto
        public string? ComentariosEvaluacion { get; set; }
    }
}
