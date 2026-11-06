using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class ModelSelectionPolicy
{
    public Guid PolicyId { get; set; }

    public Guid? DepartmentId { get; set; }

    public string PolicyName { get; set; } = null!;

    public decimal CostWeight { get; set; }

    public decimal QualityWeight { get; set; }

    public decimal LatencyWeight { get; set; }

    public decimal? MaximumCostPerRequest { get; set; }

    public Guid? FallbackModelId { get; set; }

    public string Rules { get; set; } = null!;

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public virtual AppUser? CreatedByUser { get; set; }

    public virtual Department? Department { get; set; }

    public virtual AiModel? FallbackModel { get; set; }

    public virtual AppUser? UpdatedByUser { get; set; }
}
