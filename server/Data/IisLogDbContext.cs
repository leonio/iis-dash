using Microsoft.EntityFrameworkCore;
using Server.Data.Entities;

namespace Server.Data;

public sealed class IisLogDbContext : DbContext
{
    public IisLogDbContext(DbContextOptions<IisLogDbContext> options) : base(options)
    {
    }

    public DbSet<LogFile> LogFiles => Set<LogFile>();
    public DbSet<LogRawLine> LogRawLines => Set<LogRawLine>();
    public DbSet<LogEntry> LogEntries => Set<LogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var logFile = modelBuilder.Entity<LogFile>();
        logFile.HasIndex(f => f.FileHash).IsUnique();
        logFile.Property(f => f.FileHash).HasMaxLength(64).IsRequired();
        logFile.Property(f => f.OriginalFileName).HasMaxLength(260).IsRequired();
        logFile.Property(f => f.Format).HasMaxLength(24).IsRequired();

        var raw = modelBuilder.Entity<LogRawLine>();
        raw.Property(r => r.ParseStatus).HasMaxLength(24).IsRequired();
        raw.Property(r => r.ParseError).HasMaxLength(1024);
        raw.Property(r => r.LineText).HasColumnType("nvarchar(max)").IsRequired();
        raw.HasOne(r => r.LogFile)
            .WithMany(f => f.RawLines)
            .HasForeignKey(r => r.LogFileId)
            .OnDelete(DeleteBehavior.Cascade);

        var entry = modelBuilder.Entity<LogEntry>();
        entry.Property(e => e.DateText).HasMaxLength(16);
        entry.Property(e => e.TimeText).HasMaxLength(16);
        entry.Property(e => e.ClientIp).HasMaxLength(64);
        entry.Property(e => e.Username).HasMaxLength(128);
        entry.Property(e => e.Method).HasMaxLength(32);
        entry.Property(e => e.UriStem).HasMaxLength(2048);
        entry.Property(e => e.UriQuery).HasColumnType("nvarchar(max)");
        entry.Property(e => e.UserAgent).HasColumnType("nvarchar(max)");
        entry.Property(e => e.Referer).HasColumnType("nvarchar(max)");
        entry.Property(e => e.Host).HasMaxLength(256);
        entry.Property(e => e.ServerIp).HasMaxLength(64);
        entry.Property(e => e.ProtocolVersion).HasMaxLength(32);
        entry.Property(e => e.SiteName).HasMaxLength(256);
        entry.Property(e => e.ServerName).HasMaxLength(256);
        entry.Property(e => e.ServiceName).HasMaxLength(256);
        entry.Property(e => e.Cookie).HasColumnType("nvarchar(max)");
        entry.HasOne(e => e.LogFile)
            .WithMany(f => f.Entries)
            .HasForeignKey(e => e.LogFileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
