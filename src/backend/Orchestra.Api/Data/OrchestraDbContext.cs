using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Orchestra.Api.Models.Entities;

namespace Orchestra.Api.Data;

public partial class OrchestraDbContext : DbContext
{
    public OrchestraDbContext(DbContextOptions<OrchestraDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AdditionalCreditRequest> AdditionalCreditRequest { get; set; }

    public virtual DbSet<AiModel> AiModel { get; set; }

    public virtual DbSet<AiProvider> AiProvider { get; set; }

    public virtual DbSet<AiRequest> AiRequest { get; set; }

    public virtual DbSet<AiResponse> AiResponse { get; set; }

    public virtual DbSet<ApiKey> ApiKey { get; set; }

    public virtual DbSet<AppUser> AppUser { get; set; }

    public virtual DbSet<ApprovedEmailDomain> ApprovedEmailDomain { get; set; }

    public virtual DbSet<AuditLog> AuditLog { get; set; }

    public virtual DbSet<Conversation> Conversation { get; set; }

    public virtual DbSet<CreditAccount> CreditAccount { get; set; }

    public virtual DbSet<CreditLedgerEntry> CreditLedgerEntry { get; set; }

    public virtual DbSet<CreditTransaction> CreditTransaction { get; set; }

    public virtual DbSet<Department> Department { get; set; }

    public virtual DbSet<DepartmentModel> DepartmentModel { get; set; }

    public virtual DbSet<ModelPricing> ModelPricing { get; set; }

    public virtual DbSet<Notification> Notification { get; set; }

    public virtual DbSet<Organization> Organization { get; set; }

    public virtual DbSet<QuotaNotification> QuotaNotification { get; set; }

    public virtual DbSet<QuotaPolicy> QuotaPolicy { get; set; }

    public virtual DbSet<RequestRouting> RequestRouting { get; set; }

    public virtual DbSet<Role> Role { get; set; }

    public virtual DbSet<SystemLog> SystemLog { get; set; }

    public virtual DbSet<UsageRecord> UsageRecord { get; set; }

    public virtual DbSet<UserDepartment> UserDepartment { get; set; }

    public virtual DbSet<UserRole> UserRole { get; set; }

    public virtual DbSet<VerificationCode> VerificationCode { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pgcrypto");

        modelBuilder.Entity<AdditionalCreditRequest>(entity =>
        {
            entity.HasKey(e => e.AdditionalRequestId).HasName("additional_credit_request_pkey");

            entity.ToTable("additional_credit_request", "ai_orchestration");

            entity.HasIndex(e => new { e.Status, e.CreatedAt }, "ix_additional_credit_status");

            entity.Property(e => e.AdditionalRequestId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("additional_request_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.DepartmentId).HasColumnName("department_id");
            entity.Property(e => e.RequestedBy).HasColumnName("requested_by");
            entity.Property(e => e.RequestedCredits)
                .HasPrecision(18, 6)
                .HasColumnName("requested_credits");
            entity.Property(e => e.ReviewedBy).HasColumnName("reviewed_by");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.AdditionalCreditRequestCreatedByNavigation)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("fk_additional_credit_created_by");

            entity.HasOne(d => d.Department).WithMany(p => p.AdditionalCreditRequest)
                .HasForeignKey(d => d.DepartmentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("additional_credit_request_department_id_fkey");

            entity.HasOne(d => d.RequestedByNavigation).WithMany(p => p.AdditionalCreditRequestRequestedByNavigation)
                .HasForeignKey(d => d.RequestedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("additional_credit_request_requested_by_fkey");

            entity.HasOne(d => d.ReviewedByNavigation).WithMany(p => p.AdditionalCreditRequestReviewedByNavigation)
                .HasForeignKey(d => d.ReviewedBy)
                .HasConstraintName("additional_credit_request_reviewed_by_fkey");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.AdditionalCreditRequestUpdatedByNavigation)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("fk_additional_credit_updated_by");
        });

        modelBuilder.Entity<AiModel>(entity =>
        {
            entity.HasKey(e => e.ModelId).HasName("ai_model_pkey");

            entity.ToTable("ai_model", "ai_orchestration");

            entity.HasIndex(e => e.ProviderId, "ix_ai_model_provider");

            entity.HasIndex(e => new { e.ProviderId, e.ModelName }, "uq_model_per_provider").IsUnique();

            entity.Property(e => e.ModelId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("model_id");
            entity.Property(e => e.ModelName)
                .HasMaxLength(150)
                .HasColumnName("model_name");
            entity.Property(e => e.ModelType)
                .HasMaxLength(50)
                .HasColumnName("model_type");
            entity.Property(e => e.ProviderId).HasColumnName("provider_id");
            entity.Property(e => e.Skills)
                .HasColumnType("jsonb")
                .HasColumnName("skills");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasColumnName("status");

            entity.HasOne(d => d.Provider).WithMany(p => p.AiModel)
                .HasForeignKey(d => d.ProviderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("ai_model_provider_id_fkey");
        });

        modelBuilder.Entity<AiProvider>(entity =>
        {
            entity.HasKey(e => e.ProviderId).HasName("ai_provider_pkey");

            entity.ToTable("ai_provider", "ai_orchestration");

            entity.Property(e => e.ProviderId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("provider_id");
            entity.Property(e => e.BaseUrl).HasColumnName("base_url");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.Name)
                .HasMaxLength(150)
                .HasColumnName("name");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.AiProviderCreatedByNavigation)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("fk_ai_provider_created_by");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.AiProviderUpdatedByNavigation)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("fk_ai_provider_updated_by");
        });

        modelBuilder.Entity<AiRequest>(entity =>
        {
            entity.HasKey(e => e.RequestId).HasName("ai_request_pkey");

            entity.ToTable("ai_request", "ai_orchestration");

            entity.HasIndex(e => new { e.ConversationId, e.RequestedAt }, "ix_ai_request_conversation_requested").IsDescending(false, true);

            entity.Property(e => e.RequestId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("request_id");
            entity.Property(e => e.ConversationId).HasColumnName("conversation_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.ModelId).HasColumnName("model_id");
            entity.Property(e => e.Prompt).HasColumnName("prompt");
            entity.Property(e => e.RequestedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("requested_at");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");

            entity.HasOne(d => d.Conversation).WithMany(p => p.AiRequest)
                .HasForeignKey(d => d.ConversationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("ai_request_conversation_id_fkey");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.AiRequestCreatedByNavigation)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("fk_ai_request_created_by");

            entity.HasOne(d => d.Model).WithMany(p => p.AiRequest)
                .HasForeignKey(d => d.ModelId)
                .HasConstraintName("ai_request_model_id_fkey");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.AiRequestUpdatedByNavigation)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("fk_ai_request_updated_by");
        });

        modelBuilder.Entity<AiResponse>(entity =>
        {
            entity.HasKey(e => e.ResponseId).HasName("ai_response_pkey");

            entity.ToTable("ai_response", "ai_orchestration");

            entity.HasIndex(e => e.RequestId, "ai_response_request_id_key").IsUnique();

            entity.Property(e => e.ResponseId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("response_id");
            entity.Property(e => e.DurationMs).HasColumnName("duration_ms");
            entity.Property(e => e.InputTokens).HasColumnName("input_tokens");
            entity.Property(e => e.OutputTokens).HasColumnName("output_tokens");
            entity.Property(e => e.RequestId).HasColumnName("request_id");
            entity.Property(e => e.ResponseText).HasColumnName("response_text");

            entity.HasOne(d => d.Request).WithOne(p => p.AiResponse)
                .HasForeignKey<AiResponse>(d => d.RequestId)
                .HasConstraintName("ai_response_request_id_fkey");
        });

        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.HasKey(e => e.ApiKeyId).HasName("api_key_pkey");

            entity.ToTable("api_key", "ai_orchestration");

            entity.HasIndex(e => new { e.UserId, e.Status }, "ix_api_key_user_status");

            entity.Property(e => e.ApiKeyId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("api_key_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.KeyHash).HasColumnName("key_hash");
            entity.Property(e => e.Name)
                .HasMaxLength(150)
                .HasColumnName("name");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.ApiKeyCreatedByNavigation)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("fk_api_key_created_by");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.ApiKeyUpdatedByNavigation)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("fk_api_key_updated_by");

            entity.HasOne(d => d.User).WithMany(p => p.ApiKeyUser)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("api_key_user_id_fkey");
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("app_user_pkey");

            entity.ToTable("app_user", "ai_orchestration");

            entity.HasIndex(e => e.Email, "app_user_email_key").IsUnique();

            entity.Property(e => e.UserId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("user_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.InverseCreatedByNavigation)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("fk_app_user_created_by");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.InverseUpdatedByNavigation)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("fk_app_user_updated_by");
        });

        modelBuilder.Entity<ApprovedEmailDomain>(entity =>
        {
            entity.HasKey(e => e.DomainId).HasName("approved_email_domain_pkey");

            entity.ToTable("approved_email_domain", "ai_orchestration");

            entity.HasIndex(e => new { e.OrganizationId, e.DomainName }, "uq_approved_domain_per_org").IsUnique();

            entity.Property(e => e.DomainId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("domain_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
            entity.Property(e => e.DomainName)
                .HasMaxLength(255)
                .HasColumnName("domain_name");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.OrganizationId).HasColumnName("organization_id");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");

            entity.HasOne(d => d.CreatedByUser).WithMany(p => p.ApprovedEmailDomainCreatedByUser)
                .HasForeignKey(d => d.CreatedByUserId)
                .HasConstraintName("fk_approved_domain_created_by");

            entity.HasOne(d => d.Organization).WithMany(p => p.ApprovedEmailDomain)
                .HasForeignKey(d => d.OrganizationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("approved_email_domain_organization_id_fkey");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.ApprovedEmailDomainUpdatedByNavigation)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("fk_approved_domain_updated_by");
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.AuditId).HasName("audit_log_pkey");

            entity.ToTable("audit_log", "ai_orchestration");

            entity.HasIndex(e => new { e.ActorUserId, e.OccurredAt }, "ix_audit_log_actor_occurred").IsDescending(false, true);

            entity.HasIndex(e => new { e.TargetType, e.TargetId }, "ix_audit_log_target");

            entity.Property(e => e.AuditId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("audit_id");
            entity.Property(e => e.Action)
                .HasMaxLength(100)
                .HasColumnName("action");
            entity.Property(e => e.ActorRole)
                .HasMaxLength(30)
                .HasColumnName("actor_role");
            entity.Property(e => e.ActorUserId).HasColumnName("actor_user_id");
            entity.Property(e => e.CorrelationId).HasColumnName("correlation_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.DepartmentId).HasColumnName("department_id");
            entity.Property(e => e.NewValues)
                .HasColumnType("jsonb")
                .HasColumnName("new_values");
            entity.Property(e => e.OccurredAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("occurred_at");
            entity.Property(e => e.OldValues)
                .HasColumnType("jsonb")
                .HasColumnName("old_values");
            entity.Property(e => e.Outcome)
                .HasMaxLength(20)
                .HasColumnName("outcome");
            entity.Property(e => e.Reason).HasColumnName("reason");
            entity.Property(e => e.TargetId).HasColumnName("target_id");
            entity.Property(e => e.TargetType)
                .HasMaxLength(100)
                .HasColumnName("target_type");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");

            entity.HasOne(d => d.ActorUser).WithMany(p => p.AuditLogActorUser)
                .HasForeignKey(d => d.ActorUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("audit_log_actor_user_id_fkey");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.AuditLogCreatedByNavigation)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("fk_audit_log_created_by");

            entity.HasOne(d => d.Department).WithMany(p => p.AuditLog)
                .HasForeignKey(d => d.DepartmentId)
                .HasConstraintName("audit_log_department_id_fkey");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.AuditLogUpdatedByNavigation)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("fk_audit_log_updated_by");
        });

        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.HasKey(e => e.ConversationId).HasName("conversation_pkey");

            entity.ToTable("conversation", "ai_orchestration");

            entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "ix_conversation_user_created").IsDescending(false, true);

            entity.Property(e => e.ConversationId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("conversation_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.DepartmentId).HasColumnName("department_id");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.ConversationCreatedByNavigation)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("fk_conversation_created_by");

            entity.HasOne(d => d.Department).WithMany(p => p.Conversation)
                .HasForeignKey(d => d.DepartmentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("conversation_department_id_fkey");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.ConversationUpdatedByNavigation)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("fk_conversation_updated_by");

            entity.HasOne(d => d.User).WithMany(p => p.ConversationUser)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("conversation_user_id_fkey");
        });

        modelBuilder.Entity<CreditAccount>(entity =>
        {
            entity.HasKey(e => e.CreditAccountId).HasName("credit_account_pkey");

            entity.ToTable("credit_account", "ai_orchestration");

            entity.HasIndex(e => new { e.OrganizationId, e.DepartmentId }, "ix_credit_account_org_department");

            entity.Property(e => e.CreditAccountId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("credit_account_id");
            entity.Property(e => e.AvailableCredits)
                .HasPrecision(18, 6)
                .HasColumnName("available_credits");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.DepartmentId).HasColumnName("department_id");
            entity.Property(e => e.OrganizationId).HasColumnName("organization_id");
            entity.Property(e => e.ReservedCredits)
                .HasPrecision(18, 6)
                .HasColumnName("reserved_credits");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.CreditAccountCreatedByNavigation)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("fk_credit_account_created_by");

            entity.HasOne(d => d.Department).WithMany(p => p.CreditAccount)
                .HasForeignKey(d => d.DepartmentId)
                .HasConstraintName("credit_account_department_id_fkey");

            entity.HasOne(d => d.Organization).WithMany(p => p.CreditAccount)
                .HasForeignKey(d => d.OrganizationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("credit_account_organization_id_fkey");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.CreditAccountUpdatedByNavigation)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("fk_credit_account_updated_by");
        });

        modelBuilder.Entity<CreditLedgerEntry>(entity =>
        {
            entity.HasKey(e => e.LedgerEntryId).HasName("credit_ledger_entry_pkey");

            entity.ToTable("credit_ledger_entry", "ai_orchestration");

            entity.HasIndex(e => new { e.CreditAccountId, e.CreatedAt }, "ix_credit_ledger_account_created").IsDescending(false, true);

            entity.Property(e => e.LedgerEntryId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("ledger_entry_id");
            entity.Property(e => e.Amount)
                .HasPrecision(18, 6)
                .HasColumnName("amount");
            entity.Property(e => e.BalanceAfter)
                .HasPrecision(18, 6)
                .HasColumnName("balance_after");
            entity.Property(e => e.CorrelationId).HasColumnName("correlation_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.CreditAccountId).HasColumnName("credit_account_id");
            entity.Property(e => e.DepartmentId).HasColumnName("department_id");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.IdempotencyKey)
                .HasMaxLength(150)
                .HasColumnName("idempotency_key");
            entity.Property(e => e.PerformedBy).HasColumnName("performed_by");
            entity.Property(e => e.ReferenceId).HasColumnName("reference_id");
            entity.Property(e => e.ReferenceType)
                .HasMaxLength(50)
                .HasColumnName("reference_type");
            entity.Property(e => e.RequestId).HasColumnName("request_id");
            entity.Property(e => e.TransactionType)
                .HasMaxLength(30)
                .HasColumnName("transaction_type");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.CreditLedgerEntryCreatedByNavigation)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("fk_credit_ledger_created_by");

            entity.HasOne(d => d.CreditAccount).WithMany(p => p.CreditLedgerEntry)
                .HasForeignKey(d => d.CreditAccountId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("credit_ledger_entry_credit_account_id_fkey");

            entity.HasOne(d => d.Department).WithMany(p => p.CreditLedgerEntry)
                .HasForeignKey(d => d.DepartmentId)
                .HasConstraintName("credit_ledger_entry_department_id_fkey");

            entity.HasOne(d => d.PerformedByNavigation).WithMany(p => p.CreditLedgerEntryPerformedByNavigation)
                .HasForeignKey(d => d.PerformedBy)
                .HasConstraintName("credit_ledger_entry_performed_by_fkey");

            entity.HasOne(d => d.Request).WithMany(p => p.CreditLedgerEntry)
                .HasForeignKey(d => d.RequestId)
                .HasConstraintName("credit_ledger_entry_request_id_fkey");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.CreditLedgerEntryUpdatedByNavigation)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("fk_credit_ledger_updated_by");
        });

        modelBuilder.Entity<CreditTransaction>(entity =>
        {
            entity.HasKey(e => e.TransactionId).HasName("credit_transaction_pkey");

            entity.ToTable("credit_transaction", "ai_orchestration");

            entity.HasIndex(e => e.IdempotencyKey, "credit_transaction_idempotency_key_key").IsUnique();

            entity.Property(e => e.TransactionId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("transaction_id");
            entity.Property(e => e.Amount)
                .HasPrecision(18, 6)
                .HasColumnName("amount");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.CreditAccountId).HasColumnName("credit_account_id");
            entity.Property(e => e.IdempotencyKey)
                .HasMaxLength(150)
                .HasColumnName("idempotency_key");
            entity.Property(e => e.RequestId).HasColumnName("request_id");
            entity.Property(e => e.TransactionType)
                .HasMaxLength(30)
                .HasColumnName("transaction_type");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.CreditTransactionCreatedByNavigation)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("fk_credit_transaction_created_by");

            entity.HasOne(d => d.CreditAccount).WithMany(p => p.CreditTransaction)
                .HasForeignKey(d => d.CreditAccountId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("credit_transaction_credit_account_id_fkey");

            entity.HasOne(d => d.Request).WithMany(p => p.CreditTransaction)
                .HasForeignKey(d => d.RequestId)
                .HasConstraintName("credit_transaction_request_id_fkey");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.CreditTransactionUpdatedByNavigation)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("fk_credit_transaction_updated_by");
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.HasKey(e => e.DepartmentId).HasName("department_pkey");

            entity.ToTable("department", "ai_orchestration");

            entity.HasIndex(e => e.OrganizationId, "ix_department_organization");

            entity.HasIndex(e => new { e.OrganizationId, e.DepartmentName }, "uq_department_name_per_org").IsUnique();

            entity.Property(e => e.DepartmentId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("department_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.DepartmentConfiguration)
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("department_configuration");
            entity.Property(e => e.DepartmentName)
                .HasMaxLength(150)
                .HasColumnName("department_name");
            entity.Property(e => e.DepartmentStatus)
                .HasMaxLength(30)
                .HasColumnName("department_status");
            entity.Property(e => e.OrganizationId).HasColumnName("organization_id");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.DepartmentCreatedByNavigation)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("fk_department_created_by");

            entity.HasOne(d => d.Organization).WithMany(p => p.Department)
                .HasForeignKey(d => d.OrganizationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("department_organization_id_fkey");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.DepartmentUpdatedByNavigation)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("fk_department_updated_by");
        });

        modelBuilder.Entity<DepartmentModel>(entity =>
        {
            entity.HasKey(e => new { e.DepartmentId, e.ModelId }).HasName("department_model_pkey");

            entity.ToTable("department_model", "ai_orchestration");

            entity.Property(e => e.DepartmentId).HasColumnName("department_id");
            entity.Property(e => e.ModelId).HasColumnName("model_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.IsEnabled)
                .HasDefaultValue(true)
                .HasColumnName("is_enabled");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.DepartmentModelCreatedByNavigation)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("fk_department_model_created_by");

            entity.HasOne(d => d.Department).WithMany(p => p.DepartmentModel)
                .HasForeignKey(d => d.DepartmentId)
                .HasConstraintName("department_model_department_id_fkey");

            entity.HasOne(d => d.Model).WithMany(p => p.DepartmentModel)
                .HasForeignKey(d => d.ModelId)
                .HasConstraintName("department_model_model_id_fkey");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.DepartmentModelUpdatedByNavigation)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("fk_department_model_updated_by");
        });

        modelBuilder.Entity<ModelPricing>(entity =>
        {
            entity.HasKey(e => e.PricingId).HasName("model_pricing_pkey");

            entity.ToTable("model_pricing", "ai_orchestration");

            entity.Property(e => e.PricingId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("pricing_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.EffectiveFrom).HasColumnName("effective_from");
            entity.Property(e => e.EffectiveTo).HasColumnName("effective_to");
            entity.Property(e => e.InputPricePerToken)
                .HasPrecision(18, 10)
                .HasColumnName("input_price_per_token");
            entity.Property(e => e.ModelId).HasColumnName("model_id");
            entity.Property(e => e.OutputPricePerToken)
                .HasPrecision(18, 10)
                .HasColumnName("output_price_per_token");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.ModelPricingCreatedByNavigation)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("fk_model_pricing_created_by");

            entity.HasOne(d => d.Model).WithMany(p => p.ModelPricing)
                .HasForeignKey(d => d.ModelId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("model_pricing_model_id_fkey");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.ModelPricingUpdatedByNavigation)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("fk_model_pricing_updated_by");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.NotificationId).HasName("notification_pkey");

            entity.ToTable("notification", "ai_orchestration");

            entity.HasIndex(e => new { e.UserId, e.ReadAt }, "ix_notification_user_read");

            entity.Property(e => e.NotificationId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("notification_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.ReadAt).HasColumnName("read_at");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.NotificationCreatedByNavigation)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("fk_notification_created_by");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.NotificationUpdatedByNavigation)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("fk_notification_updated_by");

            entity.HasOne(d => d.User).WithMany(p => p.NotificationUser)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("notification_user_id_fkey");
        });

        modelBuilder.Entity<Organization>(entity =>
        {
            entity.HasKey(e => e.OrganizationId).HasName("organization_pkey");

            entity.ToTable("organization", "ai_orchestration");

            entity.Property(e => e.OrganizationId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("organization_id");
            entity.Property(e => e.Name)
                .HasMaxLength(150)
                .HasColumnName("name");
        });

        modelBuilder.Entity<QuotaNotification>(entity =>
        {
            entity.HasKey(e => e.NotificationId).HasName("quota_notification_pkey");

            entity.ToTable("quota_notification", "ai_orchestration");

            entity.Property(e => e.NotificationId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("notification_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.CreditAccountId).HasColumnName("credit_account_id");
            entity.Property(e => e.NotificationType)
                .HasMaxLength(50)
                .HasColumnName("notification_type");
            entity.Property(e => e.ThresholdPercent)
                .HasPrecision(5, 2)
                .HasColumnName("threshold_percent");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.QuotaNotificationCreatedByNavigation)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("fk_quota_notification_created_by");

            entity.HasOne(d => d.CreditAccount).WithMany(p => p.QuotaNotification)
                .HasForeignKey(d => d.CreditAccountId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("quota_notification_credit_account_id_fkey");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.QuotaNotificationUpdatedByNavigation)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("fk_quota_notification_updated_by");
        });

        modelBuilder.Entity<QuotaPolicy>(entity =>
        {
            entity.HasKey(e => e.QuotaPolicyId).HasName("quota_policy_pkey");

            entity.ToTable("quota_policy", "ai_orchestration");

            entity.Property(e => e.QuotaPolicyId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("quota_policy_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.CreditLimit)
                .HasPrecision(18, 6)
                .HasColumnName("credit_limit");
            entity.Property(e => e.DepartmentId).HasColumnName("department_id");
            entity.Property(e => e.OrganizationId).HasColumnName("organization_id");
            entity.Property(e => e.Period)
                .HasMaxLength(30)
                .HasColumnName("period");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
            entity.Property(e => e.WarningThreshold)
                .HasPrecision(5, 2)
                .HasColumnName("warning_threshold");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.QuotaPolicyCreatedByNavigation)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("fk_quota_policy_created_by");

            entity.HasOne(d => d.Department).WithMany(p => p.QuotaPolicy)
                .HasForeignKey(d => d.DepartmentId)
                .HasConstraintName("quota_policy_department_id_fkey");

            entity.HasOne(d => d.Organization).WithMany(p => p.QuotaPolicy)
                .HasForeignKey(d => d.OrganizationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("quota_policy_organization_id_fkey");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.QuotaPolicyUpdatedByNavigation)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("fk_quota_policy_updated_by");
        });

        modelBuilder.Entity<RequestRouting>(entity =>
        {
            entity.HasKey(e => e.RoutingId).HasName("request_routing_pkey");

            entity.ToTable("request_routing", "ai_orchestration");

            entity.Property(e => e.RoutingId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("routing_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.ModelId).HasColumnName("model_id");
            entity.Property(e => e.RequestId).HasColumnName("request_id");
            entity.Property(e => e.RoutingReason).HasColumnName("routing_reason");
            entity.Property(e => e.SelectedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("selected_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.RequestRoutingCreatedByNavigation)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("fk_request_routing_created_by");

            entity.HasOne(d => d.Model).WithMany(p => p.RequestRouting)
                .HasForeignKey(d => d.ModelId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("request_routing_model_id_fkey");

            entity.HasOne(d => d.Request).WithMany(p => p.RequestRouting)
                .HasForeignKey(d => d.RequestId)
                .HasConstraintName("request_routing_request_id_fkey");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.RequestRoutingUpdatedByNavigation)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("fk_request_routing_updated_by");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.RoleId).HasName("role_pkey");

            entity.ToTable("role", "ai_orchestration");

            entity.HasIndex(e => e.Name, "role_name_key").IsUnique();

            entity.Property(e => e.RoleId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("role_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.Permission)
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("permission");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.RoleCreatedByNavigation)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("fk_role_created_by");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.RoleUpdatedByNavigation)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("fk_role_updated_by");
        });

        modelBuilder.Entity<SystemLog>(entity =>
        {
            entity.HasKey(e => e.LogId).HasName("system_log_pkey");

            entity.ToTable("system_log", "ai_orchestration");

            entity.HasIndex(e => new { e.LogCategory, e.Severity }, "ix_system_log_category_severity");

            entity.HasIndex(e => e.OccurredAt, "ix_system_log_occurred").IsDescending();

            entity.Property(e => e.LogId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("log_id");
            entity.Property(e => e.Action)
                .HasMaxLength(50)
                .HasColumnName("action");
            entity.Property(e => e.ActorUserId).HasColumnName("actor_user_id");
            entity.Property(e => e.CorrelationId).HasColumnName("correlation_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.EntityId).HasColumnName("entity_id");
            entity.Property(e => e.EntityType)
                .HasMaxLength(100)
                .HasColumnName("entity_type");
            entity.Property(e => e.ErrorCode)
                .HasMaxLength(100)
                .HasColumnName("error_code");
            entity.Property(e => e.EventCode)
                .HasMaxLength(100)
                .HasColumnName("event_code");
            entity.Property(e => e.LogCategory)
                .HasMaxLength(30)
                .HasColumnName("log_category");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .HasColumnName("metadata");
            entity.Property(e => e.OccurredAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("occurred_at");
            entity.Property(e => e.Outcome)
                .HasMaxLength(20)
                .HasColumnName("outcome");
            entity.Property(e => e.RequestId).HasColumnName("request_id");
            entity.Property(e => e.Severity)
                .HasMaxLength(20)
                .HasColumnName("severity");
            entity.Property(e => e.SourceService)
                .HasMaxLength(100)
                .HasColumnName("source_service");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");

            entity.HasOne(d => d.ActorUser).WithMany(p => p.SystemLogActorUser)
                .HasForeignKey(d => d.ActorUserId)
                .HasConstraintName("system_log_actor_user_id_fkey");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.SystemLogCreatedByNavigation)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("fk_system_log_created_by");

            entity.HasOne(d => d.Request).WithMany(p => p.SystemLog)
                .HasForeignKey(d => d.RequestId)
                .HasConstraintName("system_log_request_id_fkey");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.SystemLogUpdatedByNavigation)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("fk_system_log_updated_by");
        });

        modelBuilder.Entity<UsageRecord>(entity =>
        {
            entity.HasKey(e => e.UsageId).HasName("usage_record_pkey");

            entity.ToTable("usage_record", "ai_orchestration");

            entity.HasIndex(e => e.CreatedAt, "ix_usage_record_created").IsDescending();

            entity.Property(e => e.UsageId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("usage_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.EstimatedCost)
                .HasPrecision(18, 8)
                .HasColumnName("estimated_cost");
            entity.Property(e => e.InputTokens).HasColumnName("input_tokens");
            entity.Property(e => e.OutputTokens).HasColumnName("output_tokens");
            entity.Property(e => e.RequestId).HasColumnName("request_id");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.UsageRecordCreatedByNavigation)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("fk_usage_record_created_by");

            entity.HasOne(d => d.Request).WithMany(p => p.UsageRecord)
                .HasForeignKey(d => d.RequestId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("usage_record_request_id_fkey");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.UsageRecordUpdatedByNavigation)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("fk_usage_record_updated_by");
        });

        modelBuilder.Entity<UserDepartment>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.DepartmentId }).HasName("user_department_pkey");

            entity.ToTable("user_department", "ai_orchestration");

            entity.HasIndex(e => e.DepartmentId, "ix_user_department_department");

            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.DepartmentId).HasColumnName("department_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.MembershipRole)
                .HasMaxLength(50)
                .HasColumnName("membership_role");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.UserDepartmentCreatedByNavigation)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("fk_user_department_created_by");

            entity.HasOne(d => d.Department).WithMany(p => p.UserDepartment)
                .HasForeignKey(d => d.DepartmentId)
                .HasConstraintName("user_department_department_id_fkey");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.UserDepartmentUpdatedByNavigation)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("fk_user_department_updated_by");

            entity.HasOne(d => d.User).WithMany(p => p.UserDepartmentUser)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("user_department_user_id_fkey");
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.RoleId }).HasName("user_role_pkey");

            entity.ToTable("user_role", "ai_orchestration");

            entity.HasIndex(e => e.RoleId, "ix_user_role_role");

            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.RoleId).HasColumnName("role_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.UserRoleCreatedByNavigation)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("fk_user_role_created_by");

            entity.HasOne(d => d.Role).WithMany(p => p.UserRole)
                .HasForeignKey(d => d.RoleId)
                .HasConstraintName("user_role_role_id_fkey");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.UserRoleUpdatedByNavigation)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("fk_user_role_updated_by");

            entity.HasOne(d => d.User).WithMany(p => p.UserRoleUser)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("user_role_user_id_fkey");
        });

        modelBuilder.Entity<VerificationCode>(entity =>
        {
            entity.HasKey(e => e.CodeId).HasName("verification_code_pkey");

            entity.ToTable("verification_code", "ai_orchestration");

            entity.HasIndex(e => new { e.UserId, e.ExpiresAt }, "ix_verification_user_expiry");

            entity.Property(e => e.CodeId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("code_id");
            entity.Property(e => e.CodeHash).HasColumnName("code_hash");
            entity.Property(e => e.DeliveryStatus)
                .HasMaxLength(30)
                .HasColumnName("delivery_status");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.FailedAttempts).HasColumnName("failed_attempts");
            entity.Property(e => e.Purpose)
                .HasMaxLength(50)
                .HasColumnName("purpose");
            entity.Property(e => e.UsedAt).HasColumnName("used_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.VerificationCode)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("verification_code_user_id_fkey");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
