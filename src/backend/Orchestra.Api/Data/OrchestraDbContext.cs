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

    public virtual DbSet<AdditionalCreditRequest> AdditionalCreditRequests { get; set; }

    public virtual DbSet<AiModel> AiModels { get; set; }

    public virtual DbSet<AiProvider> AiProviders { get; set; }

    public virtual DbSet<AiRequest> AiRequests { get; set; }

    public virtual DbSet<AiResponse> AiResponses { get; set; }

    public virtual DbSet<ApiKey> ApiKeys { get; set; }

    public virtual DbSet<AppUser> AppUsers { get; set; }

    public virtual DbSet<ApprovedEmailDomain> ApprovedEmailDomains { get; set; }

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public virtual DbSet<Conversation> Conversations { get; set; }

    public virtual DbSet<CreditAccount> CreditAccounts { get; set; }

    public virtual DbSet<CreditLedgerEntry> CreditLedgerEntries { get; set; }

    public virtual DbSet<Department> Departments { get; set; }

    public virtual DbSet<DepartmentModel> DepartmentModels { get; set; }

    public virtual DbSet<ModelPricing> ModelPricings { get; set; }

    public virtual DbSet<ModelSelectionPolicy> ModelSelectionPolicies { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<OrganizationProfile> OrganizationProfiles { get; set; }

    public virtual DbSet<PlatformSetting> PlatformSettings { get; set; }

    public virtual DbSet<QuotaPolicy> QuotaPolicies { get; set; }

    public virtual DbSet<RefreshToken> RefreshTokens { get; set; }

    public virtual DbSet<RequestRoutingAttempt> RequestRoutingAttempts { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<SystemLog> SystemLogs { get; set; }

    public virtual DbSet<UsageRecord> UsageRecords { get; set; }

    public virtual DbSet<UserPreference> UserPreferences { get; set; }

    public virtual DbSet<VerificationCode> VerificationCodes { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pgcrypto");

        modelBuilder.Entity<AdditionalCreditRequest>(entity =>
        {
            entity.HasKey(e => e.AdditionalCreditRequestId).HasName("additional_credit_request_pkey");

            entity.ToTable("additional_credit_request", "ai_orchestration");

            entity.HasIndex(e => new { e.Status, e.CreatedAt }, "ix_additional_credit_status_created");

            entity.Property(e => e.AdditionalCreditRequestId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("additional_credit_request_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.DecisionNote).HasColumnName("decision_note");
            entity.Property(e => e.DepartmentId).HasColumnName("department_id");
            entity.Property(e => e.Reason).HasColumnName("reason");
            entity.Property(e => e.RequestedByUserId).HasColumnName("requested_by_user_id");
            entity.Property(e => e.RequestedCredits)
                .HasPrecision(20, 8)
                .HasColumnName("requested_credits");
            entity.Property(e => e.ReviewedAt).HasColumnName("reviewed_at");
            entity.Property(e => e.ReviewedByUserId).HasColumnName("reviewed_by_user_id");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'PENDING'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Department).WithMany(p => p.AdditionalCreditRequests)
                .HasForeignKey(d => d.DepartmentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("additional_credit_request_department_id_fkey");

            entity.HasOne(d => d.RequestedByUser).WithMany(p => p.AdditionalCreditRequestRequestedByUsers)
                .HasForeignKey(d => d.RequestedByUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("additional_credit_request_requested_by_user_id_fkey");

            entity.HasOne(d => d.ReviewedByUser).WithMany(p => p.AdditionalCreditRequestReviewedByUsers)
                .HasForeignKey(d => d.ReviewedByUserId)
                .HasConstraintName("additional_credit_request_reviewed_by_user_id_fkey");
        });

        modelBuilder.Entity<AiModel>(entity =>
        {
            entity.HasKey(e => e.ModelId).HasName("ai_model_pkey");

            entity.ToTable("ai_model", "ai_orchestration");

            entity.HasIndex(e => new { e.ProviderId, e.Status }, "ix_ai_model_provider_status");

            entity.HasIndex(e => new { e.ProviderId, e.ProviderModelKey }, "uq_ai_model_provider_key").IsUnique();

            entity.Property(e => e.ModelId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("model_id");
            entity.Property(e => e.Capabilities)
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("capabilities");
            entity.Property(e => e.ContextWindowTokens).HasColumnName("context_window_tokens");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
            entity.Property(e => e.DisplayName)
                .HasMaxLength(150)
                .HasColumnName("display_name");
            entity.Property(e => e.ModelType)
                .HasMaxLength(30)
                .HasDefaultValueSql("'CHAT'::character varying")
                .HasColumnName("model_type");
            entity.Property(e => e.ProviderId).HasColumnName("provider_id");
            entity.Property(e => e.ProviderModelKey)
                .HasMaxLength(200)
                .HasColumnName("provider_model_key");
            entity.Property(e => e.QualityTier)
                .HasDefaultValue((short)1)
                .HasColumnName("quality_tier");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ACTIVE'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.UpdatedByUserId).HasColumnName("updated_by_user_id");

            entity.HasOne(d => d.CreatedByUser).WithMany(p => p.AiModelCreatedByUsers)
                .HasForeignKey(d => d.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("ai_model_created_by_user_id_fkey");

            entity.HasOne(d => d.Provider).WithMany(p => p.AiModels)
                .HasForeignKey(d => d.ProviderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("ai_model_provider_id_fkey");

            entity.HasOne(d => d.UpdatedByUser).WithMany(p => p.AiModelUpdatedByUsers)
                .HasForeignKey(d => d.UpdatedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("ai_model_updated_by_user_id_fkey");
        });

        modelBuilder.Entity<AiProvider>(entity =>
        {
            entity.HasKey(e => e.ProviderId).HasName("ai_provider_pkey");

            entity.ToTable("ai_provider", "ai_orchestration");

            entity.HasIndex(e => e.ProviderName, "ai_provider_provider_name_key").IsUnique();

            entity.Property(e => e.ProviderId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("provider_id");
            entity.Property(e => e.BaseUrl).HasColumnName("base_url");
            entity.Property(e => e.Configuration)
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("configuration");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
            entity.Property(e => e.CredentialReference)
                .HasMaxLength(150)
                .HasColumnName("credential_reference");
            entity.Property(e => e.ProviderName)
                .HasMaxLength(150)
                .HasColumnName("provider_name");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ACTIVE'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.TimeoutSeconds)
                .HasDefaultValue(60)
                .HasColumnName("timeout_seconds");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.UpdatedByUserId).HasColumnName("updated_by_user_id");

            entity.HasOne(d => d.CreatedByUser).WithMany(p => p.AiProviderCreatedByUsers)
                .HasForeignKey(d => d.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("ai_provider_created_by_user_id_fkey");

            entity.HasOne(d => d.UpdatedByUser).WithMany(p => p.AiProviderUpdatedByUsers)
                .HasForeignKey(d => d.UpdatedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("ai_provider_updated_by_user_id_fkey");
        });

        modelBuilder.Entity<AiRequest>(entity =>
        {
            entity.HasKey(e => e.RequestId).HasName("ai_request_pkey");

            entity.ToTable("ai_request", "ai_orchestration");

            entity.HasIndex(e => e.CorrelationId, "ai_request_correlation_id_key").IsUnique();

            entity.HasIndex(e => e.IdempotencyKey, "ai_request_idempotency_key_key").IsUnique();

            entity.HasIndex(e => new { e.ConversationId, e.RequestedAt }, "ix_ai_request_conversation_requested").IsDescending(false, true);

            entity.HasIndex(e => new { e.Status, e.RequestedAt }, "ix_ai_request_status_requested");

            entity.Property(e => e.RequestId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("request_id");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at");
            entity.Property(e => e.ConversationId).HasColumnName("conversation_id");
            entity.Property(e => e.CorrelationId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("correlation_id");
            entity.Property(e => e.FailureCode)
                .HasMaxLength(100)
                .HasColumnName("failure_code");
            entity.Property(e => e.FailureMessage).HasColumnName("failure_message");
            entity.Property(e => e.IdempotencyKey)
                .HasMaxLength(150)
                .HasColumnName("idempotency_key");
            entity.Property(e => e.Intent)
                .HasMaxLength(100)
                .HasColumnName("intent");
            entity.Property(e => e.PromptText).HasColumnName("prompt_text");
            entity.Property(e => e.ProviderRequestId)
                .HasMaxLength(255)
                .HasColumnName("provider_request_id");
            entity.Property(e => e.RequestedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("requested_at");
            entity.Property(e => e.SelectedModelId).HasColumnName("selected_model_id");
            entity.Property(e => e.StartedAt).HasColumnName("started_at");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasDefaultValueSql("'QUEUED'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Conversation).WithMany(p => p.AiRequests)
                .HasForeignKey(d => d.ConversationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("ai_request_conversation_id_fkey");

            entity.HasOne(d => d.SelectedModel).WithMany(p => p.AiRequests)
                .HasForeignKey(d => d.SelectedModelId)
                .HasConstraintName("ai_request_selected_model_id_fkey");
        });

        modelBuilder.Entity<AiResponse>(entity =>
        {
            entity.HasKey(e => e.ResponseId).HasName("ai_response_pkey");

            entity.ToTable("ai_response", "ai_orchestration");

            entity.HasIndex(e => e.RequestId, "ai_response_request_id_key").IsUnique();

            entity.Property(e => e.ResponseId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("response_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.FinishReason)
                .HasMaxLength(50)
                .HasColumnName("finish_reason");
            entity.Property(e => e.RequestId).HasColumnName("request_id");
            entity.Property(e => e.ResponseText).HasColumnName("response_text");

            entity.HasOne(d => d.Request).WithOne(p => p.AiResponse)
                .HasForeignKey<AiResponse>(d => d.RequestId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("ai_response_request_id_fkey");
        });

        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.HasKey(e => e.ApiKeyId).HasName("api_key_pkey");

            entity.ToTable("api_key", "ai_orchestration");

            entity.HasIndex(e => e.KeyHash, "api_key_key_hash_key").IsUnique();

            entity.HasIndex(e => new { e.UserId, e.Status }, "ix_api_key_user_status");

            entity.HasIndex(e => new { e.UserId, e.KeyName }, "uq_api_key_name_per_user").IsUnique();

            entity.Property(e => e.ApiKeyId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("api_key_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.KeyHash)
                .HasMaxLength(64)
                .IsFixedLength()
                .HasColumnName("key_hash");
            entity.Property(e => e.KeyName)
                .HasMaxLength(150)
                .HasColumnName("key_name");
            entity.Property(e => e.KeyPrefix)
                .HasMaxLength(20)
                .HasColumnName("key_prefix");
            entity.Property(e => e.LastUsedAt).HasColumnName("last_used_at");
            entity.Property(e => e.RevokedAt).HasColumnName("revoked_at");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ACTIVE'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.ApiKeys)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("api_key_user_id_fkey");
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("app_user_pkey");

            entity.ToTable("app_user", "ai_orchestration");

            entity.HasIndex(e => new { e.DepartmentId, e.Status }, "ix_app_user_department_status").HasFilter("(deleted_at IS NULL)");

            entity.HasIndex(e => e.RoleCode, "ux_one_active_admin")
                .IsUnique()
                .HasFilter("(((role_code)::text = 'ADMIN'::text) AND ((status)::text = 'ACTIVE'::text) AND (deleted_at IS NULL))");

            entity.HasIndex(e => e.RoleCode, "ux_one_active_super_admin")
                .IsUnique()
                .HasFilter("(((role_code)::text = 'SUPER_ADMIN'::text) AND ((status)::text = 'ACTIVE'::text) AND (deleted_at IS NULL))");

            entity.Property(e => e.UserId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("user_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");
            entity.Property(e => e.DepartmentId).HasColumnName("department_id");
            entity.Property(e => e.DisplayName)
                .HasMaxLength(150)
                .HasColumnName("display_name");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(e => e.EmailVerifiedAt).HasColumnName("email_verified_at");
            entity.Property(e => e.FailedLoginAttempts).HasColumnName("failed_login_attempts");
            entity.Property(e => e.FirstName)
                .HasMaxLength(100)
                .HasColumnName("first_name");
            entity.Property(e => e.JobTitle)
                .HasMaxLength(120)
                .HasColumnName("job_title");
            entity.Property(e => e.LastLoginAt).HasColumnName("last_login_at");
            entity.Property(e => e.LastName)
                .HasMaxLength(100)
                .HasColumnName("last_name");
            entity.Property(e => e.LockedUntil).HasColumnName("locked_until");
            entity.Property(e => e.MustChangePassword).HasColumnName("must_change_password");
            entity.Property(e => e.PasswordChangedAt).HasColumnName("password_changed_at");
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash");
            entity.Property(e => e.PhoneNumber)
                .HasMaxLength(30)
                .HasColumnName("phone_number");
            entity.Property(e => e.RoleCode)
                .HasMaxLength(30)
                .HasColumnName("role_code");
            entity.Property(e => e.SecurityStamp)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("security_stamp");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'PENDING'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.UpdatedByUserId).HasColumnName("updated_by_user_id");

            entity.HasOne(d => d.CreatedByUser).WithMany(p => p.InverseCreatedByUser)
                .HasForeignKey(d => d.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("app_user_created_by_user_id_fkey");

            entity.HasOne(d => d.Department).WithMany(p => p.AppUsers)
                .HasForeignKey(d => d.DepartmentId)
                .HasConstraintName("fk_app_user_department");

            entity.HasOne(d => d.RoleCodeNavigation).WithOne(p => p.AppUser)
                .HasForeignKey<AppUser>(d => d.RoleCode)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("app_user_role_code_fkey");

            entity.HasOne(d => d.UpdatedByUser).WithMany(p => p.InverseUpdatedByUser)
                .HasForeignKey(d => d.UpdatedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("app_user_updated_by_user_id_fkey");
        });

        modelBuilder.Entity<ApprovedEmailDomain>(entity =>
        {
            entity.HasKey(e => e.DomainId).HasName("approved_email_domain_pkey");

            entity.ToTable("approved_email_domain", "ai_orchestration");

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
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.UpdatedByUserId).HasColumnName("updated_by_user_id");

            entity.HasOne(d => d.CreatedByUser).WithMany(p => p.ApprovedEmailDomainCreatedByUsers)
                .HasForeignKey(d => d.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("approved_email_domain_created_by_user_id_fkey");

            entity.HasOne(d => d.UpdatedByUser).WithMany(p => p.ApprovedEmailDomainUpdatedByUsers)
                .HasForeignKey(d => d.UpdatedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("approved_email_domain_updated_by_user_id_fkey");
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.AuditLogId).HasName("audit_log_pkey");

            entity.ToTable("audit_log", "ai_orchestration");

            entity.HasIndex(e => new { e.ActorUserId, e.OccurredAt }, "ix_audit_actor_occurred").IsDescending(false, true);

            entity.HasIndex(e => e.CorrelationId, "ix_audit_correlation");

            entity.HasIndex(e => new { e.TargetType, e.TargetId }, "ix_audit_target");

            entity.Property(e => e.AuditLogId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("audit_log_id");
            entity.Property(e => e.Action)
                .HasMaxLength(100)
                .HasColumnName("action");
            entity.Property(e => e.ActorRoleCode)
                .HasMaxLength(30)
                .HasColumnName("actor_role_code");
            entity.Property(e => e.ActorUserId).HasColumnName("actor_user_id");
            entity.Property(e => e.CorrelationId).HasColumnName("correlation_id");
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

            entity.HasOne(d => d.ActorUser).WithMany(p => p.AuditLogs)
                .HasForeignKey(d => d.ActorUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("audit_log_actor_user_id_fkey");

            entity.HasOne(d => d.Department).WithMany(p => p.AuditLogs)
                .HasForeignKey(d => d.DepartmentId)
                .HasConstraintName("audit_log_department_id_fkey");
        });

        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.HasKey(e => e.ConversationId).HasName("conversation_pkey");

            entity.ToTable("conversation", "ai_orchestration");

            entity.HasIndex(e => new { e.DepartmentId, e.CreatedAt }, "ix_conversation_department_created").IsDescending(false, true);

            entity.HasIndex(e => new { e.UserId, e.UpdatedAt }, "ix_conversation_user_updated")
                .IsDescending(false, true)
                .HasFilter("(deleted_at IS NULL)");

            entity.Property(e => e.ConversationId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("conversation_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");
            entity.Property(e => e.DepartmentId).HasColumnName("department_id");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Department).WithMany(p => p.Conversations)
                .HasForeignKey(d => d.DepartmentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("conversation_department_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.Conversations)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("conversation_user_id_fkey");
        });

        modelBuilder.Entity<CreditAccount>(entity =>
        {
            entity.HasKey(e => e.CreditAccountId).HasName("credit_account_pkey");

            entity.ToTable("credit_account", "ai_orchestration");

            entity.HasIndex(e => e.DepartmentId, "ux_one_credit_account_per_department")
                .IsUnique()
                .HasFilter("((scope_type)::text = 'DEPARTMENT'::text)");

            entity.Property(e => e.CreditAccountId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("credit_account_id");
            entity.Property(e => e.AvailableCredits)
                .HasPrecision(20, 8)
                .HasColumnName("available_credits");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.DepartmentId).HasColumnName("department_id");
            entity.Property(e => e.ReservedCredits)
                .HasPrecision(20, 8)
                .HasColumnName("reserved_credits");
            entity.Property(e => e.ScopeType)
                .HasMaxLength(20)
                .HasColumnName("scope_type");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.Version).HasColumnName("version");

            entity.HasOne(d => d.Department).WithOne(p => p.CreditAccount)
                .HasForeignKey<CreditAccount>(d => d.DepartmentId)
                .HasConstraintName("credit_account_department_id_fkey");
        });

        modelBuilder.Entity<CreditLedgerEntry>(entity =>
        {
            entity.HasKey(e => e.LedgerEntryId).HasName("credit_ledger_entry_pkey");

            entity.ToTable("credit_ledger_entry", "ai_orchestration");

            entity.HasIndex(e => e.IdempotencyKey, "credit_ledger_entry_idempotency_key_key").IsUnique();

            entity.HasIndex(e => new { e.CreditAccountId, e.CreatedAt }, "ix_credit_ledger_account_created").IsDescending(false, true);

            entity.HasIndex(e => e.RequestId, "ix_credit_ledger_request").HasFilter("(request_id IS NOT NULL)");

            entity.Property(e => e.LedgerEntryId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("ledger_entry_id");
            entity.Property(e => e.Amount)
                .HasPrecision(20, 8)
                .HasColumnName("amount");
            entity.Property(e => e.AvailableBalanceAfter)
                .HasPrecision(20, 8)
                .HasColumnName("available_balance_after");
            entity.Property(e => e.AvailableDelta)
                .HasPrecision(20, 8)
                .HasColumnName("available_delta");
            entity.Property(e => e.CorrelationId).HasColumnName("correlation_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreditAccountId).HasColumnName("credit_account_id");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.EntryType)
                .HasMaxLength(30)
                .HasColumnName("entry_type");
            entity.Property(e => e.IdempotencyKey)
                .HasMaxLength(150)
                .HasColumnName("idempotency_key");
            entity.Property(e => e.PerformedByUserId).HasColumnName("performed_by_user_id");
            entity.Property(e => e.RequestId).HasColumnName("request_id");
            entity.Property(e => e.ReservationId).HasColumnName("reservation_id");
            entity.Property(e => e.ReservedBalanceAfter)
                .HasPrecision(20, 8)
                .HasColumnName("reserved_balance_after");
            entity.Property(e => e.ReservedDelta)
                .HasPrecision(20, 8)
                .HasColumnName("reserved_delta");

            entity.HasOne(d => d.CreditAccount).WithMany(p => p.CreditLedgerEntries)
                .HasForeignKey(d => d.CreditAccountId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("credit_ledger_entry_credit_account_id_fkey");

            entity.HasOne(d => d.PerformedByUser).WithMany(p => p.CreditLedgerEntries)
                .HasForeignKey(d => d.PerformedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("credit_ledger_entry_performed_by_user_id_fkey");

            entity.HasOne(d => d.Request).WithMany(p => p.CreditLedgerEntries)
                .HasForeignKey(d => d.RequestId)
                .HasConstraintName("credit_ledger_entry_request_id_fkey");
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.HasKey(e => e.DepartmentId).HasName("department_pkey");

            entity.ToTable("department", "ai_orchestration");

            entity.HasIndex(e => e.DepartmentName, "uq_department_name").IsUnique();

            entity.Property(e => e.DepartmentId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("department_id");
            entity.Property(e => e.Configuration)
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("configuration");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
            entity.Property(e => e.DepartmentName)
                .HasMaxLength(150)
                .HasColumnName("department_name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ACTIVE'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.UpdatedByUserId).HasColumnName("updated_by_user_id");

            entity.HasOne(d => d.CreatedByUser).WithMany(p => p.DepartmentCreatedByUsers)
                .HasForeignKey(d => d.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("department_created_by_user_id_fkey");

            entity.HasOne(d => d.UpdatedByUser).WithMany(p => p.DepartmentUpdatedByUsers)
                .HasForeignKey(d => d.UpdatedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("department_updated_by_user_id_fkey");
        });

        modelBuilder.Entity<DepartmentModel>(entity =>
        {
            entity.HasKey(e => new { e.DepartmentId, e.ModelId }).HasName("department_model_pkey");

            entity.ToTable("department_model", "ai_orchestration");

            entity.HasIndex(e => new { e.DepartmentId, e.IsEnabled }, "ix_department_model_enabled");

            entity.Property(e => e.DepartmentId).HasColumnName("department_id");
            entity.Property(e => e.ModelId).HasColumnName("model_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.IsEnabled)
                .HasDefaultValue(true)
                .HasColumnName("is_enabled");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.UpdatedByUserId).HasColumnName("updated_by_user_id");

            entity.HasOne(d => d.Department).WithMany(p => p.DepartmentModels)
                .HasForeignKey(d => d.DepartmentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("department_model_department_id_fkey");

            entity.HasOne(d => d.Model).WithMany(p => p.DepartmentModels)
                .HasForeignKey(d => d.ModelId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("department_model_model_id_fkey");

            entity.HasOne(d => d.UpdatedByUser).WithMany(p => p.DepartmentModels)
                .HasForeignKey(d => d.UpdatedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("department_model_updated_by_user_id_fkey");
        });

        modelBuilder.Entity<ModelPricing>(entity =>
        {
            entity.HasKey(e => e.PricingId).HasName("model_pricing_pkey");

            entity.ToTable("model_pricing", "ai_orchestration");

            entity.HasIndex(e => new { e.ModelId, e.VersionNumber }, "uq_model_pricing_version").IsUnique();

            entity.HasIndex(e => e.ModelId, "ux_model_pricing_one_current")
                .IsUnique()
                .HasFilter("(effective_to IS NULL)");

            entity.Property(e => e.PricingId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("pricing_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
            entity.Property(e => e.CurrencyCode)
                .HasMaxLength(3)
                .HasDefaultValueSql("'USD'::bpchar")
                .IsFixedLength()
                .HasColumnName("currency_code");
            entity.Property(e => e.EffectiveFrom).HasColumnName("effective_from");
            entity.Property(e => e.EffectiveTo).HasColumnName("effective_to");
            entity.Property(e => e.InputCostPerMillionTokens)
                .HasPrecision(20, 8)
                .HasColumnName("input_cost_per_million_tokens");
            entity.Property(e => e.ModelId).HasColumnName("model_id");
            entity.Property(e => e.OutputCostPerMillionTokens)
                .HasPrecision(20, 8)
                .HasColumnName("output_cost_per_million_tokens");
            entity.Property(e => e.VersionNumber).HasColumnName("version_number");

            entity.HasOne(d => d.CreatedByUser).WithMany(p => p.ModelPricings)
                .HasForeignKey(d => d.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("model_pricing_created_by_user_id_fkey");

            entity.HasOne(d => d.Model).WithOne(p => p.ModelPricing)
                .HasForeignKey<ModelPricing>(d => d.ModelId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("model_pricing_model_id_fkey");
        });

        modelBuilder.Entity<ModelSelectionPolicy>(entity =>
        {
            entity.HasKey(e => e.PolicyId).HasName("model_selection_policy_pkey");

            entity.ToTable("model_selection_policy", "ai_orchestration");

            entity.HasIndex(e => e.DepartmentId, "ux_one_active_department_selection_policy")
                .IsUnique()
                .HasFilter("((department_id IS NOT NULL) AND (is_active = true))");

            entity.Property(e => e.PolicyId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("policy_id");
            entity.Property(e => e.CostWeight)
                .HasPrecision(5, 4)
                .HasDefaultValue(0.4000m)
                .HasColumnName("cost_weight");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
            entity.Property(e => e.DepartmentId).HasColumnName("department_id");
            entity.Property(e => e.FallbackModelId).HasColumnName("fallback_model_id");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.LatencyWeight)
                .HasPrecision(5, 4)
                .HasDefaultValue(0.2000m)
                .HasColumnName("latency_weight");
            entity.Property(e => e.MaximumCostPerRequest)
                .HasPrecision(20, 8)
                .HasColumnName("maximum_cost_per_request");
            entity.Property(e => e.PolicyName)
                .HasMaxLength(150)
                .HasColumnName("policy_name");
            entity.Property(e => e.QualityWeight)
                .HasPrecision(5, 4)
                .HasDefaultValue(0.4000m)
                .HasColumnName("quality_weight");
            entity.Property(e => e.Rules)
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("rules");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.UpdatedByUserId).HasColumnName("updated_by_user_id");

            entity.HasOne(d => d.CreatedByUser).WithMany(p => p.ModelSelectionPolicyCreatedByUsers)
                .HasForeignKey(d => d.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("model_selection_policy_created_by_user_id_fkey");

            entity.HasOne(d => d.Department).WithOne(p => p.ModelSelectionPolicy)
                .HasForeignKey<ModelSelectionPolicy>(d => d.DepartmentId)
                .HasConstraintName("model_selection_policy_department_id_fkey");

            entity.HasOne(d => d.FallbackModel).WithMany(p => p.ModelSelectionPolicies)
                .HasForeignKey(d => d.FallbackModelId)
                .HasConstraintName("model_selection_policy_fallback_model_id_fkey");

            entity.HasOne(d => d.UpdatedByUser).WithMany(p => p.ModelSelectionPolicyUpdatedByUsers)
                .HasForeignKey(d => d.UpdatedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("model_selection_policy_updated_by_user_id_fkey");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.NotificationId).HasName("notification_pkey");

            entity.ToTable("notification", "ai_orchestration");

            entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "ix_notification_user_unread")
                .IsDescending(false, true)
                .HasFilter("(read_at IS NULL)");

            entity.HasIndex(e => e.DeduplicationKey, "notification_deduplication_key_key").IsUnique();

            entity.Property(e => e.NotificationId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("notification_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.Data)
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("data");
            entity.Property(e => e.DeduplicationKey)
                .HasMaxLength(180)
                .HasColumnName("deduplication_key");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.NotificationType)
                .HasMaxLength(50)
                .HasColumnName("notification_type");
            entity.Property(e => e.ReadAt).HasColumnName("read_at");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("notification_user_id_fkey");
        });

        modelBuilder.Entity<OrganizationProfile>(entity =>
        {
            entity.HasKey(e => e.OrganizationId).HasName("organization_profile_pkey");

            entity.ToTable("organization_profile", "ai_orchestration");

            entity.Property(e => e.OrganizationId)
                .HasDefaultValue((short)1)
                .HasColumnName("organization_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.OrganizationName)
                .HasMaxLength(150)
                .HasColumnName("organization_name");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ACTIVE'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.Timezone)
                .HasMaxLength(80)
                .HasDefaultValueSql("'Asia/Colombo'::character varying")
                .HasColumnName("timezone");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<PlatformSetting>(entity =>
        {
            entity.HasKey(e => e.SettingKey).HasName("platform_setting_pkey");

            entity.ToTable("platform_setting", "ai_orchestration");

            entity.Property(e => e.SettingKey)
                .HasMaxLength(120)
                .HasColumnName("setting_key");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.IsSensitive).HasColumnName("is_sensitive");
            entity.Property(e => e.SettingValue)
                .HasColumnType("jsonb")
                .HasColumnName("setting_value");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.UpdatedByUserId).HasColumnName("updated_by_user_id");

            entity.HasOne(d => d.UpdatedByUser).WithMany(p => p.PlatformSettings)
                .HasForeignKey(d => d.UpdatedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("platform_setting_updated_by_user_id_fkey");
        });

        modelBuilder.Entity<QuotaPolicy>(entity =>
        {
            entity.HasKey(e => e.QuotaPolicyId).HasName("quota_policy_pkey");

            entity.ToTable("quota_policy", "ai_orchestration");

            entity.HasIndex(e => e.DepartmentId, "ux_one_active_department_quota")
                .IsUnique()
                .HasFilter("(((scope_type)::text = 'DEPARTMENT'::text) AND (is_active = true))");

            entity.Property(e => e.QuotaPolicyId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("quota_policy_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
            entity.Property(e => e.CreditLimit)
                .HasPrecision(20, 8)
                .HasColumnName("credit_limit");
            entity.Property(e => e.CriticalThresholdPercent)
                .HasPrecision(5, 2)
                .HasDefaultValue(95.00m)
                .HasColumnName("critical_threshold_percent");
            entity.Property(e => e.DepartmentId).HasColumnName("department_id");
            entity.Property(e => e.EffectiveFrom)
                .HasDefaultValueSql("now()")
                .HasColumnName("effective_from");
            entity.Property(e => e.EffectiveTo).HasColumnName("effective_to");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.PeriodType)
                .HasMaxLength(20)
                .HasDefaultValueSql("'MONTHLY'::character varying")
                .HasColumnName("period_type");
            entity.Property(e => e.ScopeType)
                .HasMaxLength(20)
                .HasColumnName("scope_type");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.UpdatedByUserId).HasColumnName("updated_by_user_id");
            entity.Property(e => e.WarningThresholdPercent)
                .HasPrecision(5, 2)
                .HasDefaultValue(80.00m)
                .HasColumnName("warning_threshold_percent");

            entity.HasOne(d => d.CreatedByUser).WithMany(p => p.QuotaPolicyCreatedByUsers)
                .HasForeignKey(d => d.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("quota_policy_created_by_user_id_fkey");

            entity.HasOne(d => d.Department).WithOne(p => p.QuotaPolicy)
                .HasForeignKey<QuotaPolicy>(d => d.DepartmentId)
                .HasConstraintName("quota_policy_department_id_fkey");

            entity.HasOne(d => d.UpdatedByUser).WithMany(p => p.QuotaPolicyUpdatedByUsers)
                .HasForeignKey(d => d.UpdatedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("quota_policy_updated_by_user_id_fkey");
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.RefreshTokenId).HasName("refresh_token_pkey");

            entity.ToTable("refresh_token", "ai_orchestration");

            entity.HasIndex(e => e.ExpiresAt, "ix_refresh_token_expiry").HasFilter("(revoked_at IS NULL)");

            entity.HasIndex(e => new { e.UserId, e.FamilyId }, "ix_refresh_token_user_family");

            entity.HasIndex(e => e.TokenHash, "refresh_token_token_hash_key").IsUnique();

            entity.HasIndex(e => new { e.RefreshTokenId, e.UserId, e.FamilyId }, "uq_refresh_token_identity_family").IsUnique();

            entity.Property(e => e.RefreshTokenId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("refresh_token_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedByIp).HasColumnName("created_by_ip");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.FamilyId).HasColumnName("family_id");
            entity.Property(e => e.LastUsedAt).HasColumnName("last_used_at");
            entity.Property(e => e.ParentTokenId).HasColumnName("parent_token_id");
            entity.Property(e => e.ReplacedByTokenId).HasColumnName("replaced_by_token_id");
            entity.Property(e => e.RevokedAt).HasColumnName("revoked_at");
            entity.Property(e => e.RevokedReason)
                .HasMaxLength(150)
                .HasColumnName("revoked_reason");
            entity.Property(e => e.TokenHash)
                .HasMaxLength(64)
                .IsFixedLength()
                .HasColumnName("token_hash");
            entity.Property(e => e.UserAgent).HasColumnName("user_agent");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.RefreshTokens)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("refresh_token_user_id_fkey");

            entity.HasOne(d => d.RefreshTokenNavigation).WithMany(p => p.InverseRefreshTokenNavigation)
                .HasPrincipalKey(p => new { p.RefreshTokenId, p.UserId, p.FamilyId })
                .HasForeignKey(d => new { d.ParentTokenId, d.UserId, d.FamilyId })
                .HasConstraintName("fk_refresh_token_parent_same_user_family");

            entity.HasOne(d => d.RefreshToken1).WithMany(p => p.InverseRefreshToken1)
                .HasPrincipalKey(p => new { p.RefreshTokenId, p.UserId, p.FamilyId })
                .HasForeignKey(d => new { d.ReplacedByTokenId, d.UserId, d.FamilyId })
                .HasConstraintName("fk_refresh_token_replacement_same_user_family");
        });

        modelBuilder.Entity<RequestRoutingAttempt>(entity =>
        {
            entity.HasKey(e => e.RoutingAttemptId).HasName("request_routing_attempt_pkey");

            entity.ToTable("request_routing_attempt", "ai_orchestration");

            entity.HasIndex(e => new { e.RequestId, e.AttemptNumber }, "ix_routing_attempt_request");

            entity.HasIndex(e => new { e.RequestId, e.AttemptNumber }, "uq_routing_attempt").IsUnique();

            entity.Property(e => e.RoutingAttemptId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("routing_attempt_id");
            entity.Property(e => e.AttemptNumber).HasColumnName("attempt_number");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at");
            entity.Property(e => e.ErrorCode)
                .HasMaxLength(100)
                .HasColumnName("error_code");
            entity.Property(e => e.ModelId).HasColumnName("model_id");
            entity.Property(e => e.ProviderRequestId)
                .HasMaxLength(255)
                .HasColumnName("provider_request_id");
            entity.Property(e => e.RequestId).HasColumnName("request_id");
            entity.Property(e => e.RoutingReason).HasColumnName("routing_reason");
            entity.Property(e => e.StartedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("started_at");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasColumnName("status");

            entity.HasOne(d => d.Model).WithMany(p => p.RequestRoutingAttempts)
                .HasForeignKey(d => d.ModelId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("request_routing_attempt_model_id_fkey");

            entity.HasOne(d => d.Request).WithMany(p => p.RequestRoutingAttempts)
                .HasForeignKey(d => d.RequestId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("request_routing_attempt_request_id_fkey");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.RoleCode).HasName("role_pkey");

            entity.ToTable("role", "ai_orchestration");

            entity.HasIndex(e => e.DisplayName, "role_display_name_key").IsUnique();

            entity.Property(e => e.RoleCode)
                .HasMaxLength(30)
                .HasColumnName("role_code");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.DisplayName)
                .HasMaxLength(100)
                .HasColumnName("display_name");
            entity.Property(e => e.Permissions)
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("permissions");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<SystemLog>(entity =>
        {
            entity.HasKey(e => e.SystemLogId).HasName("system_log_pkey");

            entity.ToTable("system_log", "ai_orchestration");

            entity.HasIndex(e => e.CorrelationId, "ix_system_log_correlation");

            entity.HasIndex(e => e.OccurredAt, "ix_system_log_occurred").IsDescending();

            entity.Property(e => e.SystemLogId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("system_log_id");
            entity.Property(e => e.ActorUserId).HasColumnName("actor_user_id");
            entity.Property(e => e.Category)
                .HasMaxLength(50)
                .HasColumnName("category");
            entity.Property(e => e.CorrelationId).HasColumnName("correlation_id");
            entity.Property(e => e.ErrorCode)
                .HasMaxLength(100)
                .HasColumnName("error_code");
            entity.Property(e => e.EventCode)
                .HasMaxLength(100)
                .HasColumnName("event_code");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.Metadata)
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("metadata");
            entity.Property(e => e.OccurredAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("occurred_at");
            entity.Property(e => e.RequestId).HasColumnName("request_id");
            entity.Property(e => e.Severity)
                .HasMaxLength(20)
                .HasColumnName("severity");
            entity.Property(e => e.SourceService)
                .HasMaxLength(100)
                .HasColumnName("source_service");

            entity.HasOne(d => d.ActorUser).WithMany(p => p.SystemLogs)
                .HasForeignKey(d => d.ActorUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("system_log_actor_user_id_fkey");

            entity.HasOne(d => d.Request).WithMany(p => p.SystemLogs)
                .HasForeignKey(d => d.RequestId)
                .HasConstraintName("system_log_request_id_fkey");
        });

        modelBuilder.Entity<UsageRecord>(entity =>
        {
            entity.HasKey(e => e.UsageId).HasName("usage_record_pkey");

            entity.ToTable("usage_record", "ai_orchestration");

            entity.HasIndex(e => e.RecordedAt, "ix_usage_record_recorded").IsDescending();

            entity.HasIndex(e => e.RequestId, "usage_record_request_id_key").IsUnique();

            entity.Property(e => e.UsageId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("usage_id");
            entity.Property(e => e.CreditsConsumed)
                .HasPrecision(20, 8)
                .HasColumnName("credits_consumed");
            entity.Property(e => e.InputTokens).HasColumnName("input_tokens");
            entity.Property(e => e.IsEstimated).HasColumnName("is_estimated");
            entity.Property(e => e.MonetaryCost)
                .HasPrecision(20, 10)
                .HasColumnName("monetary_cost");
            entity.Property(e => e.OutputTokens).HasColumnName("output_tokens");
            entity.Property(e => e.PricingId).HasColumnName("pricing_id");
            entity.Property(e => e.RecordedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("recorded_at");
            entity.Property(e => e.RequestId).HasColumnName("request_id");
            entity.Property(e => e.SettledAt).HasColumnName("settled_at");
            entity.Property(e => e.SettlementStatus)
                .HasMaxLength(30)
                .HasDefaultValueSql("'PENDING'::character varying")
                .HasColumnName("settlement_status");

            entity.HasOne(d => d.Pricing).WithMany(p => p.UsageRecords)
                .HasForeignKey(d => d.PricingId)
                .HasConstraintName("usage_record_pricing_id_fkey");

            entity.HasOne(d => d.Request).WithOne(p => p.UsageRecord)
                .HasForeignKey<UsageRecord>(d => d.RequestId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("usage_record_request_id_fkey");
        });

        modelBuilder.Entity<VerificationCode>(entity =>
        {
            entity.HasKey(e => e.VerificationCodeId).HasName("verification_code_pkey");

            entity.ToTable("verification_code", "ai_orchestration");

            entity.HasIndex(e => new { e.UserId, e.Purpose, e.ExpiresAt }, "ix_verification_user_purpose_expiry").IsDescending(false, false, true);

            entity.Property(e => e.VerificationCodeId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("verification_code_id");
            entity.Property(e => e.CodeHash).HasColumnName("code_hash");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.DeliveryStatus)
                .HasMaxLength(20)
                .HasDefaultValueSql("'PENDING'::character varying")
                .HasColumnName("delivery_status");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.FailedAttempts).HasColumnName("failed_attempts");
            entity.Property(e => e.InvalidatedAt).HasColumnName("invalidated_at");
            entity.Property(e => e.Purpose)
                .HasMaxLength(30)
                .HasColumnName("purpose");
            entity.Property(e => e.SecurityStampSnapshot).HasColumnName("security_stamp_snapshot");
            entity.Property(e => e.UsedAt).HasColumnName("used_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.VerificationCodes)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("verification_code_user_id_fkey");
        });
        
        modelBuilder.Entity<UserPreference>(entity =>
        {
            entity.HasKey(e => e.PreferenceId);

            entity.ToTable("user_preference", "ai_orchestration");

            entity.HasIndex(e => new { e.UserId, e.PreferenceKey })
                .IsUnique();

            entity.Property(e => e.PreferenceId)
                .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(e => e.PreferenceKey)
                .HasMaxLength(100);

            entity.Property(e => e.PreferenceValue)
                .HasColumnType("jsonb");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()");

            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()");

            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
