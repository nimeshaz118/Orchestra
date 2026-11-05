using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class AiModel
{
    public Guid ModelId { get; set; }

    public Guid ProviderId { get; set; }

    public string ModelName { get; set; } = null!;

    public string? ModelType { get; set; }

    public string? Skills { get; set; }

    public string Status { get; set; } = null!;

    public virtual ICollection<AiRequest> AiRequest { get; set; } = new List<AiRequest>();

    public virtual ICollection<DepartmentModel> DepartmentModel { get; set; } = new List<DepartmentModel>();

    public virtual ICollection<ModelPricing> ModelPricing { get; set; } = new List<ModelPricing>();

    public virtual AiProvider Provider { get; set; } = null!;

    public virtual ICollection<RequestRouting> RequestRouting { get; set; } = new List<RequestRouting>();
}
