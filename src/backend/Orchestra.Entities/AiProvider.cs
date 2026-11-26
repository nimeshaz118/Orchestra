using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class AiProvider
{
    public Guid ProviderId { get; set; }

    public string ProviderName { get; set; } = null!;

    public string BaseUrl { get; set; } = null!;

    public string? CredentialReference { get; set; }

    public string Status { get; set; } = null!;

    public int TimeoutSeconds { get; set; }

    public string Configuration { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public virtual ICollection<AiModel> AiModels { get; set; } = new List<AiModel>();

    public virtual AppUser? CreatedByUser { get; set; }

    public virtual AppUser? UpdatedByUser { get; set; }
}
