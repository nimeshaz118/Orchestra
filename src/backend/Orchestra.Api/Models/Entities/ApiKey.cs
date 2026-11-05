using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class ApiKey
{
    public Guid ApiKeyId { get; set; }

    public Guid UserId { get; set; }

    public string Name { get; set; } = null!;

    public string KeyHash { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime? ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public virtual AppUser? CreatedByNavigation { get; set; }

    public virtual AppUser? UpdatedByNavigation { get; set; }

    public virtual AppUser User { get; set; } = null!;
}
