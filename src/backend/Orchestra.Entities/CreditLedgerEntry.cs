using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class CreditLedgerEntry
{
    public Guid LedgerEntryId { get; set; }

    public Guid CreditAccountId { get; set; }

    public Guid? RequestId { get; set; }

    public Guid? ReservationId { get; set; }

    public string EntryType { get; set; } = null!;

    public decimal Amount { get; set; }

    public decimal AvailableDelta { get; set; }

    public decimal ReservedDelta { get; set; }

    public decimal AvailableBalanceAfter { get; set; }

    public decimal ReservedBalanceAfter { get; set; }

    public string IdempotencyKey { get; set; } = null!;

    public string? Description { get; set; }

    public Guid CorrelationId { get; set; }

    public Guid? PerformedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual CreditAccount CreditAccount { get; set; } = null!;

    public virtual AppUser? PerformedByUser { get; set; }

    public virtual AiRequest? Request { get; set; }
}
