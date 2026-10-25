using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SigpaBackend.Models
{
    [Table("usuarios")]
    public class Usuario
    {
        [Key]
        [Column("id")]
        public Guid Id { get; set; }

        [Column("nombre_completo")]
        public string NombreCompleto { get; set; } = string.Empty;

        [Column("correo_institucional")]
        public string CorreoInstitucional { get; set; } = string.Empty;

        [Column("rol")]
        public string Rol { get; set; } = string.Empty;

        [Column("fecha_creacion")]
        public DateTime FechaCreacion { get; set; }

        [Column("tipo_documento")]
        public string? TipoDocumento { get; set; }

        [Column("numero_identificacion")]
        public string? NumeroIdentificacion { get; set; }

        [Column("codigo_identificacion")]
        public string? CodigoIdentificacion { get; set; }

        [Column("semestre")]
        public int? Semestre { get; set; }

        [Column("programa_academico")]
        public string? ProgramaAcademico { get; set; }

        [Column("departamento_academico")]
        public string? DepartamentoAcademico { get; set; }

        [Column("activo")]
        public bool Activo { get; set; } = true;

        [Column("debe_cambiar_contrasena")]
        public bool DebeCambiarContrasena { get; set; }
    }
}