using LotamSentinel.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LotamSentinel.Infrastructure.Data;

public class LotamSentinelDbContext : DbContext
{
    public LotamSentinelDbContext(DbContextOptions<LotamSentinelDbContext> options) : base(options) { }

    public DbSet<LotamRelease> LotamReleases => Set<LotamRelease>();
    public DbSet<MaterialCategory> MaterialCategories => Set<MaterialCategory>();
    public DbSet<MaterialSection> MaterialSections => Set<MaterialSection>();
    public DbSet<MaterialCodeRemap> MaterialCodeRemaps => Set<MaterialCodeRemap>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierAlias> SupplierAliases => Set<SupplierAlias>();
    public DbSet<Technology> Technologies => Set<Technology>();
    public DbSet<TechnologyMappingRule> TechnologyMappingRules => Set<TechnologyMappingRule>();
    public DbSet<MaterialSectionTechnology> MaterialSectionTechnologies => Set<MaterialSectionTechnology>();
    public DbSet<StatusKeywordRule> StatusKeywordRules => Set<StatusKeywordRule>();
    public DbSet<SeverityRule> SeverityRules => Set<SeverityRule>();
    public DbSet<LotamEntry> LotamEntries => Set<LotamEntry>();
    public DbSet<ChangeRecord> ChangeRecords => Set<ChangeRecord>();
    public DbSet<SourceEvidence> SourceEvidences => Set<SourceEvidence>();
    public DbSet<AiEnrichmentLog> AiEnrichmentLogs => Set<AiEnrichmentLog>();
    public DbSet<WatchlistSupplier> WatchlistSuppliers => Set<WatchlistSupplier>();
    public DbSet<SecDocumentWatch> SecDocumentWatches => Set<SecDocumentWatch>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<PasswordResetOtp> PasswordResetOtps => Set<PasswordResetOtp>();
    public DbSet<UserNotificationSubscription> UserNotificationSubscriptions => Set<UserNotificationSubscription>();
    public DbSet<SubscriptionRecipient> SubscriptionRecipients => Set<SubscriptionRecipient>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Npgsql's EF Core provider does NOT auto-convert PascalCase C# property
        // names to snake_case columns - schema.sql uses snake_case throughout
        // (unquoted Postgres identifiers are lowercased), so without this,
        // EF Core would look for a quoted "Id" column instead of the real `id`.
        // This runs FIRST so any more specific .HasColumnName(...) call further
        // down in this method (there are a few, e.g. ai_enrichment_log's
        // raw_response) still wins - Fluent API calls are last-write-wins.
        foreach (var entityType in b.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }

