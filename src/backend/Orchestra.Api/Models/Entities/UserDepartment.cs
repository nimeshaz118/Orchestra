using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class UserDepartment
{
    public Guid UserId { get; set; }

    public Guid DepartmentId { get; set; }

    public string MembershipRole { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public virtual AppUser? CreatedByNavigation { get; set; }

    public virtual Department Department { get; set; } = null!;

    public virtual AppUser? UpdatedByNavigation { get; set; }

    public virtual AppUser User { get; set; } = null!;
}
