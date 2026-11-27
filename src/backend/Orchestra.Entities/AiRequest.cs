using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class AiRequest
{
    public Guid RequestId { get; set; }

    public Guid ConversationId { get; set; }

    public string IdempotencyKey { get; set; } = null!;

    public Guid CorrelationId { get; set; }

    public string PromptText { get; set; } = null!;

    public string? Intent { get; set; }

    public Guid? SelectedModelId { get; set; }

    public string? ProviderRequestId { get; set; }

    public string Status { get; set; } = null!;

    public string? FailureCode { get; set; }

    public string? FailureMessage { get; set; }

    public DateTime RequestedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual AiResponse? AiResponse { get; set; }

    public virtual Conversation Conversation { get; set; } = null!;

    public virtual ICollection<CreditLedgerEntry> CreditLedgerEntries { get; set; } = new List<CreditLedgerEntry>();

    public virtual ICollection<RequestRoutingAttempt> RequestRoutingAttempts { get; set; } = new List<RequestRoutingAttempt>();

    public virtual AiModel? SelectedModel { get; set; }

    public virtual ICollection<SystemLog> SystemLogs { get; set; } = new List<SystemLog>();

    public virtual UsageRecord? UsageRecord { get; set; }
}
