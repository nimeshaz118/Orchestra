using Xunit;
using System.Runtime.CompilerServices;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void Configure()
    {
        Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "1");
    }
}
