using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class UserPreference
{
    public Guid PreferenceId { get; set; }

    public Guid UserId { get; set; }

    public string PreferenceKey { get; set; } = null!;

    public string PreferenceValue { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual AppUser User { get; set; } = null!;
}
