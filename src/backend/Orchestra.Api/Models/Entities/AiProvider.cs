using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class AiProvider
{
    public Guid ProviderId { get; set; }

    public string Name { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string? BaseUrl { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public virtual ICollection<AiModel> AiModel { get; set; } = new List<AiModel>();

    public virtual AppUser? CreatedByNavigation { get; set; }

    public virtual AppUser? UpdatedByNavigation { get; set; }
}
