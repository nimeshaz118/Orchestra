using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class QuotaPolicy
{
    public Guid QuotaPolicyId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid? DepartmentId { get; set; }

    public decimal CreditLimit { get; set; }

    public string Period { get; set; } = null!;

    public decimal? WarningThreshold { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public virtual AppUser? CreatedByNavigation { get; set; }

    public virtual Department? Department { get; set; }

    public virtual Organization Organization { get; set; } = null!;

    public virtual AppUser? UpdatedByNavigation { get; set; }
}
