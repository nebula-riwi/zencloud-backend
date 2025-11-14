using Microsoft.EntityFrameworkCore;
using ZenCloud.Data.Entities;

namespace ZenCloud.Data.DbContext;

public class PgDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    public PgDbContext(DbContextOptions<PgDbContext> options) : base(options)
    {
    }
    public DbSet<User> Users { get; set; }
    public DbSet<Plan> Plans { get; set; }
    public DbSet<Subscription> Subscriptions { get; set; }
    public DbSet<DatabaseEngine> DatabaseEngines { get; set; }
    public DbSet<DatabaseInstance> DatabaseInstances { get; set; }
    public DbSet<Payment> Payments { get; set; }
    public DbSet<EmailLog> EmailLogs { get; set; }
    public DbSet<WebhookConfiguration> WebhookConfigurations { get; set; }
    public DbSet<WebhookLog> WebhookLogs { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<ErrorLog> ErrorLogs { get; set; }
    public DbSet<DatabaseQueryHistory> DatabaseQueryHistory { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLog");

            entity.HasKey(al => al.AuditId);

            entity.Property(al => al.Action)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(al => al.EntityType)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(al => al.OldValue)
                .HasColumnType("text");

            entity.Property(al => al.NewValue)
                .HasColumnType("text");

            entity.Property(al => al.IpAddress)
                .HasMaxLength(50);

            entity.Property(al => al.UserAgent)
                .HasMaxLength(500);

            entity.Property(al => al.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(al => al.User)
                .WithMany(u => u.AuditLogs)
                .HasForeignKey(al => al.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DatabaseEngine>(entity =>
        {
            entity.ToTable("DatabaseEngines");

            entity.HasKey(de => de.EngineId);

            entity.Property(de => de.EngineName)
                .IsRequired()
                .HasMaxLength(100)
                .HasConversion<string>();

            entity.Property(de => de.DefaultPort)
                .IsRequired();

            entity.Property(de => de.IsActive)
                .HasDefaultValue(true);

            entity.Property(de => de.IconUrl)
                .HasMaxLength(500);

            entity.Property(de => de.Description)
                .HasMaxLength(500);

        });

        modelBuilder.Entity<DatabaseInstance>(entity =>
        {
            entity.ToTable("DatabaseInstances");
            entity.HasKey(di => di.InstanceId);

            entity.Property(di => di.DatabaseName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(di => di.DatabaseUser)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(di => di.DatabasePasswordHash)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(di => di.AssignedPort)
                .IsRequired();

            entity.Property(di => di.ConnectionString)
                .HasMaxLength(1000);

            entity.Property(di => di.Status)
                .IsRequired()
                .HasMaxLength(50)
                .HasConversion<int>();

            entity.Property(di => di.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(di => di.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(di => di.ServerIpAddress)
                .HasMaxLength(50);

            entity.HasOne(di => di.User)
                .WithMany(u => u.DatabaseInstances)
                .HasForeignKey(di => di.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(di => di.Engine)
                .WithMany(de => de.DatabaseInstances)
                .HasForeignKey(di => di.EngineId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EmailLog>(entity =>
        {
            entity.ToTable("EmailLogs");

            entity.HasKey(el => el.EmailLogId);

            entity.Property(el => el.EmailType)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(el => el.RecipientEmail)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(el => el.Subject)
                .HasMaxLength(500);

            entity.Property(el => el.Status)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(el => el.ErrorMessage)
                .HasMaxLength(2000);

            entity.Property(el => el.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(el => el.User)
                .WithMany(u => u.EmailLogs)
                .HasForeignKey(el => el.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ErrorLog>(entity =>
        {
            entity.ToTable("ErrorLogs");

            entity.HasKey(el => el.ErrorId);

            entity.Property(el => el.ErrorMessage)
                .IsRequired()
                .HasMaxLength(2000);

            entity.Property(el => el.StackTrace)
                .HasColumnType("text");

            entity.Property(el => el.Source)
                .HasMaxLength(200);

            entity.Property(el => el.RequestPath)
                .HasMaxLength(500);

            entity.Property(el => el.RequestMethod)
                .HasMaxLength(10);

            entity.Property(el => el.IpAddress)
                .HasMaxLength(50);

            entity.Property(el => el.Severity)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(el => el.IsNotified)
                .HasDefaultValue(false);

            entity.Property(el => el.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(el => el.User)
                .WithMany(u => u.ErrorLogs)
                .HasForeignKey(el => el.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("Payments");
            entity.HasKey(p => p.PaymentId);

            entity.Property(p => p.MercadoPagoPaymentId)
                .HasMaxLength(200);

            entity.Property(p => p.Amount)
                .HasColumnType("decimal(18,2)")
                .IsRequired();

            entity.Property(p => p.Currency)
                .IsRequired()
                .HasMaxLength(10)
                .HasDefaultValue("COP");

            entity.Property(p => p.PaymentStatus)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(p => p.PaymentMethod)
                .HasMaxLength(100);

            entity.Property(p => p.PaymentMethodId)
                .HasMaxLength(100);

            entity.Property(p => p.PayerId)
                .HasMaxLength(100);

            entity.Property(p => p.CardId)
                .HasMaxLength(100);

            entity.Property(p => p.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(p => p.User)
                .WithMany(u => u.Payments)
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(p => p.Subscription)
                .WithMany(us => us.Payments)
                .HasForeignKey(p => p.SubscriptionId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        
        modelBuilder.Entity<Plan>(entity =>
        {
            entity.ToTable("Plans");
            entity.HasKey(p => p.PlanId);

            entity.Property(p => p.PlanName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(p => p.MaxDatabasesPerEngine)
                .IsRequired();

            entity.Property(p => p.PriceInCOP)
                .HasColumnType("decimal(18,2)")
                .IsRequired();

            entity.Property(p => p.DurationInDays)
                .IsRequired();

            entity.Property(p => p.IsActive)
                .HasDefaultValue(true);

            entity.Property(p => p.Description)
                .HasMaxLength(500);
        });

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.ToTable("Subscriptions");
            entity.HasKey(us => us.SubscriptionId);

            entity.Property(us => us.IsActive)
                .HasDefaultValue(true);

            entity.Property(us => us.MercadoPagoSubscriptionId)
                .HasMaxLength(200);

            entity.Property(us => us.PaymentStatus)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(us => us.AutoRenewEnabled)
                .HasDefaultValue(false);

            entity.Property(us => us.LastAutoRenewError)
                .HasMaxLength(500);

            entity.Property(us => us.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(us => us.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(us => us.User)
                .WithMany(u => u.Subscriptions)
                .HasForeignKey(us => us.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(us => us.Plan)
                .WithMany(p => p.Subscriptions)
                .HasForeignKey(us => us.PlanId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(u => u.UserId);

            entity.Property(u => u.Email)
                .IsRequired()
                .HasMaxLength(255);

            entity.HasIndex(u => u.Email)
                .IsUnique();

            entity.Property(u => u.PasswordHash)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(u => u.FullName)
                .HasMaxLength(200);

            entity.Property(u => u.IsEmailVerified)
                .HasDefaultValue(false);

            entity.Property(u => u.EmailVerificationToken)
                .HasMaxLength(500);

            entity.Property(u => u.PasswordResetToken)
                .HasMaxLength(500);

            entity.Property(u => u.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(u => u.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(u => u.IsActive)
                .HasDefaultValue(true);
        });

        modelBuilder.Entity<WebhookConfiguration>(entity =>
        {
            entity.ToTable("WebhookConfigurations");
            entity.HasKey(wc => wc.WebhookId);

            entity.Property(wc => wc.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(wc => wc.WebhookUrl)
                .IsRequired()
                .HasMaxLength(1000);

            entity.Property(wc => wc.EventType)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(wc => wc.IsActive)
                .HasDefaultValue(true);

            entity.Property(wc => wc.SecretToken)
                .HasMaxLength(500);

            entity.Property(wc => wc.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(wc => wc.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(wc => wc.User)
                .WithMany(u => u.WebhookConfigurations)
                .HasForeignKey(wc => wc.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DatabaseQueryHistory>(entity =>
        {
            entity.ToTable("DatabaseQueryHistory");
            entity.HasKey(q => q.QueryHistoryId);

            entity.Property(q => q.QueryText)
                .IsRequired()
                .HasColumnType("text");

            entity.Property(q => q.ErrorMessage)
                .HasColumnType("text");

            entity.Property(q => q.ExecutedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(q => q.EngineType)
                .HasConversion<string>()
                .HasMaxLength(50);

            entity.HasOne(q => q.User)
                .WithMany(u => u.QueryHistory)
                .HasForeignKey(q => q.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(q => q.Instance)
                .WithMany(i => i.QueryHistory)
                .HasForeignKey(q => q.InstanceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WebhookLog>(entity =>
        {
            entity.ToTable("WebhookLogs");
            entity.HasKey(wl => wl.WebhookLogId);

            entity.Property(wl => wl.EventType)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(wl => wl.PayloadJson)
                .HasColumnType("text");

            entity.Property(wl => wl.ResponseBody)
                .HasColumnType("text");

            entity.Property(wl => wl.Status)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(wl => wl.AttemptCount)
                .HasDefaultValue(0);

            entity.Property(wl => wl.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(wl => wl.WebhookConfiguration)
                .WithMany(wc => wc.WebhookLogs)
                .HasForeignKey(wl => wl.WebhookId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}