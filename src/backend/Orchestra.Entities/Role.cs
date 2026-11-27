using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class Role
{
    public string RoleCode { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public string? Description { get; set; }

    public string Permissions { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual AppUser? AppUser { get; set; }
}
