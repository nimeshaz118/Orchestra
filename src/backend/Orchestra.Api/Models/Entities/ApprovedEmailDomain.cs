using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class ApprovedEmailDomain
{
    public Guid DomainId { get; set; }

    public Guid OrganizationId { get; set; }

    public string DomainName { get; set; } = null!;

    public bool IsActive { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual AppUser? CreatedByUser { get; set; }

    public virtual Organization Organization { get; set; } = null!;

    public virtual AppUser? UpdatedByNavigation { get; set; }
}
