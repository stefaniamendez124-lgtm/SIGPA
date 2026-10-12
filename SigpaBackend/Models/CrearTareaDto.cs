using System;

namespace SigpaBackend.Models
{
    public class CrearTareaDto
    {
        public string Titulo { get; set; } = string.Empty;
        public Guid? ResponsableId { get; set; }
        public DateTime? FechaVencimiento { get; set; }
    }
}