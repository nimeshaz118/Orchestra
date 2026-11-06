using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class AdditionalCreditRequest
{
    public Guid AdditionalCreditRequestId { get; set; }

    public Guid DepartmentId { get; set; }

    public Guid RequestedByUserId { get; set; }

    public decimal RequestedCredits { get; set; }

    public string Reason { get; set; } = null!;

    public string Status { get; set; } = null!;

    public Guid? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public string? DecisionNote { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Department Department { get; set; } = null!;

    public virtual AppUser RequestedByUser { get; set; } = null!;

    public virtual AppUser? ReviewedByUser { get; set; }
}
