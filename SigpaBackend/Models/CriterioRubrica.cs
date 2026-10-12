using System;

namespace SigpaBackend.Models
{
    public class CriterioRubrica
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid EntregableId { get; set; }
        
        public string Nombre { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public decimal PesoPorcentual { get; set; }
    }
}