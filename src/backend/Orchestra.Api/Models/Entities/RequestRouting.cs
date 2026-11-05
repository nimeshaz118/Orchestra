using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class RequestRouting
{
    public Guid RoutingId { get; set; }

    public Guid RequestId { get; set; }

    public Guid ModelId { get; set; }

    public string? RoutingReason { get; set; }

    public DateTime SelectedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public virtual AppUser? CreatedByNavigation { get; set; }

    public virtual AiModel Model { get; set; } = null!;

    public virtual AiRequest Request { get; set; } = null!;

    public virtual AppUser? UpdatedByNavigation { get; set; }
}
