using PhotoRecull.Core.Settings;

namespace PhotoRecull.App.ViewModels;

public sealed record CleanupFlowRequest(Guid? ExistingCleanupId, IReadOnlyList<TargetSettings> Targets);
