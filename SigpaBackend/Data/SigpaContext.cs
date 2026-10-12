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
    }
}