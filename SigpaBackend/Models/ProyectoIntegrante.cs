using System;

namespace SigpaBackend.Models
{
    public class ProyectoIntegrante
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        
        // Llaves foráneas para saber qué usuario está en qué proyecto
        public Guid ProyectoId { get; set; }
        public Guid UsuarioId { get; set; }
        
        public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;
    }
}