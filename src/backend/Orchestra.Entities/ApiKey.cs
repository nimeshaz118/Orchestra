using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class ApiKey
{
    public Guid ApiKeyId { get; set; }

    public Guid UserId { get; set; }

    public string KeyName { get; set; } = null!;

    public string KeyPrefix { get; set; } = null!;

    public string KeyHash { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime? LastUsedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual AppUser User { get; set; } = null!;
}
