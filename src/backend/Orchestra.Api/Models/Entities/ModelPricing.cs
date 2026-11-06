using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class ModelPricing
{
    public Guid PricingId { get; set; }

    public Guid ModelId { get; set; }

    public int VersionNumber { get; set; }

    public string CurrencyCode { get; set; } = null!;

    public decimal InputCostPerMillionTokens { get; set; }

    public decimal OutputCostPerMillionTokens { get; set; }

    public DateTime EffectiveFrom { get; set; }

    public DateTime? EffectiveTo { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public virtual AppUser? CreatedByUser { get; set; }

    public virtual AiModel Model { get; set; } = null!;

    public virtual ICollection<UsageRecord> UsageRecords { get; set; } = new List<UsageRecord>();
}
