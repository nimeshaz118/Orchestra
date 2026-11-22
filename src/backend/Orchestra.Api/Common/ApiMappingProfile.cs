using System.Text.Json;
using AutoMapper;
using Orchestra.Api.Models.Entities;
using Orchestra.Api.Modules.Auth;
using Orchestra.Api.Modules.Departments;
using Orchestra.Api.Modules.Domains;
using Orchestra.Api.Modules.Models;
using Orchestra.Api.Modules.PlatformSettings;
using Orchestra.Api.Modules.Providers;
using Orchestra.Api.Modules.RoutingPolicies.DTOs;
using Orchestra.Api.Modules.Users;

namespace Orchestra.Api.Common;

public sealed class ApiMappingProfile : Profile
{
    public ApiMappingProfile()
    {
        ShouldUseConstructor = constructor => constructor.IsPublic;

        CreateMap<Department, DepartmentDto>()
            .ForCtorParam(nameof(DepartmentDto.Id), opt => opt.MapFrom(source => source.DepartmentId))
            .ForCtorParam(nameof(DepartmentDto.Name), opt => opt.MapFrom(source => source.DepartmentName));
        CreateMap<ApprovedEmailDomain, DomainDto>();
        CreateMap<RoutingPolicy, RoutingPolicyDto>();

        CreateMap<AiProvider, ProviderDto>()
            .ForCtorParam(nameof(ProviderDto.Configuration), opt => opt.MapFrom(source => ParseJson(source.Configuration)));
        CreateMap<AiModel, ModelDto>()
            .ForCtorParam(nameof(ModelDto.Id), opt => opt.MapFrom(source => source.ModelId))
            .ForCtorParam(nameof(ModelDto.Capabilities), opt => opt.MapFrom(source => ParseJson(source.Capabilities)));
        CreateMap<PlatformSetting, PlatformSettingDto>()
            .ForCtorParam(nameof(PlatformSettingDto.Key), opt => opt.MapFrom(source => source.SettingKey))
            .ForCtorParam(nameof(PlatformSettingDto.Value),
                opt => opt.MapFrom(source => source.IsSensitive ? (JsonElement?)null : ParseJson(source.SettingValue)));

        CreateMap<AppUser, MeDto>()
            .ForCtorParam(nameof(MeDto.Id), opt => opt.MapFrom(source => source.UserId))
            .ForCtorParam(nameof(MeDto.Role), opt => opt.MapFrom(source => source.RoleCode));
        CreateMap<AppUser, UserDto>()
            .ForCtorParam(nameof(UserDto.Id), opt => opt.MapFrom(source => source.UserId))
            .ForCtorParam(nameof(UserDto.Role), opt => opt.MapFrom(source => source.RoleCode));
        CreateMap<MeDto, UserDto>();
        CreateMap<RefreshToken, SessionDto>()
            .ForCtorParam(nameof(SessionDto.Id), opt => opt.MapFrom(source => source.RefreshTokenId))
            .ForCtorParam(nameof(SessionDto.IsCurrent),
                opt => opt.MapFrom((source, context) => context.State is Guid currentSessionId &&
                    source.RefreshTokenId == currentSessionId));
    }

    private static JsonElement ParseJson(string value) => JsonSerializer.Deserialize<JsonElement>(value);
}
