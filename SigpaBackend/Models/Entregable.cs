using System;

namespace SigpaBackend.Models
{
    public class Entregable
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ProyectoId { get; set; }
        
        // Configuración del Profesor (REQ-4)
        public string Titulo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public decimal PorcentajeNota { get; set; }
        public DateTimeOffset FechaLimite { get; set; }

        // Evidencia del Estudiante (REQ-6 y REQ-7)
        public string ArchivoUrl { get; set; } = string.Empty;
        public DateTime? FechaEntrega { get; set; } // Es nullable (?) porque al inicio está vacío
        public string Estado { get; set; } = "Pendiente"; // Pendiente, Entregado a Tiempo, Entregado Tardío

        public bool PermiteEntregaExtemporanea { get; set; }

        public decimal PenalizacionExtemporaneaPct { get; set; }

        public decimal PenalizacionAplicadaPct { get; set; }
    }
}