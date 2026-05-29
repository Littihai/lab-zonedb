using Microsoft.EntityFrameworkCore;
using NexAuth.Domain.Entities;

namespace NexAuthDb.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options) { }

        public DbSet<User> Users => Set<User>();
        // เพิ่ม DbSet อื่นๆ ตามต้องการ
    }
}
