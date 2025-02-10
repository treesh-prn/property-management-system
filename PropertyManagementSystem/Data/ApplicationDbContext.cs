using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
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
        public virtual DbSet<Gen_Employee> Employees { get; set; }
        public virtual DbSet<Gen_Department> Departments { get; set; }
        public virtual DbSet<Gen_Company> Companies { get; set; }
        public virtual DbSet<Gen_Position> Positions { get; set; }
        public virtual DbSet<GFMS_Facility> Facilities { get; set; }
        public virtual DbSet<GFMS_Rooms> Rooms { get; set; }
        public virtual DbSet<GFMS_Equipments> Equipments { get; set; }
        public virtual DbSet<GFMS_ReservationHeader> ReservationHeaders { get; set; }
        public virtual DbSet<GFMS_ReservationDetails> ReservationDetails { get; set; }
        public virtual DbSet<HRIS_EmploymentLevel> EmploymentLevels { get; set; }
        public DbSet<ReservationReport> ReservationReports { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<GFMS_Rooms>()
                .HasOne(r => r.Facility)
                .WithMany(f => f.Rooms)
                .HasForeignKey(r => r.FacilityId);

            modelBuilder.Entity<GFMS_ReservationDetails>()
                .HasKey(r => r.ReservationDetailId);

            modelBuilder.Entity<GFMS_ReservationDetails>()
                .HasOne(rd => rd.ReservationHeader)
                .WithMany()
                .HasForeignKey(rd => rd.ReservationId);

            modelBuilder.Entity<GFMS_ReservationDetails>()
            .Property(e => e.StartDate)
            .HasColumnType("datetime");

            modelBuilder.Entity<GFMS_ReservationDetails>()
            .Property(e => e.EndDate)
            .HasColumnType("datetime");

            modelBuilder.Entity<ReservationReport>().HasNoKey();
        }
    }

 }
