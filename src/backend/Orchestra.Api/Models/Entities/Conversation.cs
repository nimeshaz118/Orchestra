using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class Conversation
{
    public Guid ConversationId { get; set; }

    public Guid UserId { get; set; }

    public Guid DepartmentId { get; set; }

    public string? Title { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual ICollection<AiRequest> AiRequests { get; set; } = new List<AiRequest>();

    public virtual Department Department { get; set; } = null!;

    public virtual AppUser User { get; set; } = null!;
}
