using Xunit;

namespace Orchestra.Api.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ORCHESTRA_RUN_POSTGRES_TESTS") != "true")
            Skip = "Set ORCHESTRA_RUN_POSTGRES_TESTS=true to run PostgreSQL integration tests.";
    }
}
