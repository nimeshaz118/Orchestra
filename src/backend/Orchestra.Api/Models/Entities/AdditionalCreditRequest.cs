using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class AdditionalCreditRequest
{
    public Guid AdditionalRequestId { get; set; }

    public Guid DepartmentId { get; set; }

    public Guid RequestedBy { get; set; }

    public decimal RequestedCredits { get; set; }

    public string Status { get; set; } = null!;

    public Guid? ReviewedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public virtual AppUser? CreatedByNavigation { get; set; }

    public virtual Department Department { get; set; } = null!;

    public virtual AppUser RequestedByNavigation { get; set; } = null!;

    public virtual AppUser? ReviewedByNavigation { get; set; }

    public virtual AppUser? UpdatedByNavigation { get; set; }
}
