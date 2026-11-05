using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class UsageRecord
{
    public Guid UsageId { get; set; }

    public Guid RequestId { get; set; }

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public decimal EstimatedCost { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public virtual AppUser? CreatedByNavigation { get; set; }

    public virtual AiRequest Request { get; set; } = null!;

    public virtual AppUser? UpdatedByNavigation { get; set; }
}
