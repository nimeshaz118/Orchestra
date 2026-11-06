using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class AuditLog
{
    public Guid AuditLogId { get; set; }

    public Guid? ActorUserId { get; set; }

    public string? ActorRoleCode { get; set; }

    public string Action { get; set; } = null!;

    public string TargetType { get; set; } = null!;

    public Guid? TargetId { get; set; }

    public Guid? DepartmentId { get; set; }

    public string? OldValues { get; set; }

    public string? NewValues { get; set; }

    public string Outcome { get; set; } = null!;

    public string? Reason { get; set; }

    public Guid CorrelationId { get; set; }

    public DateTime OccurredAt { get; set; }

    public virtual AppUser? ActorUser { get; set; }

    public virtual Department? Department { get; set; }
}
