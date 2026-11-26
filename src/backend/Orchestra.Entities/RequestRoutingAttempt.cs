using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class RequestRoutingAttempt
{
    public Guid RoutingAttemptId { get; set; }

    public Guid RequestId { get; set; }

    public int AttemptNumber { get; set; }

    public Guid ModelId { get; set; }

    public string? ProviderRequestId { get; set; }

    public string? RoutingReason { get; set; }

    public string Status { get; set; } = null!;

    public string? ErrorCode { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public virtual AiModel Model { get; set; } = null!;

    public virtual AiRequest Request { get; set; } = null!;
}
