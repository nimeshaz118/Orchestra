using AutoMapper;
using Microsoft.Extensions.DependencyInjection;
using Orchestra.Api.Models.Entities;
using Orchestra.Api.Modules.Models;
using Orchestra.Api.Modules.Providers;
using Xunit;

namespace Orchestra.Api.Tests;

public sealed class ApiMappingTests
{
    [Fact]
    public async Task Registered_mappings_match_the_response_contracts()
    {
        await using var host = new ApiHost();
        using var scope = host.Services.CreateScope();
        var mapper = scope.ServiceProvider.GetRequiredService<IMapper>();
        mapper.ConfigurationProvider.AssertConfigurationIsValid();

        var provider = new AiProvider
        {
            ProviderId = Guid.NewGuid(),
            ProviderName = "Example",
            BaseUrl = "https://example.test",
            Status = "ACTIVE",
            Configuration = """{"region":"us"}"""
        };
        var model = new AiModel
        {
            ModelId = Guid.NewGuid(),
            ProviderId = provider.ProviderId,
            Capabilities = """{"streaming":true}"""
        };

        var providerDto = mapper.Map<ProviderDto>(provider);
        var modelDto = mapper.Map<ModelDto>(model);

        Assert.Equal(provider.ProviderId, providerDto.ProviderId);
        Assert.Null(providerDto.CredentialReference);
        Assert.Equal("us", providerDto.Configuration.GetProperty("region").GetString());
        Assert.Equal(model.ModelId, modelDto.Id);
        Assert.Equal(provider.ProviderId, modelDto.ProviderId);
        Assert.Null(modelDto.ContextWindowTokens);
        Assert.True(modelDto.Capabilities.GetProperty("streaming").GetBoolean());
    }
}
