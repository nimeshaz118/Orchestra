using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class AiResponse
{
    public Guid ResponseId { get; set; }

    public Guid RequestId { get; set; }

    public string? ResponseText { get; set; }

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public long? DurationMs { get; set; }

    public virtual AiRequest Request { get; set; } = null!;
}
