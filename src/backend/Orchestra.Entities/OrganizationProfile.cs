using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class OrganizationProfile
{
    public short OrganizationId { get; set; }

    public string OrganizationName { get; set; } = null!;

    public string Timezone { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
