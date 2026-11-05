using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class CreditTransaction
{
    public Guid TransactionId { get; set; }

    public Guid CreditAccountId { get; set; }

    public Guid? RequestId { get; set; }

    public string TransactionType { get; set; } = null!;

    public decimal Amount { get; set; }

    public string? IdempotencyKey { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public virtual AppUser? CreatedByNavigation { get; set; }

    public virtual CreditAccount CreditAccount { get; set; } = null!;

    public virtual AiRequest? Request { get; set; }

    public virtual AppUser? UpdatedByNavigation { get; set; }
}
