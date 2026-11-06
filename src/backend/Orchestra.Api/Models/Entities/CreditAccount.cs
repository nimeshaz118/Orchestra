using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class CreditAccount
{
    public Guid CreditAccountId { get; set; }

    public string ScopeType { get; set; } = null!;

    public Guid? DepartmentId { get; set; }

    public decimal AvailableCredits { get; set; }

    public decimal ReservedCredits { get; set; }

    public int Version { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<CreditLedgerEntry> CreditLedgerEntries { get; set; } = new List<CreditLedgerEntry>();

    public virtual Department? Department { get; set; }
}
