using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class Department
{
    public Guid DepartmentId { get; set; }

    public string DepartmentName { get; set; } = null!;

    public string DepartmentStatus { get; set; } = null!;

    public string? DepartmentConfiguration { get; set; }

    public Guid OrganizationId { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public virtual ICollection<AdditionalCreditRequest> AdditionalCreditRequest { get; set; } = new List<AdditionalCreditRequest>();

    public virtual ICollection<AuditLog> AuditLog { get; set; } = new List<AuditLog>();

    public virtual ICollection<Conversation> Conversation { get; set; } = new List<Conversation>();

    public virtual AppUser? CreatedByNavigation { get; set; }

    public virtual ICollection<CreditAccount> CreditAccount { get; set; } = new List<CreditAccount>();

    public virtual ICollection<CreditLedgerEntry> CreditLedgerEntry { get; set; } = new List<CreditLedgerEntry>();

    public virtual ICollection<DepartmentModel> DepartmentModel { get; set; } = new List<DepartmentModel>();

    public virtual Organization Organization { get; set; } = null!;

    public virtual ICollection<QuotaPolicy> QuotaPolicy { get; set; } = new List<QuotaPolicy>();

    public virtual AppUser? UpdatedByNavigation { get; set; }

    public virtual ICollection<UserDepartment> UserDepartment { get; set; } = new List<UserDepartment>();
}
