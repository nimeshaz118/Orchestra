using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class AiRequest
{
    public Guid RequestId { get; set; }

    public Guid ConversationId { get; set; }

    public Guid? ModelId { get; set; }

    public string Prompt { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime RequestedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public virtual AiResponse? AiResponse { get; set; }

    public virtual Conversation Conversation { get; set; } = null!;

    public virtual AppUser? CreatedByNavigation { get; set; }

    public virtual ICollection<CreditLedgerEntry> CreditLedgerEntry { get; set; } = new List<CreditLedgerEntry>();

    public virtual ICollection<CreditTransaction> CreditTransaction { get; set; } = new List<CreditTransaction>();

    public virtual AiModel? Model { get; set; }

    public virtual ICollection<RequestRouting> RequestRouting { get; set; } = new List<RequestRouting>();

    public virtual ICollection<SystemLog> SystemLog { get; set; } = new List<SystemLog>();

    public virtual AppUser? UpdatedByNavigation { get; set; }

    public virtual ICollection<UsageRecord> UsageRecord { get; set; } = new List<UsageRecord>();
}
