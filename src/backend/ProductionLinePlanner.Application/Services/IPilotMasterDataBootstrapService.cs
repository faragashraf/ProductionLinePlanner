using ProductionLinePlanner.Application.DTOs;

namespace ProductionLinePlanner.Application.Services;

public interface IPilotMasterDataBootstrapService
{
    Task<PilotMasterDataBootstrapPreviewDto> PreviewAsync(PilotMasterDataBootstrapInput input, CancellationToken cancellationToken = default);
    Task<PilotMasterDataBootstrapApplyResultDto> ApplyAsync(PilotMasterDataBootstrapInput input, Guid actorUserId, bool confirmed, CancellationToken cancellationToken = default);
    Task<PilotMasterDataBootstrapVerificationDto> VerifyAsync(PilotMasterDataBootstrapInput input, CancellationToken cancellationToken = default);
}

public interface IPilotMasterDataResetService
{
    Task<PilotMasterDataResetPreviewDto> PreviewAsync(CancellationToken cancellationToken = default);
    Task<PilotMasterDataResetApplyResultDto> ApplyAsync(Guid actorUserId, bool confirmed, CancellationToken cancellationToken = default);
}
