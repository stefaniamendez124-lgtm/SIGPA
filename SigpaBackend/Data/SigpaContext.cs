using Microsoft.EntityFrameworkCore;
using SigpaBackend.Models;

namespace SigpaBackend.Data
{
    public class SigpaContext : DbContext
    {
        public SigpaContext(DbContextOptions<SigpaContext> options) : base(options) { }

        public DbSet<Usuario> Usuarios { get; set; }
        public DbSet<Proyecto> Proyectos { get; set; }
        public DbSet<ProyectoIntegrante> ProyectoIntegrantes { get; set; }
        public DbSet<Entregable> Entregables { get; set; }
        public DbSet<CriterioRubrica> CriteriosRubrica { get; set; } // Agregado para el CU-04
        public DbSet<TareaKanban> TareasKanban { get; set; } // Agregado para el CU-03
        public DbSet<Riesgo> Riesgos { get; set; }
        public DbSet<EventoAuditoria> EventosAuditoria { get; set; }
        public DbSet<ObjetivoEspecifico> ObjetivosEspecificos { get; set; }
        public DbSet<Evaluacion> Evaluaciones { get; set; }
        public DbSet<EvaluacionCriterio> EvaluacionCriterios { get; set; }
        public DbSet<EntregaVersion> EntregasVersiones { get; set; }
        public DbSet<NivelRubrica> NivelesRubrica { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ProyectoIntegrante>()
                .Property(member => member.FechaRegistro)
                .HasColumnName("created_at");

            modelBuilder.Entity<EventoAuditoria>()
                .Property(auditEvent => auditEvent.Datos)
                .HasColumnType("jsonb");

            modelBuilder.Entity<ObjetivoEspecifico>()
                .HasIndex(objective => new { objective.ProyectoId, objective.Orden })
                .IsUnique();

            modelBuilder.Entity<Riesgo>()
                .HasIndex(risk => new { risk.ProyectoId, risk.CreatedAt });

            modelBuilder.Entity<EventoAuditoria>()
                .HasIndex(auditEvent => new { auditEvent.RecursoTipo, auditEvent.RecursoId, auditEvent.OcurridoAt });
        }
    }
}