using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class UsageRecord
{
    public Guid UsageId { get; set; }

    public Guid RequestId { get; set; }

    public Guid? PricingId { get; set; }

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public decimal MonetaryCost { get; set; }

    public decimal CreditsConsumed { get; set; }

    public bool IsEstimated { get; set; }

    public string SettlementStatus { get; set; } = null!;

    public DateTime RecordedAt { get; set; }

    public DateTime? SettledAt { get; set; }

    public virtual ModelPricing? Pricing { get; set; }

    public virtual AiRequest Request { get; set; } = null!;
}
