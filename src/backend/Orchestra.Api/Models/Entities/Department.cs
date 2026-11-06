using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class Department
{
    public Guid DepartmentId { get; set; }

    public string DepartmentName { get; set; } = null!;

    public string? Description { get; set; }

    public string Status { get; set; } = null!;

    public string Configuration { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public virtual ICollection<AdditionalCreditRequest> AdditionalCreditRequests { get; set; } = new List<AdditionalCreditRequest>();

    public virtual ICollection<AppUser> AppUsers { get; set; } = new List<AppUser>();

    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();

    public virtual ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();

    public virtual AppUser? CreatedByUser { get; set; }

    public virtual CreditAccount? CreditAccount { get; set; }

    public virtual ICollection<DepartmentModel> DepartmentModels { get; set; } = new List<DepartmentModel>();

    public virtual ModelSelectionPolicy? ModelSelectionPolicy { get; set; }

    public virtual QuotaPolicy? QuotaPolicy { get; set; }

    public virtual AppUser? UpdatedByUser { get; set; }
}
