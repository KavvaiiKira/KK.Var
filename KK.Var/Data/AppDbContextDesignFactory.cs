using System.IO;
using KK.Var.Configuration;
using KK.Var.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace KK.Var.Data
{
    public sealed class AppDbContextDesignFactory
        : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile(ApplicationConstants.SettingsConfigurationFileName, optional: true)
                .Build();

            var fileName = configuration[$"{DatabaseOptions.SectionName}:FileName"] ?? ApplicationConstants.DefaultDatabaseFileName;

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={DatabasePaths.GetDatabaseFilePath(fileName)}")
                .Options;

            return new AppDbContext(options);
        }
    }
}
