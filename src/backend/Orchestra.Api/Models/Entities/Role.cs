using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class Role
{
    public Guid RoleId { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string Permission { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public virtual AppUser? CreatedByNavigation { get; set; }

    public virtual AppUser? UpdatedByNavigation { get; set; }

    public virtual ICollection<UserRole> UserRole { get; set; } = new List<UserRole>();
}
