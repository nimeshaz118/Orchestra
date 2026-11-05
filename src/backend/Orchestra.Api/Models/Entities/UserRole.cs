using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class UserRole
{
    public Guid UserId { get; set; }

    public Guid RoleId { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public virtual AppUser? CreatedByNavigation { get; set; }

    public virtual Role Role { get; set; } = null!;

    public virtual AppUser? UpdatedByNavigation { get; set; }

    public virtual AppUser User { get; set; } = null!;
}
