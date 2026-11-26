using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class AiResponse
{
    public Guid ResponseId { get; set; }

    public Guid RequestId { get; set; }

    public string? ResponseText { get; set; }

    public string? FinishReason { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual AiRequest Request { get; set; } = null!;
}
