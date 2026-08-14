using Microsoft.EntityFrameworkCore;
using System.Reflection;
using MongoDB.Driver;
using Grafirio.Identity.Api.Features.Companies;
using Grafirio.Identity.Api.Features.Companies.Documents;
using Grafirio.Identity.Api.Features.Subscriptions;
using Grafirio.Identity.Api.Features.Users;

namespace Grafirio.Identity.Api.Repositories
{
    public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
    {
        public DbSet<Company> Companies { get; set; }
        public DbSet<CompanyDocument> CompanyDocuments { get; set; }
        public DbSet<UserCompanyRole> UserCompanyRoles { get; set; }
        public DbSet<Subscription> Subscriptions { get; set; }

        public static AppDbContext Create(IMongoDatabase database)
        {
            var optionsBuilder =
                new DbContextOptionsBuilder<AppDbContext>().UseMongoDB(database.Client,
                    database.DatabaseNamespace.DatabaseName);

            return new AppDbContext(optionsBuilder.Options);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }
    }
}