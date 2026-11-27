using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class SystemLog
{
    public Guid SystemLogId { get; set; }

    public string EventCode { get; set; } = null!;

    public string Category { get; set; } = null!;

    public string Severity { get; set; } = null!;

    public string SourceService { get; set; } = null!;

    public string Message { get; set; } = null!;

    public Guid? ActorUserId { get; set; }

    public Guid? RequestId { get; set; }

    public Guid CorrelationId { get; set; }

    public string? ErrorCode { get; set; }

    public string Metadata { get; set; } = null!;

    public DateTime OccurredAt { get; set; }

    public virtual AppUser? ActorUser { get; set; }

    public virtual AiRequest? Request { get; set; }
}
