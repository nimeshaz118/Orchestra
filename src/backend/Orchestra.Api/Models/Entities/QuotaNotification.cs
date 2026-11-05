using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class QuotaNotification
{
    public Guid NotificationId { get; set; }

    public Guid CreditAccountId { get; set; }

    public decimal ThresholdPercent { get; set; }

    public string NotificationType { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public virtual AppUser? CreatedByNavigation { get; set; }

    public virtual CreditAccount CreditAccount { get; set; } = null!;

    public virtual AppUser? UpdatedByNavigation { get; set; }
}
