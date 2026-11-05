using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class SystemLog
{
    public Guid LogId { get; set; }

    public string EventCode { get; set; } = null!;

    public string LogCategory { get; set; } = null!;

    public string Severity { get; set; } = null!;

    public string SourceService { get; set; } = null!;

    public string Message { get; set; } = null!;

    public Guid? ActorUserId { get; set; }

    public string? EntityType { get; set; }

    public Guid? EntityId { get; set; }

    public string? Action { get; set; }

    public string? Outcome { get; set; }

    public Guid CorrelationId { get; set; }

    public Guid? RequestId { get; set; }

    public string? ErrorCode { get; set; }

    public string? Metadata { get; set; }

    public DateTime OccurredAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public virtual AppUser? ActorUser { get; set; }

    public virtual AppUser? CreatedByNavigation { get; set; }

    public virtual AiRequest? Request { get; set; }

    public virtual AppUser? UpdatedByNavigation { get; set; }
}
