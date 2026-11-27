using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class ApprovedEmailDomain
{
    public Guid DomainId { get; set; }

    public string DomainName { get; set; } = null!;

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public virtual AppUser? CreatedByUser { get; set; }

    public virtual AppUser? UpdatedByUser { get; set; }
}
