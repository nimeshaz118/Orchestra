using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class DepartmentModel
{
    public Guid DepartmentId { get; set; }

    public Guid ModelId { get; set; }

    public bool IsEnabled { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public virtual AppUser? CreatedByNavigation { get; set; }

    public virtual Department Department { get; set; } = null!;

    public virtual AiModel Model { get; set; } = null!;

    public virtual AppUser? UpdatedByNavigation { get; set; }
}
