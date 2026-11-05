using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class CreditAccount
{
    public Guid CreditAccountId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid? DepartmentId { get; set; }

    public decimal AvailableCredits { get; set; }

    public decimal ReservedCredits { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public virtual AppUser? CreatedByNavigation { get; set; }

    public virtual ICollection<CreditLedgerEntry> CreditLedgerEntry { get; set; } = new List<CreditLedgerEntry>();

    public virtual ICollection<CreditTransaction> CreditTransaction { get; set; } = new List<CreditTransaction>();

    public virtual Department? Department { get; set; }

    public virtual Organization Organization { get; set; } = null!;

    public virtual ICollection<QuotaNotification> QuotaNotification { get; set; } = new List<QuotaNotification>();

    public virtual AppUser? UpdatedByNavigation { get; set; }
}
