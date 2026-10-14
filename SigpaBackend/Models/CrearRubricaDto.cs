using System.Collections.Generic;

namespace SigpaBackend.Models
{
    // Esto representa la información de un solo criterio
    public class CriterioDto
    {
        public string Nombre { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public decimal PesoPorcentual { get; set; }
        public List<NivelRubricaDto> Niveles { get; set; } = new();
    }

    public class NivelRubricaDto
    {
        public int Nivel { get; set; }
        public string Etiqueta { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public decimal Puntaje { get; set; }
    }

    // Esto representa la rúbrica completa (una lista de criterios)
    public class CrearRubricaDto
    {
        public List<CriterioDto> Criterios { get; set; } = new List<CriterioDto>();
    }
}