using Grafirio.DataAnalysis.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.DataAnalysis.Api.Data;

/// <summary>
/// Data Analysis veritabanı context'i
/// </summary>
public class DataAnalysisDbContext : DbContext
{
    public DataAnalysisDbContext(DbContextOptions<DataAnalysisDbContext> options) 
        : base(options)
    {
    }
    
    public DbSet<SavedConnection> SavedConnections { get; set; }
    public DbSet<AnalysisConfig> AnalysisConfigs { get; set; }
    public DbSet<QueryHistory> QueryHistories { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // SavedConnection configuration
        modelBuilder.Entity<SavedConnection>(entity =>
        {
            entity.ToTable("SavedConnections");
            
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.UserId)
                .IsRequired()
                .HasMaxLength(100);
            
            entity.Property(e => e.CompanyId)
                .IsRequired()
                .HasMaxLength(100);
            
            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(200);
            
            entity.Property(e => e.Host)
                .IsRequired()
                .HasMaxLength(500);
            
            entity.Property(e => e.Database)
                .IsRequired()
                .HasMaxLength(200);
            
            entity.Property(e => e.Username)
                .IsRequired()
                .HasMaxLength(100);
            
            entity.Property(e => e.EncryptedPassword)
                .IsRequired()
                .HasMaxLength(500);
            
            entity.Property(e => e.CreatedAt)
                .IsRequired();
            
            entity.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValue(true);
            
            // Indexes
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.UserId, e.CompanyId });
            entity.HasIndex(e => new { e.UserId, e.Name });
        });

        // AnalysisConfig configuration
        modelBuilder.Entity<AnalysisConfig>(entity =>
        {
            entity.ToTable("AnalysisConfigs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ConfigJson).HasColumnType("text");
            entity.Property(e => e.SchemaSummary).HasColumnType("text");
            entity.Property(e => e.TablesJson).HasColumnType("text");
            entity.HasIndex(e => e.ConnectionId);
            entity.HasIndex(e => e.UserId);
        });

        // QueryHistory configuration
        modelBuilder.Entity<QueryHistory>(entity =>
        {
            entity.ToTable("QueryHistories");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Question).HasMaxLength(2000);
            entity.Property(e => e.PyCaretParamsJson).HasColumnType("text");
            entity.Property(e => e.ResultJson).HasColumnType("text");
            entity.Property(e => e.ClarificationQuestion).HasMaxLength(2000);
            entity.HasIndex(e => e.ConfigId);
            entity.HasIndex(e => e.UserId);
            // Zincir geriye dogru yurunuyor (cocuktan ebeveyne) ve her adim
            // ayri bir sorgu: indekssiz her turda tablo taramasi olurdu.
            entity.HasIndex(e => e.ParentQueryId);
        });
    }
}
