
using Microsoft.EntityFrameworkCore;
using SigpaBackend.Models;

namespace SigpaBackend.Data
{
    public class SigpaDbContext : DbContext
    {
        public SigpaDbContext(DbContextOptions<SigpaDbContext> options)
            : base(options)
        {
        }

        public DbSet<Usuario> Usuarios { get; set; }
    }
}
