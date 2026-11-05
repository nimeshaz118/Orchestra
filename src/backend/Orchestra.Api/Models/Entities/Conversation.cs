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

    public Guid? CreatedBy { get; set; }

    public Guid? UpdatedBy { get; set; }

    public virtual ICollection<AiRequest> AiRequest { get; set; } = new List<AiRequest>();

    public virtual AppUser? CreatedByNavigation { get; set; }

    public virtual Department Department { get; set; } = null!;

    public virtual AppUser? UpdatedByNavigation { get; set; }

    public virtual AppUser User { get; set; } = null!;
}
