using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class ModelPricing
{
    public Guid PricingId { get; set; }

    public Guid ModelId { get; set; }

    public decimal InputPricePerToken { get; set; }

    public decimal OutputPricePerToken { get; set; }

    public DateTime EffectiveFrom { get; set; }

    public DateTime? EffectiveTo { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public virtual AppUser? CreatedByNavigation { get; set; }

    public virtual AiModel Model { get; set; } = null!;

    public virtual AppUser? UpdatedByNavigation { get; set; }
}
