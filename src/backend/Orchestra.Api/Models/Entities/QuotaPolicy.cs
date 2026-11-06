using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class QuotaPolicy
{
    public Guid QuotaPolicyId { get; set; }

    public string ScopeType { get; set; } = null!;

    public Guid? DepartmentId { get; set; }

    public decimal CreditLimit { get; set; }

    public string PeriodType { get; set; } = null!;

    public decimal WarningThresholdPercent { get; set; }

    public decimal CriticalThresholdPercent { get; set; }

    public bool IsActive { get; set; }

    public DateTime EffectiveFrom { get; set; }

    public DateTime? EffectiveTo { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public virtual AppUser? CreatedByUser { get; set; }

    public virtual Department? Department { get; set; }

    public virtual AppUser? UpdatedByUser { get; set; }
}
