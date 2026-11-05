using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class AppUser
{
    public Guid UserId { get; set; }

    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public virtual ICollection<AdditionalCreditRequest> AdditionalCreditRequestCreatedByNavigation { get; set; } = new List<AdditionalCreditRequest>();

    public virtual ICollection<AdditionalCreditRequest> AdditionalCreditRequestRequestedByNavigation { get; set; } = new List<AdditionalCreditRequest>();

    public virtual ICollection<AdditionalCreditRequest> AdditionalCreditRequestReviewedByNavigation { get; set; } = new List<AdditionalCreditRequest>();

    public virtual ICollection<AdditionalCreditRequest> AdditionalCreditRequestUpdatedByNavigation { get; set; } = new List<AdditionalCreditRequest>();

    public virtual ICollection<AiProvider> AiProviderCreatedByNavigation { get; set; } = new List<AiProvider>();

    public virtual ICollection<AiProvider> AiProviderUpdatedByNavigation { get; set; } = new List<AiProvider>();

    public virtual ICollection<AiRequest> AiRequestCreatedByNavigation { get; set; } = new List<AiRequest>();

    public virtual ICollection<AiRequest> AiRequestUpdatedByNavigation { get; set; } = new List<AiRequest>();

    public virtual ICollection<ApiKey> ApiKeyCreatedByNavigation { get; set; } = new List<ApiKey>();

    public virtual ICollection<ApiKey> ApiKeyUpdatedByNavigation { get; set; } = new List<ApiKey>();

    public virtual ICollection<ApiKey> ApiKeyUser { get; set; } = new List<ApiKey>();

    public virtual ICollection<ApprovedEmailDomain> ApprovedEmailDomainCreatedByUser { get; set; } = new List<ApprovedEmailDomain>();

    public virtual ICollection<ApprovedEmailDomain> ApprovedEmailDomainUpdatedByNavigation { get; set; } = new List<ApprovedEmailDomain>();

    public virtual ICollection<AuditLog> AuditLogActorUser { get; set; } = new List<AuditLog>();

    public virtual ICollection<AuditLog> AuditLogCreatedByNavigation { get; set; } = new List<AuditLog>();

    public virtual ICollection<AuditLog> AuditLogUpdatedByNavigation { get; set; } = new List<AuditLog>();

    public virtual ICollection<Conversation> ConversationCreatedByNavigation { get; set; } = new List<Conversation>();

    public virtual ICollection<Conversation> ConversationUpdatedByNavigation { get; set; } = new List<Conversation>();

    public virtual ICollection<Conversation> ConversationUser { get; set; } = new List<Conversation>();

    public virtual AppUser? CreatedByNavigation { get; set; }

    public virtual ICollection<CreditAccount> CreditAccountCreatedByNavigation { get; set; } = new List<CreditAccount>();

    public virtual ICollection<CreditAccount> CreditAccountUpdatedByNavigation { get; set; } = new List<CreditAccount>();

    public virtual ICollection<CreditLedgerEntry> CreditLedgerEntryCreatedByNavigation { get; set; } = new List<CreditLedgerEntry>();

    public virtual ICollection<CreditLedgerEntry> CreditLedgerEntryPerformedByNavigation { get; set; } = new List<CreditLedgerEntry>();

    public virtual ICollection<CreditLedgerEntry> CreditLedgerEntryUpdatedByNavigation { get; set; } = new List<CreditLedgerEntry>();

    public virtual ICollection<CreditTransaction> CreditTransactionCreatedByNavigation { get; set; } = new List<CreditTransaction>();

    public virtual ICollection<CreditTransaction> CreditTransactionUpdatedByNavigation { get; set; } = new List<CreditTransaction>();

    public virtual ICollection<Department> DepartmentCreatedByNavigation { get; set; } = new List<Department>();

    public virtual ICollection<DepartmentModel> DepartmentModelCreatedByNavigation { get; set; } = new List<DepartmentModel>();

    public virtual ICollection<DepartmentModel> DepartmentModelUpdatedByNavigation { get; set; } = new List<DepartmentModel>();

    public virtual ICollection<Department> DepartmentUpdatedByNavigation { get; set; } = new List<Department>();

    public virtual ICollection<AppUser> InverseCreatedByNavigation { get; set; } = new List<AppUser>();

    public virtual ICollection<AppUser> InverseUpdatedByNavigation { get; set; } = new List<AppUser>();

    public virtual ICollection<ModelPricing> ModelPricingCreatedByNavigation { get; set; } = new List<ModelPricing>();

    public virtual ICollection<ModelPricing> ModelPricingUpdatedByNavigation { get; set; } = new List<ModelPricing>();

    public virtual ICollection<Notification> NotificationCreatedByNavigation { get; set; } = new List<Notification>();

    public virtual ICollection<Notification> NotificationUpdatedByNavigation { get; set; } = new List<Notification>();

    public virtual ICollection<Notification> NotificationUser { get; set; } = new List<Notification>();

    public virtual ICollection<QuotaNotification> QuotaNotificationCreatedByNavigation { get; set; } = new List<QuotaNotification>();

    public virtual ICollection<QuotaNotification> QuotaNotificationUpdatedByNavigation { get; set; } = new List<QuotaNotification>();

    public virtual ICollection<QuotaPolicy> QuotaPolicyCreatedByNavigation { get; set; } = new List<QuotaPolicy>();

    public virtual ICollection<QuotaPolicy> QuotaPolicyUpdatedByNavigation { get; set; } = new List<QuotaPolicy>();

    public virtual ICollection<RequestRouting> RequestRoutingCreatedByNavigation { get; set; } = new List<RequestRouting>();

    public virtual ICollection<RequestRouting> RequestRoutingUpdatedByNavigation { get; set; } = new List<RequestRouting>();

    public virtual ICollection<Role> RoleCreatedByNavigation { get; set; } = new List<Role>();

    public virtual ICollection<Role> RoleUpdatedByNavigation { get; set; } = new List<Role>();

    public virtual ICollection<SystemLog> SystemLogActorUser { get; set; } = new List<SystemLog>();

    public virtual ICollection<SystemLog> SystemLogCreatedByNavigation { get; set; } = new List<SystemLog>();

    public virtual ICollection<SystemLog> SystemLogUpdatedByNavigation { get; set; } = new List<SystemLog>();

    public virtual AppUser? UpdatedByNavigation { get; set; }

    public virtual ICollection<UsageRecord> UsageRecordCreatedByNavigation { get; set; } = new List<UsageRecord>();

    public virtual ICollection<UsageRecord> UsageRecordUpdatedByNavigation { get; set; } = new List<UsageRecord>();

    public virtual ICollection<UserDepartment> UserDepartmentCreatedByNavigation { get; set; } = new List<UserDepartment>();

    public virtual ICollection<UserDepartment> UserDepartmentUpdatedByNavigation { get; set; } = new List<UserDepartment>();

    public virtual ICollection<UserDepartment> UserDepartmentUser { get; set; } = new List<UserDepartment>();

    public virtual ICollection<UserRole> UserRoleCreatedByNavigation { get; set; } = new List<UserRole>();

    public virtual ICollection<UserRole> UserRoleUpdatedByNavigation { get; set; } = new List<UserRole>();

    public virtual ICollection<UserRole> UserRoleUser { get; set; } = new List<UserRole>();

    public virtual ICollection<VerificationCode> VerificationCode { get; set; } = new List<VerificationCode>();
}
