using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class VerificationCode
{
    public Guid VerificationCodeId { get; set; }

    public Guid UserId { get; set; }

    public string Purpose { get; set; } = null!;

    public string CodeHash { get; set; } = null!;

    public Guid? SecurityStampSnapshot { get; set; }

    public string DeliveryStatus { get; set; } = null!;

    public int FailedAttempts { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? UsedAt { get; set; }

    public DateTime? InvalidatedAt { get; set; }

    public virtual AppUser User { get; set; } = null!;
}
