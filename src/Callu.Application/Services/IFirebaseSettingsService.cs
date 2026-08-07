using Callu.Shared.Models.Settings;

namespace Callu.Application.Services;

public interface IFirebaseSettingsService
{
    Task<FirebaseSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task<bool> SaveSettingsAsync(UpdateFirebaseSettingsRequest request, CancellationToken cancellationToken = default);

    Task<FirebaseTestResult> TestAsync(CancellationToken cancellationToken = default);
}
