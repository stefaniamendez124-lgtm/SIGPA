
using System;

namespace SigpaBackend.Models
{
    public class Proyecto
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Nombre { get; set; } = string.Empty;

        public string Descripcion { get; set; } = string.Empty;

        public string Curso { get; set; } = "Ingeniería de requisitos";

        public int Progreso { get; set; } = 15;

        public DateTime? FechaInicio { get; set; }

        public DateTime? FechaFin { get; set; }

        public DateTime? FechaEntrega { get; set; }

        public string Estado { get; set; } = "Pendiente";

        public Guid CreadorId { get; set; }

        public string? ComentariosEvaluacion { get; set; }
    }
}
