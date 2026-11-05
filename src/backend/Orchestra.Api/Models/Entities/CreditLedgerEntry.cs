using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class CreditLedgerEntry
{
    public Guid LedgerEntryId { get; set; }

    public Guid CreditAccountId { get; set; }

    public string TransactionType { get; set; } = null!;

    public decimal Amount { get; set; }

    public decimal BalanceAfter { get; set; }

    public string? ReferenceType { get; set; }

    public Guid? ReferenceId { get; set; }

    public string? IdempotencyKey { get; set; }

    public Guid? RequestId { get; set; }

    public string? Description { get; set; }

    public Guid? PerformedBy { get; set; }

    public Guid? DepartmentId { get; set; }

    public Guid CorrelationId { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public virtual AppUser? CreatedByNavigation { get; set; }

    public virtual CreditAccount CreditAccount { get; set; } = null!;

    public virtual Department? Department { get; set; }

    public virtual AppUser? PerformedByNavigation { get; set; }

    public virtual AiRequest? Request { get; set; }

    public virtual AppUser? UpdatedByNavigation { get; set; }
}
