using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }
    }
    public class DAL : DbContext
    {
        protected readonly IConfiguration Configuration;

        public DAL(DbContextOptions<DAL> options)
            : base(options)
        {
        }
        public virtual DbSet<GFMS_Facility> Facilities { get; set; }
        public virtual DbSet<GFMS_Rooms> Rooms { get; set; }
        public virtual DbSet<Gen_Company> Companies { get; set; }
        public virtual DbSet<GFMS_ReservationHeader> ReservationHeaders { get; set; }
        public virtual DbSet<GFMS_ReservationDetails> ReservationDetails { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {

        }
    }

 }
