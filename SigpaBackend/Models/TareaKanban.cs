using System;

namespace SigpaBackend.Models
{
    public class TareaKanban
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ProyectoId { get; set; }
        
        public string Titulo { get; set; } = string.Empty;

        public string Descripcion { get; set; } = string.Empty;
        
        // Puede ser null si la tarea aún no tiene a nadie asignado
        public Guid? ResponsableId { get; set; } 
        
        public DateTime? FechaVencimiento { get; set; }
        
        // Las tres columnas de tu requerimiento CU-03
        public string Estado { get; set; } = "Por Hacer"; 
    }
}