        // ---- lotam_release ----
        b.Entity<LotamRelease>(e =>
        {
            e.ToTable("lotam_release");
            e.HasKey(x => x.Id);
            e.Property(x => x.ReleaseDate).HasColumnName("release_date").IsRequired();
            e.Property(x => x.SourceFilename).HasColumnName("source_filename").IsRequired();
            e.Property(x => x.FileSha256).HasColumnName("file_sha256").HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.FileSha256).IsUnique();
            e.HasIndex(x => x.ReleaseDate);
            e.Property(x => x.ProcessingStatus).HasColumnName("processing_status")
                .HasConversion<string>().IsRequired();
        });

        // ---- material_category ----
        b.Entity<MaterialCategory>(e =>
        {
            e.ToTable("material_category");
            e.HasKey(x => x.Id);
            e.Property(x => x.NormalizedName).HasColumnName("normalized_name").IsRequired();
            e.HasIndex(x => x.NormalizedName).IsUnique();
        });

        // ---- material_section ----
        b.Entity<MaterialSection>(e =>
        {
            e.ToTable("material_section");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Release).WithMany(r => r.MaterialSections)
                .HasForeignKey(x => x.ReleaseId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.MaterialCategory).WithMany(c => c.MaterialSections)
                .HasForeignKey(x => x.MaterialCategoryId);
            e.HasIndex(x => new { x.ReleaseId, x.Code }).IsUnique();
            e.Property(x => x.ValidationStatus).HasColumnName("validation_status")
                .HasConversion<string>();
            e.Property(x => x.TechnologyClassificationStatus).HasColumnName("technology_classification_status")
                .HasConversion<string>();
        });

        // ---- material_code_remap ----
        b.Entity<MaterialCodeRemap>(e =>
        {
            e.ToTable("material_code_remap");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.FromMaterialSection).WithMany()
                .HasForeignKey(x => x.FromMaterialSectionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ToMaterialSection).WithMany()
                .HasForeignKey(x => x.ToMaterialSectionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.FromMaterialSectionId, x.ToMaterialSectionId }).IsUnique();
        });

        // ---- supplier / supplier_alias ----
        b.Entity<Supplier>(e =>
        {
            e.ToTable("supplier");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.ParentSupplier).WithMany()
                .HasForeignKey(x => x.ParentSupplierId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<SupplierAlias>(e =>
        {
            e.ToTable("supplier_alias");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Supplier).WithMany(s => s.Aliases)
                .HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.FirstSeenRelease).WithMany()
                .HasForeignKey(x => x.FirstSeenReleaseId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.ReviewStatus).HasColumnName("review_status").HasConversion<string>();
            e.Property(x => x.MatchSource).HasColumnName("match_source").HasConversion<string>();
            e.HasIndex(x => x.SupplierId);
        });

        // ---- technology / mapping rules ----
        b.Entity<Technology>(e =>
        {
            e.ToTable("technology");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
        });

        b.Entity<TechnologyMappingRule>(e =>
        {
            e.ToTable("technology_mapping_rule");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Technology).WithMany(t => t.MappingRules)
                .HasForeignKey(x => x.TechnologyId);
            e.Property(x => x.MatchType).HasColumnName("match_type").HasConversion<string>();
        });

        b.Entity<MaterialSectionTechnology>(e =>
        {
            e.ToTable("material_section_technology");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.MaterialSection).WithMany(m => m.Technologies)
                .HasForeignKey(x => x.MaterialSectionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Technology).WithMany(t => t.MaterialSections)
                .HasForeignKey(x => x.TechnologyId);
            e.HasIndex(x => new { x.MaterialSectionId, x.TechnologyId }).IsUnique();
            e.Property(x => x.Source).HasColumnName("source").HasConversion<string>();
        });

        // ---- status_keyword_rule ----
        b.Entity<StatusKeywordRule>(e =>
        {
            e.ToTable("status_keyword_rule");
            e.HasKey(x => x.Id);
            e.Property(x => x.StatusValue).HasColumnName("status_value").HasConversion<string>();
        });

        // ---- lotam_entry ----
        b.Entity<LotamEntry>(e =>
        {
            e.ToTable("lotam_entry");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.MaterialSection).WithMany(m => m.Entries)
                .HasForeignKey(x => x.MaterialSectionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.SupplierAlias).WithMany(s => s.Entries)
                .HasForeignKey(x => x.SupplierAliasId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.MaterialSectionId, x.OrdinalInSection }).IsUnique();
            e.HasIndex(x => x.SupplierAliasId);
            e.HasIndex(x => x.DerivedStatus);
            e.Property(x => x.DerivedStatus).HasColumnName("derived_status").HasConversion<string>();
            e.Property(x => x.StatusSource).HasColumnName("status_source").HasConversion<string>();
        });

        // ---- severity_rule ----
        b.Entity<SeverityRule>(e =>
        {
            e.ToTable("severity_rule");
            e.HasKey(x => x.Id);
            e.Property(x => x.ChangeType).HasColumnName("change_type").HasConversion<string>();
            e.Property(x => x.Severity).HasColumnName("severity").HasConversion<string>();
            e.Property(x => x.ConditionJson).HasColumnName("condition_json").HasColumnType("jsonb");
            e.HasIndex(x => x.ChangeType);
        });

        // ---- change_record ----
        b.Entity<ChangeRecord>(e =>
        {
            e.ToTable("change_record");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.PreviousRelease).WithMany()
                .HasForeignKey(x => x.PreviousReleaseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CurrentRelease).WithMany()
                .HasForeignKey(x => x.CurrentReleaseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PreviousEntry).WithMany()
                .HasForeignKey(x => x.PreviousEntryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CurrentEntry).WithMany()
                .HasForeignKey(x => x.CurrentEntryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Supplier).WithMany()
                .HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.MaterialSection).WithMany()
                .HasForeignKey(x => x.MaterialSectionId).OnDelete(DeleteBehavior.SetNull);

            e.Property(x => x.ChangeType).HasColumnName("change_type").HasConversion<string>();
            e.Property(x => x.Severity).HasColumnName("severity").HasConversion<string>();
            e.Property(x => x.SeveritySource).HasColumnName("severity_source").HasConversion<string>();
            e.Property(x => x.ReviewStatus).HasColumnName("review_status").HasConversion<string>();

            e.HasIndex(x => new { x.PreviousReleaseId, x.CurrentReleaseId });
            e.HasIndex(x => x.Severity);
            e.HasIndex(x => x.SupplierId);
            e.HasIndex(x => x.ChangeType);
        });

        // ---- source_evidence ----
        b.Entity<SourceEvidence>(e =>
        {
            e.ToTable("source_evidence");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.ChangeRecord).WithMany(c => c.Evidence)
                .HasForeignKey(x => x.ChangeRecordId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Release).WithMany()
                .HasForeignKey(x => x.ReleaseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.LotamEntry).WithMany()
                .HasForeignKey(x => x.LotamEntryId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.EvidenceType).HasColumnName("evidence_type").HasConversion<string>();
        });

        // ---- ai_enrichment_log ----
        b.Entity<AiEnrichmentLog>(e =>
        {
            e.ToTable("ai_enrichment_log");
            e.HasKey(x => x.Id);
            e.Property(x => x.SubjectType).HasColumnName("subject_type").HasConversion<string>();
            e.Property(x => x.RawResponseJson).HasColumnName("raw_response").HasColumnType("jsonb");
            e.HasIndex(x => new { x.SubjectType, x.SubjectId });
        });

        // ---- watchlist_supplier ----
        b.Entity<WatchlistSupplier>(e =>
        {
            e.ToTable("watchlist_supplier");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Supplier).WithMany()
                .HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.SupplierId).IsUnique();
        });

        // ---- sec_document_watch ----
        b.Entity<SecDocumentWatch>(e =>
        {
            e.ToTable("sec_document_watch");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Url).IsUnique();
        });

        // ---- app_user / password_reset_otp / user_notification_subscription ----
        b.Entity<AppUser>(e =>
        {
            e.ToTable("app_user");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Email).IsUnique();
        });

        b.Entity<PasswordResetOtp>(e =>
        {
            e.ToTable("password_reset_otp");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.User).WithMany(u => u.PasswordResetOtps)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.UserId);
        });

        b.Entity<UserNotificationSubscription>(e =>
        {
            e.ToTable("user_notification_subscription");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.User).WithMany(u => u.NotificationSubscriptions)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.SubscriptionType).HasColumnName("subscription_type").HasConversion<string>();
            e.HasIndex(x => new { x.UserId, x.SubscriptionType, x.TargetCode }).IsUnique();
        });

        b.Entity<SubscriptionRecipient>(e =>
        {
            e.ToTable("subscription_recipient");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Subscription).WithMany(s => s.Recipients)
                .HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.SubscriptionId);
        });
    }

    /// <summary>Converts "PageCount" -> "page_count", "Id" -> "id", etc. -
    /// matches the naming used throughout schema.sql exactly.</summary>
    private static string ToSnakeCase(string name)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
