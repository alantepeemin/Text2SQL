using Text2Sql.Application.DTOs.ApiKeys;

namespace Text2Sql.Application.Contracts
{
    public interface IApiKeyService
    {
        Task<CreateApiKeyResponse> CreateAsync(CreateApiKeyRequest request, CancellationToken ct = default);
        Task<List<ApiKeyDto>> ListAsync(CancellationToken ct = default);
        Task RevokeAsync(int keyId, CancellationToken ct = default);
    }
}
