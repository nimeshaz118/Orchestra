using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class VerificationCode
{
    public Guid CodeId { get; set; }

    public Guid UserId { get; set; }

    public string CodeHash { get; set; } = null!;

    public string Purpose { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public DateTime? UsedAt { get; set; }

    public int FailedAttempts { get; set; }

    public string? DeliveryStatus { get; set; }

    public virtual AppUser User { get; set; } = null!;
}
