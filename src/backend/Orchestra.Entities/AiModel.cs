using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class AiModel
{
    public Guid ModelId { get; set; }

    public Guid ProviderId { get; set; }

    public string ProviderModelKey { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public string ModelType { get; set; } = null!;

    public string Status { get; set; } = null!;

    public int? ContextWindowTokens { get; set; }

    public short QualityTier { get; set; }

    public string Capabilities { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public virtual ICollection<AiRequest> AiRequests { get; set; } = new List<AiRequest>();

    public virtual AppUser? CreatedByUser { get; set; }

    public virtual ICollection<DepartmentModel> DepartmentModels { get; set; } = new List<DepartmentModel>();

    public virtual ModelPricing? ModelPricing { get; set; }

    public virtual ICollection<ModelSelectionPolicy> ModelSelectionPolicies { get; set; } = new List<ModelSelectionPolicy>();

    public virtual AiProvider Provider { get; set; } = null!;

    public virtual ICollection<RequestRoutingAttempt> RequestRoutingAttempts { get; set; } = new List<RequestRoutingAttempt>();

    public virtual AppUser? UpdatedByUser { get; set; }
}
