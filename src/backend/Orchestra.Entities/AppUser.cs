using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class AppUser
{
    public Guid UserId { get; set; }

    public byte[]? AvatarData { get; set; }

    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string RoleCode { get; set; } = null!;

    public Guid? DepartmentId { get; set; }

    public string FirstName { get; set; } = null!;

    public string LastName { get; set; } = null!;

    public string? DisplayName { get; set; }

    public string? PhoneNumber { get; set; }

    public string? JobTitle { get; set; }

    public string Status { get; set; } = null!;

    public DateTime? EmailVerifiedAt { get; set; }

    public bool MustChangePassword { get; set; }

    public DateTime? PasswordChangedAt { get; set; }

    public Guid SecurityStamp { get; set; }

    public int FailedLoginAttempts { get; set; }

    public DateTime? LockedUntil { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public virtual ICollection<AdditionalCreditRequest> AdditionalCreditRequestRequestedByUsers { get; set; } = new List<AdditionalCreditRequest>();

    public virtual ICollection<AdditionalCreditRequest> AdditionalCreditRequestReviewedByUsers { get; set; } = new List<AdditionalCreditRequest>();

    public virtual ICollection<AiModel> AiModelCreatedByUsers { get; set; } = new List<AiModel>();

    public virtual ICollection<AiModel> AiModelUpdatedByUsers { get; set; } = new List<AiModel>();

    public virtual ICollection<AiProvider> AiProviderCreatedByUsers { get; set; } = new List<AiProvider>();

    public virtual ICollection<AiProvider> AiProviderUpdatedByUsers { get; set; } = new List<AiProvider>();

    public virtual ICollection<ApiKey> ApiKeys { get; set; } = new List<ApiKey>();

    public virtual ICollection<ApprovedEmailDomain> ApprovedEmailDomainCreatedByUsers { get; set; } = new List<ApprovedEmailDomain>();

    public virtual ICollection<ApprovedEmailDomain> ApprovedEmailDomainUpdatedByUsers { get; set; } = new List<ApprovedEmailDomain>();

    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();

    public virtual ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();

    public virtual AppUser? CreatedByUser { get; set; }

    public virtual ICollection<CreditLedgerEntry> CreditLedgerEntries { get; set; } = new List<CreditLedgerEntry>();

    public virtual Department? Department { get; set; }

    public virtual ICollection<Department> DepartmentCreatedByUsers { get; set; } = new List<Department>();

    public virtual ICollection<DepartmentModel> DepartmentModels { get; set; } = new List<DepartmentModel>();

    public virtual ICollection<Department> DepartmentUpdatedByUsers { get; set; } = new List<Department>();

    public virtual ICollection<AppUser> InverseCreatedByUser { get; set; } = new List<AppUser>();

    public virtual ICollection<AppUser> InverseUpdatedByUser { get; set; } = new List<AppUser>();

    public virtual ICollection<ModelPricing> ModelPricings { get; set; } = new List<ModelPricing>();

    public virtual ICollection<ModelSelectionPolicy> ModelSelectionPolicyCreatedByUsers { get; set; } = new List<ModelSelectionPolicy>();

    public virtual ICollection<ModelSelectionPolicy> ModelSelectionPolicyUpdatedByUsers { get; set; } = new List<ModelSelectionPolicy>();

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual ICollection<PlatformSetting> PlatformSettings { get; set; } = new List<PlatformSetting>();

    public virtual ICollection<QuotaPolicy> QuotaPolicyCreatedByUsers { get; set; } = new List<QuotaPolicy>();

    public virtual ICollection<QuotaPolicy> QuotaPolicyUpdatedByUsers { get; set; } = new List<QuotaPolicy>();

    public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public virtual Role RoleCodeNavigation { get; set; } = null!;

    public virtual ICollection<SystemLog> SystemLogs { get; set; } = new List<SystemLog>();

    public virtual AppUser? UpdatedByUser { get; set; }

    public virtual ICollection<VerificationCode> VerificationCodes { get; set; } = new List<VerificationCode>();
}
