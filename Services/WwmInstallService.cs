using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Storage;

namespace BdoClient.Services;

public enum WwmMutationError
{
    None, InvalidRoot, InvalidState, ModifiedFiles, Package, Snapshot, Mutation, Verification,
    RollbackFailed, RecoveryRequired, SnapshotStale, Cancelled
}

public sealed record WwmMutationResult(bool IsSuccess, WwmMutationError Error, string? Message = null)
{
    public static WwmMutationResult Success() => new(true, WwmMutationError.None);
    public static WwmMutationResult Failure(WwmMutationError error, string message) => new(false, error, message);
}

/// <summary>WWM-specific two-file transaction, snapshot, restore and crash recovery.</summary>
public sealed class WwmInstallService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly HttpClient _httpClient;
    private readonly GamePersistencePaths _persistencePaths;
    private readonly WwmStateStore _store;
    private readonly ILogger _logger;
    private readonly WwmGameDefinition _definition;
    private readonly ConfigStore? _configStore;
    private readonly AsyncLocal<WwmInstallationBinding?> _bindingContext = new();
    internal Action<int>? AfterTargetAppliedForTest { get; set; }
    internal Action? BeforeStateSaveForTest { get; set; }
    internal Action? BeforeRestoreJournalForTest { get; set; }
    internal Action<string, string>? BeforeGameTempWriteForTest { get; set; }
    internal Action<string, string>? BeforeTargetReplaceForTest { get; set; }

    public WwmInstallService(HttpClient httpClient, GamePersistencePaths persistencePaths, WwmStateStore store, ILogger logger,
        WwmGameDefinition? definition = null, ConfigStore? configStore = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _persistencePaths = persistencePaths ?? throw new ArgumentNullException(nameof(persistencePaths));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _definition = definition ?? WwmGameDefinition.Default;
        _configStore = configStore;
    }

    public async Task<WwmMutationResult> RecoverAsync(string gameRoot, string? currentBuildId, CancellationToken token = default,
        WwmInstallationBinding? explicitBinding = null)
    {
        using var bindingScope = PushBinding(gameRoot, currentBuildId, explicitBinding);
        var binding = _bindingContext.Value!;
        if (!_definition.IsMutationLayoutSafe(gameRoot, binding.Layout)) return WwmMutationResult.Failure(WwmMutationError.InvalidRoot, "Перевірена розкладка Where Winds Meet недоступна.");
        var legacyBinding = await BindLegacyMetadataAsync(token).ConfigureAwait(false);
        if (legacyBinding != null) return legacyBinding;
        WwmTransactionJournal? journal;
        string? error;
        try { journal = _store.LoadJournal(out error); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _logger.Error($"WWM transaction journal could not be inspected: {ex.Message}");
            return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Не вдалося безпечно перевірити журнал відновлення WWM.");
        }
        if (error != null) return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Журнал відновлення WWM пошкоджено; файли не змінювалися.");
        if (journal == null)
            return await RecoverAbortedSnapshotCycleAsync(gameRoot, currentBuildId, token).ConfigureAwait(false);

        if (!JournalMatchesBinding(journal, binding))
            return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Журнал WWM належить іншому шляху або цільовій розкладці; відновлення заблоковано.");

        var opDir = Path.Combine(_store.TransactionDirectory, journal.OperationId);
        bool committed;
        try { committed = await MatchesExpectedOutcomeAsync(gameRoot, journal, token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _logger.Error($"WWM recovery target validation failed: {ex.Message}");
            return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Не вдалося безпечно перевірити файли WWM для відновлення.");
        }
        if (committed)
        {
            if (!CleanupGameTemps(gameRoot, journal.OperationId))
                return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Результат транзакції підтверджено, але тимчасові файли WWM ще не вдалося прибрати.");
            if (journal.Operation == "restore" && !TryDeleteDirectory(_store.SnapshotDirectory))
                return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Відновлення виконано, але знімок ще не вдалося безпечно прибрати; відновлення транзакції повториться під час наступного запуску.");
            await _store.RetireJournalAsync(token).ConfigureAwait(false);
            DeleteDirectoryBestEffort(opDir);
            _logger.Info("Recovered WWM transaction: committed result was verified; journal cleanup completed.");
            return WwmMutationResult.Success();
        }

        if (binding.Source == WwmInstallationSource.Steam && journal.GameBuildId != null
            && !string.Equals(journal.GameBuildId, currentBuildId, StringComparison.Ordinal))
            return WwmMutationResult.Failure(WwmMutationError.SnapshotStale, GetSnapshotStaleMessage(binding));

        bool targetsAreTransactional;
        try { targetsAreTransactional = await MatchesJournalKnownTargetStatesAsync(gameRoot, journal, token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _logger.Error($"WWM recovery could not classify managed targets: {ex.Message}");
            return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Не вдалося безпечно класифікувати файли WWM; журнал і резервні дані збережено.");
        }
        if (!targetsAreTransactional)
            return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Файли WWM змінені поза незавершеною операцією; журнал і резервні дані збережено для ручної перевірки.");

        var rollback = await CompleteRollbackAsync(gameRoot, journal, opDir).ConfigureAwait(false);
        if (!rollback)
            return WwmMutationResult.Failure(WwmMutationError.RollbackFailed, "Не вдалося відновити всі файли WWM. Журнал і резервні дані збережено.");
        legacyBinding = await BindLegacyMetadataAsync(token).ConfigureAwait(false);
        if (legacyBinding != null) return legacyBinding;
        _logger.Warning("Recovered incomplete WWM transaction to its exact pre-operation state.");
        return WwmMutationResult.Success();
    }

    public Task<WwmMutationResult> RecoverAsync(WwmDetectionResult detection, CancellationToken token = default)
        => detection.IsMutationSupported
            ? RecoverAsync(detection.GameRoot!, detection.BuildId, token, WwmInstallationBinding.FromDetection(detection))
            : Task.FromResult(WwmMutationResult.Failure(WwmMutationError.InvalidRoot, "Для Where Winds Meet не визначено однозначну підтримувану цільову розкладку."));

    public async Task<WwmMutationResult> InstallAsync(string gameRoot, string? buildId, WwmApiPackage package,
        CancellationToken cancellationToken = default, WwmInstallationBinding? explicitBinding = null)
    {
        using var bindingScope = PushBinding(gameRoot, buildId, explicitBinding);
        var binding = _bindingContext.Value!;
        if (!_definition.IsMutationLayoutSafe(gameRoot, binding.Layout)) return WwmMutationResult.Failure(WwmMutationError.InvalidRoot, "Перевірена розкладка Where Winds Meet недоступна.");
        var recovered = await RecoverAsync(gameRoot, buildId, cancellationToken).ConfigureAwait(false);
        if (!recovered.IsSuccess) return recovered;

        var state = _store.Load(out var stateError);
        if (stateError != null) return WwmMutationResult.Failure(WwmMutationError.InvalidState, "Стан WWM пошкоджено; встановлення заблоковано.");
        if (state != null && !StateMatchesBinding(state, binding)) return WwmMutationResult.Failure(WwmMutationError.InvalidState, "Збережений стан WWM належить іншій інсталяції або розкладці.");
        if (state != null && IsStateStale(state, binding)) return WwmMutationResult.Failure(WwmMutationError.SnapshotStale, GetSnapshotStaleMessage(binding));
        if (state != null && !await MatchesManifestAsync(gameRoot, state.Targets, cancellationToken).ConfigureAwait(false))
            return WwmMutationResult.Failure(WwmMutationError.ModifiedFiles, "Файли локалізації змінені поза Хабом. Операцію заблоковано.");

        WwmStagedPackage staged;
        try { staged = await new WwmPackageService(_httpClient, _persistencePaths, _logger).DownloadAndStageAsync(package, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return WwmMutationResult.Failure(WwmMutationError.Cancelled, "Операцію скасовано."); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _logger.Warning($"WWM package preparation failed: {ex.Message}");
            return WwmMutationResult.Failure(WwmMutationError.Package, "Не вдалося перевірити пакет Winds4UA.");
        }

        try
        {
            state = _store.Load(out stateError);
            if (stateError != null)
                return WwmMutationResult.Failure(WwmMutationError.InvalidState, "Стан WWM змінився або пошкоджений; встановлення заблоковано.");
            if (state != null && !StateMatchesBinding(state, binding)) return WwmMutationResult.Failure(WwmMutationError.InvalidState, "Збережений стан WWM належить іншій інсталяції або розкладці.");
            if (state != null && IsStateStale(state, binding)) return WwmMutationResult.Failure(WwmMutationError.SnapshotStale, GetSnapshotStaleMessage(binding));
            if (state != null && !await MatchesManifestAsync(gameRoot, state.Targets, cancellationToken).ConfigureAwait(false))
                return WwmMutationResult.Failure(WwmMutationError.ModifiedFiles, "Файли локалізації змінилися під час підготовки пакета. Операцію заблоковано.");
            var operationId = Guid.NewGuid().ToString("N");
            var snapshotResult = await EnsurePreHubSnapshotAsync(gameRoot, buildId, state != null, operationId, cancellationToken).ConfigureAwait(false);
            if (snapshotResult.Snapshot == null)
                return WwmMutationResult.Failure(WwmMutationError.Snapshot, "Знімок попереднього стану відсутній або не належить активному циклу Хабу. Встановлення заблоковано.");
            var mapped = MapStagedTargets(staged, binding);
            return await RunTransactionAsync(gameRoot, buildId, mapped, snapshotResult.Snapshot,
                snapshotResult.Created, operationId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            new WwmPackageService(_httpClient, _persistencePaths, _logger).Cleanup(staged.WorkDirectory);
        }
    }

    public Task<WwmMutationResult> InstallAsync(WwmDetectionResult detection, WwmApiPackage package,
        CancellationToken cancellationToken = default)
        => detection.IsMutationSupported
            ? InstallAsync(detection.GameRoot!, detection.BuildId, package, cancellationToken, WwmInstallationBinding.FromDetection(detection))
            : Task.FromResult(WwmMutationResult.Failure(WwmMutationError.InvalidRoot, "Для Where Winds Meet не визначено однозначну підтримувану цільову розкладку."));

    public async Task<WwmMutationResult> RestorePreHubAsync(string gameRoot, string? currentBuildId, CancellationToken token = default,
        WwmInstallationBinding? explicitBinding = null)
    {
        using var bindingScope = PushBinding(gameRoot, currentBuildId, explicitBinding);
        var binding = _bindingContext.Value!;
        if (!_definition.IsMutationLayoutSafe(gameRoot, binding.Layout)) return WwmMutationResult.Failure(WwmMutationError.InvalidRoot, "Перевірена розкладка Where Winds Meet недоступна.");
        var legacyBinding = await BindLegacyMetadataAsync(token).ConfigureAwait(false);
        if (legacyBinding != null) return legacyBinding;
        var recovered = await RecoverAsync(gameRoot, currentBuildId, token).ConfigureAwait(false);
        if (!recovered.IsSuccess) return recovered;
        var state = _store.Load(out var stateError);
        if (stateError != null) return WwmMutationResult.Failure(WwmMutationError.InvalidState, "Стан WWM пошкоджено; відновлення заблоковано.");
        if (state == null) return WwmMutationResult.Failure(WwmMutationError.InvalidState, "Керований стан WWM не знайдено.");
        if (!StateMatchesBinding(state, binding)) return WwmMutationResult.Failure(WwmMutationError.InvalidState, "Збережений стан або знімок належить іншій інсталяції чи розкладці.");
        if (IsStateStale(state, binding)) return WwmMutationResult.Failure(WwmMutationError.SnapshotStale, GetSnapshotStaleMessage(binding));
        if (!await MatchesManifestAsync(gameRoot, state.Targets, token).ConfigureAwait(false))
            return WwmMutationResult.Failure(WwmMutationError.ModifiedFiles, "Керовані файли змінені поза Хабом; безпечне відновлення заблоковано.");

        var snapshot = LoadPreHubSnapshot(out var snapshotError);
        if (snapshot == null) return WwmMutationResult.Failure(WwmMutationError.Snapshot, snapshotError ?? "Знімок попереднього стану відсутній або пошкоджений.");
        if (IsSnapshotStale(snapshot, binding))
            return WwmMutationResult.Failure(WwmMutationError.SnapshotStale, GetSnapshotStaleMessage(binding));
        return await RestoreSnapshotTransactionAsync(gameRoot, currentBuildId, snapshot, token).ConfigureAwait(false);
    }

    public Task<WwmMutationResult> RestorePreHubAsync(WwmDetectionResult detection, CancellationToken token = default)
        => detection.IsMutationSupported
            ? RestorePreHubAsync(detection.GameRoot!, detection.BuildId, token, WwmInstallationBinding.FromDetection(detection))
            : Task.FromResult(WwmMutationResult.Failure(WwmMutationError.InvalidRoot, "Для Where Winds Meet не визначено однозначну підтримувану цільову розкладку."));

    public async Task<(bool IsAvailable, string? Message)> CheckRestoreAvailabilityAsync(
        string gameRoot, string? currentBuildId, CancellationToken token = default, WwmInstallationBinding? explicitBinding = null)
    {
        using var bindingScope = PushBinding(gameRoot, currentBuildId, explicitBinding);
        var binding = _bindingContext.Value!;
        if (!_definition.IsMutationLayoutSafe(gameRoot, binding.Layout))
            return (false, "Відновлення оригіналу неможливе: перевірену теку гри не знайдено.");

        var legacyBinding = await BindLegacyMetadataAsync(token).ConfigureAwait(false);
        if (legacyBinding != null) return (false, legacyBinding.Message);

        var state = _store.Load(out var stateError);
        if (stateError != null)
            return (false, "Відновлення оригіналу неможливе: збережений стан WWM пошкоджено.");
        if (state == null)
            return (false, Directory.Exists(_store.SnapshotDirectory)
                ? "Відновлення оригіналу неможливе: початковий знімок не пов'язаний із довіреним станом встановлення."
                : "Відновлення оригіналу неможливе: початковий знімок ще не створено.");
        if (!StateMatchesBinding(state, binding)) return (false, "Збережений стан або знімок належить іншій інсталяції чи розкладці.");
        if (IsStateStale(state, binding)) return (false, GetSnapshotStaleMessage(binding));
        if (!await MatchesManifestAsync(gameRoot, state.Targets, token).ConfigureAwait(false))
            return (false, "Керовані файли WWM змінені поза Хабом. Безпечне відновлення заблоковано.");

        var snapshot = LoadPreHubSnapshot(out var snapshotError);
        if (snapshot == null || !string.Equals(snapshot.CycleOperationId, state.PreHubSnapshotCycleId, StringComparison.Ordinal))
            return (false, $"Відновлення оригіналу неможливе: початковий знімок відсутній, пошкоджений або не належить цій установці.{(string.IsNullOrWhiteSpace(snapshotError) ? string.Empty : $" Деталі: {snapshotError}")}");
        if (IsSnapshotStale(snapshot, binding))
            return (false, GetSnapshotStaleMessage(binding));

        return (true, null);
    }

    public Task<(bool IsAvailable, string? Message)> CheckRestoreAvailabilityAsync(
        WwmDetectionResult detection, CancellationToken token = default)
        => detection.IsMutationSupported
            ? CheckRestoreAvailabilityAsync(detection.GameRoot!, detection.BuildId, token, WwmInstallationBinding.FromDetection(detection))
            : Task.FromResult<(bool IsAvailable, string? Message)>((false, "Для цієї інсталяції WWM не визначено однозначну цільову розкладку."));

    public async Task<WwmInstalledStateResult> ResolveStateAsync(string gameRoot, WwmApiPackage? current, CancellationToken token = default,
        WwmInstallationBinding? explicitBinding = null)
    {
        using var bindingScope = PushBinding(gameRoot, null, explicitBinding);
        var binding = _bindingContext.Value!;
        var legacyBinding = await BindLegacyMetadataAsync(token).ConfigureAwait(false);
        if (legacyBinding != null) return new(WwmInstalledStateKind.Modified, legacyBinding.Message);
        var state = _store.Load(out var error);
        if (error != null) return new(WwmInstalledStateKind.Modified, "Збережений стан WWM пошкоджено.");
        if (state == null) return new(WwmInstalledStateKind.Unknown);
        if (!StateMatchesBinding(state, binding)) return new(WwmInstalledStateKind.Modified, "Збережений стан належить іншій інсталяції або розкладці.");
        if (IsStateStale(state, binding)) return new(WwmInstalledStateKind.Modified, GetSnapshotStaleMessage(binding));
        if (!await MatchesManifestAsync(gameRoot, state.Targets, token).ConfigureAwait(false))
            return new(WwmInstalledStateKind.Modified, "Файли локалізації змінено або втрачено.");
        if (current != null && state.ModeSlug == current.Mode.Slug && state.ModeVariant == current.Mode.Variant
            && state.ReleaseVersion == current.Version && state.ArchiveSizeBytes == current.SizeBytes
            && state.ArchiveSha256 == current.Sha256)
            return new(WwmInstalledStateKind.Current);
        return new(WwmInstalledStateKind.UpdateAvailable);
    }

    public Task<WwmInstalledStateResult> ResolveStateAsync(WwmDetectionResult detection, WwmApiPackage? current,
        CancellationToken token = default)
        => detection.IsMutationSupported
            ? ResolveStateAsync(detection.GameRoot!, current, token, WwmInstallationBinding.FromDetection(detection))
            : Task.FromResult(new WwmInstalledStateResult(WwmInstalledStateKind.Unknown));

    private async Task<WwmMutationResult> RunTransactionAsync(string gameRoot, string? buildId, WwmStagedPackage staged,
        WwmPreHubSnapshot snapshot, bool createdPreHubSnapshot, string operationId, CancellationToken token)
    {
        var currentState = _store.Load(out var currentStateError);
        if (currentStateError != null)
        {
            if (createdPreHubSnapshot && !RetireCreatedSnapshot(operationId))
                return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Стан змінився; новий знімок не вдалося безпечно прибрати.");
            return WwmMutationResult.Failure(WwmMutationError.InvalidState, "Стан WWM пошкоджено перед транзакцією.");
        }
        if (currentState != null && !await MatchesManifestAsync(gameRoot, currentState.Targets, token).ConfigureAwait(false))
        {
            if (createdPreHubSnapshot && !RetireCreatedSnapshot(operationId))
                return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Файли змінилися; новий знімок не вдалося безпечно прибрати.");
            return WwmMutationResult.Failure(WwmMutationError.ModifiedFiles, "Керовані файли змінилися перед транзакцією.");
        }
        var operationDir = Path.Combine(_store.TransactionDirectory, operationId);
        WwmTransactionJournal? journal = null;
        var mutationStarted = false;
        var targetMutationOccurred = false;
        var phase = "prepare_transaction";
        string? activeTarget = null;
        try
        {
            Directory.CreateDirectory(operationDir);
            var previousState = File.Exists(_store.StateFile) ? await File.ReadAllBytesAsync(_store.StateFile, token).ConfigureAwait(false) : null;
            var previous = await CaptureTargetsAsync(gameRoot, operationDir, token).ConfigureAwait(false);
            var newState = new WwmInstallationState
            {
                SchemaVersion = 2,
                ModeSlug = staged.Package.Mode.Slug,
                ModeVariant = staged.Package.Mode.Variant,
                ReleaseVersion = staged.Package.Version,
                ArchiveSizeBytes = staged.Package.SizeBytes,
                ArchiveSha256 = staged.Package.Sha256,
                PreHubSnapshotCycleId = snapshot.CycleOperationId,
                GameBuildId = buildId,
                InstallationRoot = WwmGameDefinition.NormalizeInstallationRoot(gameRoot),
                TargetLayout = CurrentBinding.Layout.ToString(),
                InstallationFingerprint = CurrentBinding.Fingerprint,
                InstalledAt = DateTimeOffset.UtcNow,
                Targets = staged.Targets.ToList()
            };
            var stateJson = WwmStateStore.SerializeState(newState);
            journal = new WwmTransactionJournal
            {
                SchemaVersion = 2,
                OperationId = operationId,
                Operation = "install",
                CreatedPreHubSnapshot = createdPreHubSnapshot,
                PreviousStateExisted = previousState != null,
                PreviousStateBase64 = previousState == null ? "" : Convert.ToBase64String(previousState),
                PreviousTargets = previous,
                TargetManifest = staged.Targets.ToList(),
                ExpectedStateSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stateJson))).ToLowerInvariant(),
                GameBuildId = buildId,
                InstallationRoot = WwmGameDefinition.NormalizeInstallationRoot(gameRoot),
                TargetLayout = CurrentBinding.Layout.ToString(),
                InstallationFingerprint = CurrentBinding.Fingerprint
            };
            phase = "write_journal";
            await _store.WriteJournalAsync(journal, token).ConfigureAwait(false);

            var appliedTargets = 0;
            foreach (var item in staged.Targets)
            {
                token.ThrowIfCancellationRequested();
                activeTarget = item.RelativePath;
                var packageRelative = _definition.ManagedRelativePaths[Array.IndexOf(CurrentTargets, item.RelativePath)];
                var source = SafeCombine(Path.Combine(staged.WorkDirectory, "staged"), packageRelative);
                var target = SafeGameTarget(gameRoot, item.RelativePath);
                phase = "create_target_directory";
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                phase = "prepare_game_temp";
                var replacement = PrepareGameTemp(gameRoot, item.RelativePath, operationId, ".hub-");
                phase = "write_game_temp";
                BeforeGameTempWriteForTest?.Invoke(item.RelativePath, replacement);
                await CopyVerifiedAsync(source, replacement, item, token).ConfigureAwait(false);
                var previousAttributes = previous.Single(record => record.RelativePath == item.RelativePath).Attributes;
                if (File.Exists(target))
                {
                    phase = "replace_target";
                    mutationStarted = true;
                    BeforeTargetReplaceForTest?.Invoke(item.RelativePath, target);
                    ReplaceManagedTarget(replacement, target, previousAttributes,
                        () => targetMutationOccurred = true, nextPhase => phase = nextPhase);
                }
                else
                {
                    phase = "move_new_target";
                    mutationStarted = true;
                    File.Move(replacement, target);
                    targetMutationOccurred = true;
                }
                phase = "verify_target";
                await VerifyFileAsync(target, item, token).ConfigureAwait(false);
                phase = "post_replace_hook";
                AfterTargetAppliedForTest?.Invoke(++appliedTargets);
            }

            activeTarget = null;
            phase = "save_installation_state";
            BeforeStateSaveForTest?.Invoke();
            await _store.SaveAsync(newState, token).ConfigureAwait(false);
            phase = "verify_committed_outcome";
            if (!await MatchesExpectedOutcomeAsync(gameRoot, journal, token).ConfigureAwait(false))
                throw new IOException("WWM transaction post-commit verification failed.");
            phase = "cleanup_game_temps";
            if (!CleanupGameTemps(gameRoot, operationId))
                throw new IOException("WWM transaction temporary-file cleanup failed.");
            await _store.RetireJournalAsync(CancellationToken.None).ConfigureAwait(false);
            DeleteDirectoryBestEffort(operationDir);
            return WwmMutationResult.Success();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (!mutationStarted || journal == null)
            {
                if (journal != null && !await RetireUnmutatedTransactionAsync(gameRoot, operationDir, createdPreHubSnapshot, operationId).ConfigureAwait(false))
                    return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Підготовку скасовано, але тимчасові дані WWM ще потребують відновлення.");
                if (journal == null)
                {
                    DeleteDirectoryBestEffort(operationDir);
                    if (createdPreHubSnapshot && !RetireCreatedSnapshot(operationId))
                        return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Підготовку скасовано, але новий знімок не вдалося безпечно прибрати.");
                }
                return WwmMutationResult.Failure(WwmMutationError.Cancelled, "Операцію скасовано.");
            }
            if (!targetMutationOccurred)
            {
                if (await MatchesPreviousOutcomeAsync(gameRoot, journal, CancellationToken.None).ConfigureAwait(false))
                {
                    var retired = await RetireUnmutatedTransactionAsync(gameRoot, operationDir, createdPreHubSnapshot, operationId).ConfigureAwait(false);
                    return WwmMutationResult.Failure(retired ? WwmMutationError.Cancelled : WwmMutationError.RecoveryRequired,
                        retired ? "Операцію скасовано до зміни файлів гри." : "Операцію скасовано; безпечне очищення транзакції буде повторено під час наступного запуску.");
                }
                CleanupGameTemps(gameRoot, operationId);
                return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Стан файлів WWM змінився до підтвердженої заміни; журнал і резервні дані збережено.");
            }
            var rolledBack = await CompleteRollbackAsync(gameRoot, journal, operationDir).ConfigureAwait(false);
            return WwmMutationResult.Failure(rolledBack ? WwmMutationError.Cancelled : WwmMutationError.RollbackFailed,
                rolledBack ? "Операцію скасовано; попередній стан відновлено." : "Критична помилка: не вдалося відкотити файли WWM.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LogTransactionFailure(journal?.OperationId ?? "unassigned", "install", activeTarget, phase, ex);
            if (!mutationStarted || journal == null)
            {
                if (journal != null)
                {
                    if (!await RetireUnmutatedTransactionAsync(gameRoot, operationDir, createdPreHubSnapshot, operationId).ConfigureAwait(false))
                        return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Підготовка WWM не завершилась; безпечне очищення буде повторено під час відновлення.");
                }
                else
                {
                    DeleteDirectoryBestEffort(operationDir);
                    if (createdPreHubSnapshot && !RetireCreatedSnapshot(operationId))
                        return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Підготовка WWM не завершилась; новий знімок не вдалося безпечно прибрати.");
                }
                return WwmMutationResult.Failure(WwmMutationError.Mutation,
                    GetFilesystemFailureMessage(ex, "Операцію WWM не вдалося підготувати; файли гри не змінено."));
            }
            if (!targetMutationOccurred)
            {
                if (await MatchesPreviousOutcomeAsync(gameRoot, journal, CancellationToken.None).ConfigureAwait(false))
                {
                    var retired = await RetireUnmutatedTransactionAsync(gameRoot, operationDir, createdPreHubSnapshot, operationId).ConfigureAwait(false);
                    return WwmMutationResult.Failure(retired ? WwmMutationError.Mutation : WwmMutationError.RecoveryRequired,
                    retired ? GetFilesystemFailureMessage(ex, "Не вдалося підготувати заміну; файли гри не змінено.") : "Підготовка не завершилася; безпечне очищення буде повторено під час наступного запуску.");
                }
                CleanupGameTemps(gameRoot, operationId);
                return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Стан файлів WWM змінився до підтвердженої заміни; журнал і резервні дані збережено.");
            }
            var rolledBack = await CompleteRollbackAsync(gameRoot, journal, operationDir).ConfigureAwait(false);
            return WwmMutationResult.Failure(rolledBack ? WwmMutationError.Mutation : WwmMutationError.RollbackFailed,
                rolledBack ? GetFilesystemFailureMessage(ex, "Не вдалося встановити пакет; попередній стан відновлено.") : "Критична помилка: відкат WWM не завершився. Дані відновлення збережено.");
        }
    }

    private async Task<bool> RetireUnmutatedTransactionAsync(string gameRoot, string operationDir, bool createdPreHubSnapshot, string operationId)
    {
        try
        {
            if (!CleanupGameTemps(gameRoot, operationId)) return false;
            await _store.RetireJournalAsync(CancellationToken.None).ConfigureAwait(false);
            DeleteDirectoryBestEffort(operationDir);
            if (Directory.Exists(operationDir)) return false;
            return !createdPreHubSnapshot || RetireCreatedSnapshot(operationId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning($"Could not retire an unmutated WWM transaction journal: {ex.Message}");
            return false;
        }
    }

    private bool RetireCreatedSnapshot(string operationId)
    {
        var snapshot = LoadPreHubSnapshot(out _);
        return snapshot != null && snapshot.CycleOperationId == operationId && TryDeleteDirectory(_store.SnapshotDirectory);
    }

    private bool CleanupUnmutatedOperationDirectory(string operationDir)
    {
        try
        {
            DeleteDirectoryBestEffort(operationDir);
            return !Directory.Exists(operationDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning($"Could not clean unmutated WWM operation directory: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> CompleteRollbackAsync(string root, WwmTransactionJournal journal, string operationDir)
    {
        if (!await RollbackAsync(root, journal, operationDir, CancellationToken.None).ConfigureAwait(false))
            return false;
        try
        {
            await _store.RetireJournalAsync(CancellationToken.None).ConfigureAwait(false);
            DeleteDirectoryBestEffort(operationDir);
            if (Directory.Exists(operationDir)) return false;
            if (journal.CreatedPreHubSnapshot && !journal.PreviousStateExisted && !RetireCreatedSnapshot(journal.OperationId))
                return false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error($"WWM rollback completed but its journal could not be retired: {ex.Message}");
            return false;
        }
    }

    private async Task<WwmMutationResult> RestoreSnapshotTransactionAsync(string gameRoot, string? buildId, WwmPreHubSnapshot snapshot, CancellationToken token)
    {
        if (!ValidateSnapshotFiles(snapshot))
            return WwmMutationResult.Failure(WwmMutationError.Snapshot, "Знімок попереднього стану пошкоджено.");

        var id = Guid.NewGuid().ToString("N");
        var dir = Path.Combine(_store.TransactionDirectory, id);
        WwmTransactionJournal? journal = null;
        var mutationStarted = false;
        var targetMutationOccurred = false;
        var committed = false;
        var phase = "prepare_restore";
        string? activeTarget = null;
        try
        {
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(dir);
            var previousState = File.Exists(_store.StateFile)
                ? await File.ReadAllBytesAsync(_store.StateFile, token).ConfigureAwait(false)
                : null;
            var previousTargets = await CaptureTargetsAsync(gameRoot, dir, token).ConfigureAwait(false);
            var restoredTargets = new List<WwmTargetManifestEntry>();
            var absent = new List<string>();
            foreach (var item in snapshot.Targets)
            {
                if (!item.Existed) { absent.Add(item.RelativePath); continue; }
                restoredTargets.Add(new WwmTargetManifestEntry
                {
                    RelativePath = item.RelativePath,
                    SizeBytes = item.SizeBytes!.Value,
                    Sha256 = item.Sha256!
                });
            }
            journal = new WwmTransactionJournal
            {
                SchemaVersion = 2,
                OperationId = id,
                Operation = "restore",
                PreviousStateExisted = previousState != null,
                PreviousStateBase64 = previousState == null ? "" : Convert.ToBase64String(previousState),
                PreviousTargets = previousTargets,
                TargetManifest = restoredTargets,
                ExpectedAbsentTargets = absent,
                GameBuildId = buildId,
                ExpectedStateSha256 = "absent",
                InstallationRoot = WwmGameDefinition.NormalizeInstallationRoot(gameRoot),
                TargetLayout = CurrentBinding.Layout.ToString(),
                InstallationFingerprint = CurrentBinding.Fingerprint
            };
            BeforeRestoreJournalForTest?.Invoke();
            phase = "write_journal";
            await _store.WriteJournalAsync(journal, token).ConfigureAwait(false);

            foreach (var item in snapshot.Targets)
            {
                token.ThrowIfCancellationRequested();
                activeTarget = item.RelativePath;
                var target = SafeGameTarget(gameRoot, item.RelativePath);
                if (!item.Existed)
                {
                    if (File.Exists(target))
                    {
                        phase = "delete_target";
                        mutationStarted = true;
                        DeleteManagedTarget(target, nextPhase => phase = nextPhase);
                        targetMutationOccurred = true;
                    }
                    continue;
                }
                var expected = restoredTargets.Single(entry => entry.RelativePath == item.RelativePath);
                phase = "prepare_game_temp";
                var replacement = PrepareGameTemp(gameRoot, item.RelativePath, id, ".hub-");
                phase = "write_game_temp";
                BeforeGameTempWriteForTest?.Invoke(item.RelativePath, replacement);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await CopyVerifiedAsync(SafeCombine(_store.SnapshotDirectory, item.BackupFile!), replacement, expected, token).ConfigureAwait(false);
                if (File.Exists(target))
                {
                    phase = "replace_target";
                    mutationStarted = true;
                    BeforeTargetReplaceForTest?.Invoke(item.RelativePath, target);
                    ReplaceManagedTarget(replacement, target, item.Attributes,
                        () => targetMutationOccurred = true, nextPhase => phase = nextPhase);
                }
                else
                {
                    phase = "move_restored_target";
                    mutationStarted = true;
                    File.Move(replacement, target);
                    targetMutationOccurred = true;
                    if (item.Attributes is { } attributes) File.SetAttributes(target, (FileAttributes)attributes);
                }
                phase = "verify_target";
                await VerifyFileAsync(target, expected, token).ConfigureAwait(false);
            }
            activeTarget = null;
            phase = "clear_installation_state";
            await _store.ClearAsync(token).ConfigureAwait(false);
            phase = "verify_committed_outcome";
            if (!await MatchesExpectedOutcomeAsync(gameRoot, journal, token).ConfigureAwait(false))
                throw new IOException("WWM restore verification failed.");
            committed = true;
            phase = "cleanup_game_temps";
            if (!CleanupGameTemps(gameRoot, id))
                return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Попередній стан відновлено, але тимчасові файли ще потрібно прибрати; відновлення повториться під час наступного запуску.");
            if (!TryDeleteDirectory(_store.SnapshotDirectory))
                return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Попередній стан відновлено, але знімок ще залишився; відновлення повториться під час наступного запуску.");
            await _store.RetireJournalAsync(CancellationToken.None).ConfigureAwait(false);
            DeleteDirectoryBestEffort(dir);
            return WwmMutationResult.Success();
        }
        catch (OperationCanceledException)
        {
            if (committed)
                return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Відновлення вже завершене; очищення транзакції буде повторено під час наступного запуску.");
            if (!mutationStarted)
            {
                var cleaned = journal == null
                    ? CleanupUnmutatedOperationDirectory(dir)
                    : await RetireUnmutatedTransactionAsync(gameRoot, dir, createdPreHubSnapshot: false, id).ConfigureAwait(false);
                return WwmMutationResult.Failure(cleaned ? WwmMutationError.Cancelled : WwmMutationError.RecoveryRequired,
                    cleaned ? "Операцію відновлення скасовано до зміни файлів." : "Відновлення скасовано; безпечне очищення буде повторено.");
            }
            if (journal != null && !targetMutationOccurred)
            {
                if (await MatchesPreviousOutcomeAsync(gameRoot, journal, CancellationToken.None).ConfigureAwait(false))
                {
                    var cleaned = await RetireUnmutatedTransactionAsync(gameRoot, dir, createdPreHubSnapshot: false, id).ConfigureAwait(false);
                    return WwmMutationResult.Failure(cleaned ? WwmMutationError.Cancelled : WwmMutationError.RecoveryRequired,
                        cleaned ? "Операцію відновлення скасовано до зміни файлів." : "Відновлення скасовано; безпечне очищення буде повторено.");
                }
                CleanupGameTemps(gameRoot, id);
                return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Стан файлів WWM змінився до підтвердженої заміни; журнал і резервні дані збережено.");
            }
            var rollback = journal != null && await CompleteRollbackAsync(gameRoot, journal, dir).ConfigureAwait(false);
            return WwmMutationResult.Failure(rollback ? WwmMutationError.Cancelled : WwmMutationError.RollbackFailed,
                rollback ? "Відновлення скасовано; стан перед операцією повернуто." : "Критична помилка відновлення WWM; журнал збережено.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            LogTransactionFailure(journal?.OperationId ?? id, "restore", activeTarget, phase, ex);
            if (committed)
                return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Попередній стан уже відновлено, але очищення транзакції не завершилося. Відновлення повториться під час наступного запуску.");
            if (!mutationStarted)
            {
                var cleaned = journal == null
                    ? CleanupUnmutatedOperationDirectory(dir)
                    : await RetireUnmutatedTransactionAsync(gameRoot, dir, createdPreHubSnapshot: false, id).ConfigureAwait(false);
                return WwmMutationResult.Failure(cleaned ? WwmMutationError.Snapshot : WwmMutationError.RecoveryRequired,
                    cleaned ? GetFilesystemFailureMessage(ex, "Не вдалося підготувати відновлення; файли гри не змінено.") : "Підготовка не завершилася; безпечне очищення буде повторено.");
            }
            if (journal != null && !targetMutationOccurred)
            {
                if (await MatchesPreviousOutcomeAsync(gameRoot, journal, CancellationToken.None).ConfigureAwait(false))
                {
                    var cleaned = await RetireUnmutatedTransactionAsync(gameRoot, dir, createdPreHubSnapshot: false, id).ConfigureAwait(false);
                    return WwmMutationResult.Failure(cleaned ? WwmMutationError.Mutation : WwmMutationError.RecoveryRequired,
                        cleaned ? "Не вдалося підготувати відновлення; файли гри не змінено." : "Підготовка не завершилася; безпечне очищення буде повторено.");
                }
                CleanupGameTemps(gameRoot, id);
                return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Стан файлів WWM змінився до підтвердженої заміни; журнал і резервні дані збережено.");
            }
            var rollback = journal != null && await CompleteRollbackAsync(gameRoot, journal, dir).ConfigureAwait(false);
            return WwmMutationResult.Failure(rollback ? WwmMutationError.Mutation : WwmMutationError.RollbackFailed,
                rollback ? GetFilesystemFailureMessage(ex, "Не вдалося відновити попередній стан; файли повернуто до стану перед операцією.") : "Критична помилка відновлення WWM; журнал збережено.");
        }
    }

    private async Task<(WwmPreHubSnapshot? Snapshot, bool Created)> EnsurePreHubSnapshotAsync(
        string root, string? buildId, bool hasTrustedInstalledState, string cycleOperationId, CancellationToken token)
    {
        if (hasTrustedInstalledState)
        {
            var existing = LoadPreHubSnapshot(out var error);
            var installedState = _store.Load(out _);
            if (existing != null && !string.Equals(existing.CycleOperationId, installedState?.PreHubSnapshotCycleId, StringComparison.Ordinal))
            {
                _logger.Warning("WWM pre-Hub snapshot does not belong to the trusted installed-state cycle.");
                return (null, false);
            }
            if (existing == null)
                _logger.Warning($"Active WWM management cycle has no valid pre-Hub snapshot: {error}");
            return (existing, false);
        }
        if (Directory.Exists(_store.SnapshotDirectory))
        {
            _logger.Warning("WWM pre-Hub snapshot exists without trusted installed state; refusing to reuse or replace it.");
            return (null, false);
        }
        Directory.CreateDirectory(_store.SnapshotDirectory);
        var targets = new List<WwmPreviousTarget>();
        var temporaryFiles = new List<(string Temp, string Final)>();
        try
        {
            var index = 0;
            foreach (var relative in CurrentTargets)
            {
                token.ThrowIfCancellationRequested();
                var path = SafeGameTarget(root, relative);
                if (!File.Exists(path))
                {
                    targets.Add(new WwmPreviousTarget { RelativePath = relative, Existed = false });
                    index++;
                    continue;
                }
                var name = index + ".bin";
                var temp = Path.Combine(_store.SnapshotDirectory, name + ".tmp");
                var final = Path.Combine(_store.SnapshotDirectory, name);
                await CopyFileAsync(path, temp, token).ConfigureAwait(false);
                var size = new FileInfo(temp).Length;
                var sha = await HashHelper.ComputeFileSha256Async(temp, token).ConfigureAwait(false);
                targets.Add(new WwmPreviousTarget
                {
                    RelativePath = relative,
                    Existed = true,
                    SizeBytes = size,
                    Sha256 = sha,
                    BackupFile = name,
                    Attributes = (int)File.GetAttributes(path)
                });
                temporaryFiles.Add((temp, final));
                index++;
            }
            foreach (var pair in temporaryFiles) File.Move(pair.Temp, pair.Final, overwrite: false);
            var snapshot = new WwmPreHubSnapshot
            {
                SchemaVersion = 2, CycleOperationId = cycleOperationId, GameBuildId = buildId, Targets = targets,
                InstallationRoot = WwmGameDefinition.NormalizeInstallationRoot(root), TargetLayout = CurrentBinding.Layout.ToString(),
                InstallationFingerprint = CurrentBinding.Fingerprint
            };
            await File.WriteAllTextAsync(Path.Combine(_store.SnapshotDirectory, "snapshot.json.tmp"), JsonSerializer.Serialize(snapshot, JsonOptions), token).ConfigureAwait(false);
            File.Move(Path.Combine(_store.SnapshotDirectory, "snapshot.json.tmp"), Path.Combine(_store.SnapshotDirectory, "snapshot.json"), overwrite: false);
            return (snapshot, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _logger.Error($"WWM pre-Hub snapshot creation failed: {ex.Message}");
            DeleteDirectoryBestEffort(_store.SnapshotDirectory);
            return (null, false);
        }
        catch (OperationCanceledException)
        {
            DeleteDirectoryBestEffort(_store.SnapshotDirectory);
            throw;
        }
    }

    private async Task<WwmMutationResult> RecoverAbortedSnapshotCycleAsync(string root, string? currentBuildId, CancellationToken token)
    {
        var state = _store.Load(out var stateError);
        if (stateError != null)
            return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Стан WWM пошкоджено; перевірку незавершеного циклу заблоковано.");
        if (state != null)
        {
            if (!StateMatchesBinding(state, CurrentBinding))
                return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Керований стан WWM належить іншій інсталяції або розкладці.");
            if (IsStateStale(state, CurrentBinding))
                return WwmMutationResult.Failure(WwmMutationError.SnapshotStale, GetSnapshotStaleMessage(CurrentBinding));
            var activeSnapshot = LoadPreHubSnapshot(out _);
            if (activeSnapshot != null && CurrentBinding.Source != WwmInstallationSource.Steam
                && IsSnapshotStale(activeSnapshot, CurrentBinding))
                return WwmMutationResult.Failure(WwmMutationError.SnapshotStale, GetSnapshotStaleMessage(CurrentBinding));
            if (activeSnapshot == null || !string.Equals(activeSnapshot.CycleOperationId, state.PreHubSnapshotCycleId, StringComparison.Ordinal))
                return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired,
                    "Керований стан WWM не має відповідного знімка активного циклу; встановлення та відновлення заблоковано.");
            if (!await MatchesManifestAsync(root, state.Targets, token).ConfigureAwait(false))
                return WwmMutationResult.Failure(WwmMutationError.ModifiedFiles,
                    "Керовані файли WWM змінені поза Хабом. Безпечну операцію заблоковано.");
            return WwmMutationResult.Success();
        }
        if (!Directory.Exists(_store.SnapshotDirectory)) return WwmMutationResult.Success();

        var snapshot = LoadPreHubSnapshot(out var snapshotError);
        if (snapshot == null)
            return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired,
                "Знімок WWM існує без керованого стану й не має валідного доказу незавершеного циклу; потрібна ручна перевірка.");
        if (IsSnapshotStale(snapshot, CurrentBinding))
            return WwmMutationResult.Failure(WwmMutationError.SnapshotStale, GetSnapshotStaleMessage(CurrentBinding));
        if (!await MatchesSnapshotBaselineAsync(root, snapshot, token).ConfigureAwait(false))
            return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired,
                "Файли WWM відрізняються від знімка незавершеного циклу; знімок збережено для ручної перевірки.");
        var orphanOperationDir = Path.GetFullPath(Path.Combine(_store.TransactionDirectory, snapshot.CycleOperationId));
        if (!string.Equals(Path.GetDirectoryName(orphanOperationDir), Path.GetFullPath(_store.TransactionDirectory), StringComparison.OrdinalIgnoreCase))
            return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Шлях незавершеної транзакції WWM некоректний.");
        if (!TryDeleteDirectory(orphanOperationDir))
            return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Не вдалося прибрати дані перерваного циклу WWM.");
        return TryDeleteDirectory(_store.SnapshotDirectory)
            ? WwmMutationResult.Success()
            : WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Не вдалося прибрати підтверджений знімок перерваного циклу WWM.");
    }

    private async Task<bool> MatchesSnapshotBaselineAsync(string root, WwmPreHubSnapshot snapshot, CancellationToken token)
    {
        try
        {
            foreach (var item in snapshot.Targets)
            {
                var target = SafeGameTarget(root, item.RelativePath);
                if (!item.Existed)
                {
                    if (File.Exists(target)) return false;
                    continue;
                }
                if (!File.Exists(target) || new FileInfo(target).Length != item.SizeBytes
                    || !string.Equals(await HashHelper.ComputeFileSha256Async(target, token).ConfigureAwait(false), item.Sha256, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _logger.Warning($"Could not compare WWM files to an aborted snapshot baseline: {ex.Message}");
            return false;
        }
    }

    private WwmPreHubSnapshot? LoadPreHubSnapshot(out string? error)
    {
        error = null;
        try
        {
            var file = Path.Combine(_store.SnapshotDirectory, "snapshot.json");
            if (!File.Exists(file)) { error = "Pre-Hub snapshot is missing."; return null; }
            var snapshot = JsonSerializer.Deserialize<WwmPreHubSnapshot>(File.ReadAllText(file), JsonOptions);
            if (snapshot == null || !ValidateSnapshotFiles(snapshot))
                throw new InvalidDataException("Pre-Hub snapshot metadata or files are invalid.");
            return snapshot;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            error = ex.Message;
            return null;
        }
    }

    private async Task<WwmMutationResult?> BindLegacyMetadataAsync(CancellationToken token)
    {
        var state = _store.Load(out var stateError);
        if (stateError != null) return null; // The owning operation reports the existing typed state error.

        WwmTransactionJournal? journal = null;
        if (File.Exists(_store.JournalFile))
        {
            journal = _store.LoadJournal(out var journalError);
            if (journalError != null)
                return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired,
                    "Журнал відновлення WWM пошкоджено; legacy прив’язка не виконувалася, докази збережено.");
        }

        WwmPreHubSnapshot? snapshot = null;
        if (Directory.Exists(_store.SnapshotDirectory))
        {
            try
            {
                var snapshotPath = Path.Combine(_store.SnapshotDirectory, "snapshot.json");
                if (File.Exists(snapshotPath))
                    snapshot = JsonSerializer.Deserialize<WwmPreHubSnapshot>(await File.ReadAllTextAsync(snapshotPath, token).ConfigureAwait(false), JsonOptions);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or OperationCanceledException)
            {
                if (ex is OperationCanceledException) throw;
                return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired,
                    "Знімок WWM неможливо безпечно прочитати; докази збережено.");
            }
        }

        var hasLegacyState = state?.SchemaVersion == 1;
        var hasLegacySnapshot = snapshot?.SchemaVersion == 1;
        var hasLegacyJournal = journal?.SchemaVersion == 1;
        var automaticBindingCanResume = CurrentBinding.IsUniqueAutomaticSteamDetection
            && CurrentBinding.Source == WwmInstallationSource.Steam
            && CurrentBinding.Layout == WwmInstallationLayout.Package
            && string.IsNullOrWhiteSpace(CurrentBinding.LegacyPersistedRootEvidence);
        if (!hasLegacyState && !hasLegacySnapshot && !hasLegacyJournal)
        {
            if (automaticBindingCanResume && state?.SchemaVersion == 2 && snapshot?.SchemaVersion == 2
                && StateMatchesBinding(state, CurrentBinding) && ValidateSnapshotFiles(snapshot)
                && (journal == null || JournalMatchesBinding(journal, CurrentBinding))
                && string.Equals(state.PreHubSnapshotCycleId, snapshot.CycleOperationId, StringComparison.Ordinal)
                && await MatchesManifestAsync(CurrentBinding.GameRoot, state.Targets, token).ConfigureAwait(false))
                return await PersistAutomaticallyDetectedRootAsync(token).ConfigureAwait(false);
            return null;
        }
        var automaticLegacyBinding = automaticBindingCanResume;
        if (!HasTrustedLegacyRoot(CurrentBinding))
            return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired,
                "Знайдено дані попередньої версії Хабу, але їх не вдалося автоматично прив’язати до цієї інсталяції. Оберіть папку цієї гри вручну, щоб підтвердити шлях.");

        if (state != null && state.SchemaVersion == 2 && !StateMatchesBinding(state, CurrentBinding))
            return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Стан WWM належить іншій інсталяції; legacy binding заблоковано.");
        if (journal != null && journal.SchemaVersion == 2 && !JournalMatchesBinding(journal, CurrentBinding))
            return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Журнал WWM належить іншій інсталяції; legacy binding заблоковано.");
        if (snapshot?.SchemaVersion == 2 && !ValidateSnapshotFiles(snapshot))
            return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Знімок WWM має некоректну прив’язку або файли; legacy binding заблоковано.");
        if (hasLegacySnapshot && (snapshot == null || !ValidateSnapshotFiles(snapshot)))
            return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Legacy-знімок WWM не пройшов перевірку цілі, циклу або резервних файлів.");
        if (hasLegacyJournal && (journal == null || snapshot == null
            || (state != null && !string.Equals(state.PreHubSnapshotCycleId, snapshot.CycleOperationId, StringComparison.Ordinal))
            || !LegacyJournalProvesSnapshotCycle(snapshot, journal)))
            return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired,
                "Неможливо довести, що legacy-журнал WWM належить активному циклу початкового знімка.");
        if (hasLegacyState && (snapshot == null || state == null
            || !string.Equals(state.PreHubSnapshotCycleId, snapshot.CycleOperationId, StringComparison.Ordinal)))
            return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Legacy-стан WWM не має знімка того самого довіреного циклу.");
        if (hasLegacySnapshot && !SnapshotCycleIsProven(snapshot!, state, journal))
            return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Неможливо довести належність legacy-знімка активному циклу WWM.");

        Config? configToPersist = null;
        if (automaticLegacyBinding)
        {
            if (!await CanAutoBindLegacyMetadataAsync(state, snapshot, journal, hasLegacyState,
                    hasLegacyJournal, token).ConfigureAwait(false))
                return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired,
                    "Знайдено дані попередньої версії Хабу, але їх не вдалося автоматично прив’язати до цієї інсталяції. Оберіть папку цієї гри вручну, щоб підтвердити шлях.");

            if (_configStore == null)
                return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired,
                    "Не вдалося зберегти підтверджений шлях Where Winds Meet; виберіть папку гри вручну.");
            var configLoad = _configStore.Load();
            if (configLoad.Status == FileLoadStatus.Invalid)
                return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired,
                    "Налаштування шляху Where Winds Meet неможливо безпечно оновити; виберіть папку гри вручну.");
            var savedConfig = configLoad.Value ?? new Config();
            if (!string.IsNullOrWhiteSpace(savedConfig.GamePath)
                && !TrySameFullPath(savedConfig.GamePath, CurrentBinding.GameRoot))
                return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired,
                    "Збережений шлях Where Winds Meet відрізняється від автоматично знайденої гри; підтвердіть шлях вручну.");
            if (string.IsNullOrWhiteSpace(savedConfig.GamePath))
            {
                configToPersist = savedConfig;
                configToPersist.GamePath = WwmGameDefinition.NormalizeInstallationRoot(CurrentBinding.GameRoot);
            }
        }

        var legacyJournalWasCommitted = false;
        if (hasLegacyJournal)
        {
            try
            {
                // Capture the full state + target outcome before any schema migration changes state bytes.
                legacyJournalWasCommitted = await MatchesExpectedOutcomeAsync(
                    CurrentBinding.GameRoot, journal!, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException)
            {
                if (ex is OperationCanceledException) throw;
                return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired,
                    "Не вдалося безпечно перевірити завершений результат legacy-транзакції WWM.");
            }
        }

        if (hasLegacyState)
        {
            state!.SchemaVersion = 2;
            state.InstallationRoot = WwmGameDefinition.NormalizeInstallationRoot(CurrentBinding.GameRoot);
            state.TargetLayout = WwmInstallationLayout.Package.ToString();
            state.InstallationFingerprint = CurrentBinding.Fingerprint;
            if (legacyJournalWasCommitted && journal!.ExpectedStateSha256 != "absent")
            {
                // Persist the new expected hash in the bound journal before replacing the state file.
                // If the process stops between these writes, the already-validated committed
                // targets/state transition remains recognizable on the next recovery attempt.
                journal.SchemaVersion = 2;
                journal.InstallationRoot = WwmGameDefinition.NormalizeInstallationRoot(CurrentBinding.GameRoot);
                journal.TargetLayout = WwmInstallationLayout.Package.ToString();
                journal.InstallationFingerprint = CurrentBinding.Fingerprint;
                journal.ExpectedStateSha256 = Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes(WwmStateStore.SerializeState(state)))).ToLowerInvariant();
                try { await _store.WriteJournalAsync(journal, token).ConfigureAwait(false); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException)
                {
                    if (ex is OperationCanceledException) throw;
                    return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired,
                        "Не вдалося безпечно зафіксувати committed-результат legacy-транзакції WWM.");
                }
            }
            try { await _store.SaveAsync(state, token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException)
            {
                if (ex is OperationCanceledException) throw;
                return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Не вдалося безпечно закріпити legacy-стан за цією інсталяцією.");
            }
        }

        if (hasLegacySnapshot)
        {
            snapshot!.SchemaVersion = 2;
            snapshot.InstallationRoot = WwmGameDefinition.NormalizeInstallationRoot(CurrentBinding.GameRoot);
            snapshot.TargetLayout = WwmInstallationLayout.Package.ToString();
            snapshot.InstallationFingerprint = CurrentBinding.Fingerprint;
            try { await SaveSnapshotMetadataAsync(snapshot, token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                if (ex is OperationCanceledException) throw;
                return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Не вдалося безпечно закріпити legacy-знімок за цією інсталяцією.");
            }
        }

        if (hasLegacyJournal)
        {
            journal!.SchemaVersion = 2;
            journal.InstallationRoot = WwmGameDefinition.NormalizeInstallationRoot(CurrentBinding.GameRoot);
            journal.TargetLayout = WwmInstallationLayout.Package.ToString();
            journal.InstallationFingerprint = CurrentBinding.Fingerprint;
            try { await _store.WriteJournalAsync(journal, token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException)
            {
                if (ex is OperationCanceledException) throw;
                return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Не вдалося безпечно закріпити legacy-журнал за цією інсталяцією.");
            }
        }

        if (configToPersist != null)
        {
            try { await _configStore!.SaveAsync(configToPersist, token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException)
            {
                if (ex is OperationCanceledException) throw;
                return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired,
                    "Метадані WWM прив’язано, але шлях гри не вдалося зберегти. Повторіть запуск або виберіть папку вручну.");
            }
        }

        _logger.Info(automaticLegacyBinding
            ? "Bound verified legacy WWM Package metadata to the unique Steam AppID installation after installed-target and snapshot verification."
            : "Bound verified legacy WWM Package metadata to the exact persisted Steam installation root.");
        return null;
    }

    private async Task<bool> CanAutoBindLegacyMetadataAsync(WwmInstallationState? state, WwmPreHubSnapshot? snapshot,
        WwmTransactionJournal? journal, bool hasLegacyState, bool hasLegacyJournal,
        CancellationToken token)
    {
        var binding = CurrentBinding;
        if (!binding.IsUniqueAutomaticSteamDetection || binding.Source != WwmInstallationSource.Steam
            || binding.Layout != WwmInstallationLayout.Package || snapshot == null
            || snapshot.SchemaVersion is not (1 or 2))
            return false;

        if (hasLegacyState || state?.SchemaVersion == 2)
        {
            if (state == null || snapshot == null
                || !string.Equals(state.PreHubSnapshotCycleId, snapshot.CycleOperationId, StringComparison.Ordinal)
                || (state.SchemaVersion == 2 && !StateMatchesBinding(state, binding))
                || !state.Targets.Select(item => item.RelativePath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .SequenceEqual(binding.ManagedRelativePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)
                || (!hasLegacyJournal && !await MatchesManifestAsync(binding.GameRoot, state.Targets, token).ConfigureAwait(false)))
                return false;
        }

        if (hasLegacyJournal)
        {
            if (journal == null
                || !LegacyJournalProvesSnapshotCycle(snapshot, journal)
                || !await MatchesJournalKnownTargetStatesAsync(binding.GameRoot, journal, token).ConfigureAwait(false))
                return false;
        }

        // The active journal must link itself to this snapshot cycle. State linkage,
        // orphan operation directories and transaction-known bytes do not prove journal ownership.
        if (hasLegacyJournal)
            return journal != null && LegacyJournalProvesSnapshotCycle(snapshot, journal);

        // Without an active legacy journal, only a directly linked committed state can own the snapshot.
        return (hasLegacyState || state?.SchemaVersion == 2) && state != null
            && string.Equals(state.PreHubSnapshotCycleId, snapshot.CycleOperationId, StringComparison.Ordinal);
    }

    private static bool LegacyJournalProvesSnapshotCycle(WwmPreHubSnapshot snapshot, WwmTransactionJournal journal)
    {
        if (journal.CreatedPreHubSnapshot
            && string.Equals(journal.OperationId, snapshot.CycleOperationId, StringComparison.Ordinal))
            return true;

        if (!journal.PreviousStateExisted || string.IsNullOrWhiteSpace(journal.PreviousStateBase64))
            return false;

        try
        {
            var bytes = Convert.FromBase64String(journal.PreviousStateBase64);
            var json = new System.Text.UTF8Encoding(false, true).GetString(bytes);
            return WwmStateStore.TryDeserializeState(json, out var previousState, out _)
                && string.Equals(previousState!.PreHubSnapshotCycleId, snapshot.CycleOperationId, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is FormatException or System.Text.DecoderFallbackException)
        {
            return false;
        }
    }

    private async Task<WwmMutationResult?> PersistAutomaticallyDetectedRootAsync(CancellationToken token)
    {
        if (_configStore == null)
            return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired,
                "Не вдалося зберегти підтверджений шлях Where Winds Meet; виберіть папку гри вручну.");
        var loaded = _configStore.Load();
        if (loaded.Status == FileLoadStatus.Invalid)
            return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired,
                "Налаштування шляху Where Winds Meet неможливо безпечно оновити; виберіть папку гри вручну.");
        var config = loaded.Value ?? new Config();
        var normalizedRoot = WwmGameDefinition.NormalizeInstallationRoot(CurrentBinding.GameRoot);
        if (!string.IsNullOrWhiteSpace(config.GamePath))
            return TrySameFullPath(config.GamePath, normalizedRoot)
                ? null
                : WwmMutationResult.Failure(WwmMutationError.RecoveryRequired,
                    "Збережений шлях Where Winds Meet відрізняється від автоматично знайденої гри; підтвердіть шлях вручну.");
        config.GamePath = normalizedRoot;
        try { await _configStore.SaveAsync(config, token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException)
        {
            if (ex is OperationCanceledException) throw;
            return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired,
                "Метадані WWM прив’язано, але шлях гри не вдалося зберегти. Повторіть запуск або виберіть папку вручну.");
        }
        return null;
    }

    private bool SnapshotCycleIsProven(WwmPreHubSnapshot snapshot, WwmInstallationState? state, WwmTransactionJournal? journal)
    {
        if (state != null && string.Equals(state.PreHubSnapshotCycleId, snapshot.CycleOperationId, StringComparison.Ordinal)) return true;
        if (Guid.TryParseExact(snapshot.CycleOperationId, "N", out _))
        {
            try
            {
                var orphanOperation = Path.GetFullPath(Path.Combine(_store.TransactionDirectory, snapshot.CycleOperationId));
                if (string.Equals(Path.GetDirectoryName(orphanOperation), Path.GetFullPath(_store.TransactionDirectory), StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(orphanOperation)
                    && (File.GetAttributes(orphanOperation) & FileAttributes.ReparsePoint) == 0)
                    return true;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
            {
                _logger.Debug($"WWM orphan snapshot operation evidence was rejected: {ex.Message}");
            }
        }
        if (journal == null) return false;
        if (journal.CreatedPreHubSnapshot && string.Equals(journal.OperationId, snapshot.CycleOperationId, StringComparison.Ordinal)) return true;
        if (!journal.PreviousStateExisted) return false;
        try
        {
            var bytes = Convert.FromBase64String(journal.PreviousStateBase64);
            var json = new System.Text.UTF8Encoding(false, true).GetString(bytes);
            return WwmStateStore.TryDeserializeState(json, out var previous, out _)
                && string.Equals(previous!.PreHubSnapshotCycleId, snapshot.CycleOperationId, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is FormatException or System.Text.DecoderFallbackException) { return false; }
    }

    private async Task SaveSnapshotMetadataAsync(WwmPreHubSnapshot snapshot, CancellationToken token)
    {
        var path = Path.Combine(_store.SnapshotDirectory, "snapshot.json");
        var temp = path + ".binding.tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(snapshot, JsonOptions), token).ConfigureAwait(false);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private async Task<List<WwmPreviousTarget>> CaptureTargetsAsync(string root, string operationDir, CancellationToken token)
    {
        var result = new List<WwmPreviousTarget>();
        for (var i = 0; i < CurrentTargets.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var relative = CurrentTargets[i];
            var target = SafeGameTarget(root, relative);
            if (!File.Exists(target)) { result.Add(new WwmPreviousTarget { RelativePath = relative, Existed = false }); continue; }
            var backupName = i + ".bin";
            var backupPath = Path.Combine(operationDir, backupName);
            await CopyFileAsync(target, backupPath, token).ConfigureAwait(false);
            result.Add(new WwmPreviousTarget
            {
                RelativePath = relative,
                Existed = true,
                SizeBytes = new FileInfo(backupPath).Length,
                Sha256 = await HashHelper.ComputeFileSha256Async(backupPath, token).ConfigureAwait(false),
                BackupFile = backupName,
                Attributes = (int)File.GetAttributes(target)
            });
        }
        return result;
    }

    private async Task<bool> RollbackAsync(string root, WwmTransactionJournal journal, string operationDir, CancellationToken token)
    {
        var phase = "prepare_rollback";
        string? activeTarget = null;
        try
        {
            foreach (var item in journal.PreviousTargets)
            {
                token.ThrowIfCancellationRequested();
                activeTarget = item.RelativePath;
                var target = SafeGameTarget(root, item.RelativePath);
                if (!item.Existed)
                {
                    phase = "delete_created_target";
                    if (File.Exists(target)) DeleteManagedTarget(target, nextPhase => phase = nextPhase);
                    continue;
                }
                phase = "validate_rollback_backup";
                var backup = SafeCombine(operationDir, item.BackupFile!);
                if (!File.Exists(backup) || new FileInfo(backup).Length != item.SizeBytes
                    || !string.Equals(HashHelper.ComputeFileSha256(backup), item.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Error($"WWM rollback backup failed integrity validation: {item.RelativePath}");
                    return false;
                }
                phase = "prepare_rollback_temp";
                var temp = PrepareGameTemp(root, item.RelativePath, journal.OperationId, ".rollback-");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                phase = "write_rollback_temp";
                await CopyFileAsync(backup, temp, token).ConfigureAwait(false);
                if (File.Exists(target))
                {
                    phase = "replace_target_from_rollback";
                    ReplaceManagedTarget(temp, target, item.Attributes, setPhase: nextPhase => phase = nextPhase);
                }
                else
                {
                    phase = "restore_absent_target_from_rollback";
                    File.Move(temp, target);
                    if (item.Attributes is { } attributes) File.SetAttributes(target, (FileAttributes)attributes);
                }
                phase = "verify_rollback_target";
                if (new FileInfo(target).Length != item.SizeBytes
                    || !string.Equals(await HashHelper.ComputeFileSha256Async(target, token).ConfigureAwait(false), item.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Error($"WWM rollback target verification failed: {item.RelativePath}");
                    return false;
                }
            }
            activeTarget = null;
            phase = "restore_previous_state";
            var oldState = journal.PreviousStateExisted ? Convert.FromBase64String(journal.PreviousStateBase64) : null;
            await RestoreStateBytesAsync(oldState, token).ConfigureAwait(false);
            phase = "cleanup_game_temps";
            return CleanupGameTemps(root, journal.OperationId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException or FormatException)
        {
            LogTransactionFailure(journal.OperationId, "rollback", activeTarget, phase, ex);
            CleanupGameTemps(root, journal.OperationId);
            return false;
        }
    }

    private string PrepareGameTemp(string root, string relativePath, string operationId, string kind)
    {
        if (!Guid.TryParseExact(operationId, "N", out _)
            || kind is not (".hub-" or ".rollback-"))
            throw new InvalidDataException("Invalid WWM game-temp ownership metadata.");

        var target = SafeGameTarget(root, relativePath);
        var targetDirectory = Path.GetDirectoryName(target)!;
        var tempPath = Path.GetFullPath(target + kind + operationId + ".tmp");
        if (!string.Equals(Path.GetDirectoryName(tempPath), Path.GetFullPath(targetDirectory), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("WWM game-temp path escaped the managed target directory.");

        DeleteOwnedGameTemp(tempPath);
        return tempPath;
    }

    private bool CleanupGameTemps(string root, string operationId)
    {
        if (!Guid.TryParseExact(operationId, "N", out _)) return false;
        foreach (var relativePath in CurrentTargets)
        {
            foreach (var kind in new[] { ".hub-", ".rollback-" })
            {
                try
                {
                    var target = SafeGameTarget(root, relativePath);
                    var temp = Path.GetFullPath(target + kind + operationId + ".tmp");
                    if (!string.Equals(Path.GetDirectoryName(temp), Path.GetFullPath(Path.GetDirectoryName(target)!), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("WWM game-temp path escaped the managed target directory.");
                    DeleteOwnedGameTemp(temp);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    LogTransactionFailure(operationId, "cleanup", relativePath, "cleanup_game_temp", ex);
                    return false;
                }
            }
        }
        return true;
    }

    private static void ReplaceManagedTarget(string replacement, string target, int? attributesToRestore,
        Action? afterReplace = null, Action<string>? setPhase = null)
    {
        setPhase?.Invoke("inspect_target_attributes");
        var originalAttributes = File.GetAttributes(target);
        var finalAttributes = attributesToRestore is { } stored
            ? (FileAttributes)stored
            : originalAttributes;
        if ((originalAttributes & FileAttributes.ReadOnly) != 0)
        {
            setPhase?.Invoke("clear_readonly_attribute");
            File.SetAttributes(target, originalAttributes & ~FileAttributes.ReadOnly);
        }
        setPhase?.Invoke("File.Replace");
        try
        {
            File.Replace(replacement, target, null);
            afterReplace?.Invoke();
        }
        catch
        {
            if (File.Exists(target) && (originalAttributes & FileAttributes.ReadOnly) != 0)
            {
                setPhase?.Invoke("restore_attributes_after_replace_failure");
                File.SetAttributes(target, originalAttributes);
            }
            throw;
        }
        setPhase?.Invoke("restore_target_attributes");
        File.SetAttributes(target, finalAttributes);
    }

    private static void DeleteManagedTarget(string target, Action<string>? setPhase = null)
    {
        setPhase?.Invoke("inspect_target_attributes");
        var attributes = File.GetAttributes(target);
        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            setPhase?.Invoke("clear_readonly_attribute");
            File.SetAttributes(target, attributes & ~FileAttributes.ReadOnly);
        }
        try
        {
            setPhase?.Invoke("File.Delete");
            File.Delete(target);
        }
        catch
        {
            if (File.Exists(target) && (attributes & FileAttributes.ReadOnly) != 0)
            {
                setPhase?.Invoke("restore_attributes_after_delete_failure");
                File.SetAttributes(target, attributes);
            }
            throw;
        }
    }

    private void LogTransactionFailure(string operationId, string operation, string? target, string phase, Exception exception)
    {
        _logger.Error($"WWM filesystem failure operation_id={operationId} operation={operation} target={target ?? "-"} phase={phase} exception={exception.GetType().Name} hresult=0x{unchecked((uint)exception.HResult):X8}{FormatWin32Error(exception)}");
    }

    private static string FormatWin32Error(Exception exception)
    {
        var hresult = unchecked((uint)exception.HResult);
        return (hresult & 0xFFFF0000) == 0x80070000 ? $" win32_error={hresult & 0xFFFF}" : string.Empty;
    }

    private static string GetFilesystemFailureMessage(Exception exception, string fallback)
        => exception is UnauthorizedAccessException
            ? "Не вдалося змінити файли Where Winds Meet. Закрийте гру та перевірте права доступу до її теки, а потім повторіть операцію."
            : fallback;

    private static void DeleteOwnedGameTemp(string tempPath)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(tempPath); }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) != 0)
            throw new InvalidDataException("Refusing to remove an unsafe WWM game-temp path.");
        File.Delete(tempPath);
    }

    private bool ValidateSnapshotFiles(WwmPreHubSnapshot snapshot)
    {
        if (snapshot.SchemaVersion == 2 && !WwmStateStore.IsValidBinding(
                snapshot.InstallationRoot, snapshot.TargetLayout, snapshot.InstallationFingerprint,
                snapshot.Targets?.Where(item => item != null).Select(item => item.RelativePath).ToArray() ?? Array.Empty<string>()))
            return false;

        if (snapshot.SchemaVersion is not (1 or 2) || !Guid.TryParseExact(snapshot.CycleOperationId, "N", out _)
            || snapshot.Targets == null || snapshot.Targets.Count != CurrentTargets.Length
            || snapshot.Targets.Any(item => item == null || !WwmStateStore.IsAllowedPath(item.RelativePath)
                || (item.Existed
                    ? item.SizeBytes <= 0 || !IsSha(item.Sha256)
                        || (item.Attributes is { } attributes && (((FileAttributes)attributes) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                        || item.BackupFile != (Array.IndexOf(CurrentTargets, item.RelativePath) + ".bin")
                    : item.SizeBytes != null || item.Sha256 != null || item.BackupFile != null || item.Attributes != null))
            || snapshot.Targets.Select(item => item.RelativePath).Distinct(StringComparer.Ordinal).Count() != CurrentTargets.Length
            || !SnapshotMatchesBinding(snapshot, CurrentBinding))
            return false;

        try
        {
            var expectedEntries = snapshot.Targets.Where(item => item.Existed).Select(item => item.BackupFile!)
                .Append("snapshot.json").ToHashSet(StringComparer.OrdinalIgnoreCase);
            var actualEntries = Directory.EnumerateFileSystemEntries(_store.SnapshotDirectory).ToList();
            if (actualEntries.Count != expectedEntries.Count
                || actualEntries.Any(path => !expectedEntries.Contains(Path.GetFileName(path))
                    || (File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0))
                return false;
            foreach (var item in snapshot.Targets.Where(item => item.Existed))
            {
                var path = SafeCombine(_store.SnapshotDirectory, item.BackupFile!);
                if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0
                    || new FileInfo(path).Length != item.SizeBytes
                    || !string.Equals(HashHelper.ComputeFileSha256(path), item.Sha256, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _logger.Warning($"WWM snapshot validation failed: {ex.Message}");
            return false;
        }
    }

    private WwmInstallationBinding CurrentBinding => _bindingContext.Value
        ?? throw new InvalidOperationException("WWM transaction binding is not active.");

    private string[] CurrentTargets => CurrentBinding.ManagedRelativePaths;

    private IDisposable PushBinding(string root, string? buildId, WwmInstallationBinding? explicitBinding)
    {
        var prior = _bindingContext.Value;
        var normalizedRoot = WwmGameDefinition.NormalizeInstallationRoot(root);
        if (explicitBinding != null)
        {
            if (!string.Equals(WwmGameDefinition.NormalizeInstallationRoot(explicitBinding.GameRoot), normalizedRoot, StringComparison.OrdinalIgnoreCase)
                || explicitBinding.Layout is not (WwmInstallationLayout.Package or WwmInstallationLayout.LocalDataPatch)
                || explicitBinding.ManagedRelativePaths.Any(path => WwmStateStore.GetLayoutForPath(path) != explicitBinding.Layout))
                throw new InvalidDataException("Invalid explicit WWM installation binding.");
            _bindingContext.Value = explicitBinding;
        }
        else if (prior == null || !string.Equals(WwmGameDefinition.NormalizeInstallationRoot(prior.GameRoot), normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            // The legacy overload remains Package-only for existing Steam callers/tests.
            _bindingContext.Value = new WwmInstallationBinding(normalizedRoot, WwmInstallationSource.Steam,
                WwmInstallationLayout.Package, buildId, WwmGameDefinition.ComputeInstallationFingerprint(normalizedRoot));
        }
        return new BindingScope(_bindingContext, prior);
    }

    private static WwmStagedPackage MapStagedTargets(WwmStagedPackage staged, WwmInstallationBinding binding)
    {
        var paths = binding.ManagedRelativePaths;
        var mapped = staged.Targets.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select((item, index) => new WwmTargetManifestEntry
            {
                RelativePath = paths[index], SizeBytes = item.SizeBytes, Sha256 = item.Sha256
            }).ToArray();
        return new WwmStagedPackage(staged.WorkDirectory, mapped, staged.Package);
    }

    private static bool StateMatchesBinding(WwmInstallationState state, WwmInstallationBinding binding)
    {
        if (state.SchemaVersion == 1)
            return HasTrustedLegacyRoot(binding) && binding.Source == WwmInstallationSource.Steam && binding.Layout == WwmInstallationLayout.Package
                && state.Targets.Select(item => item.RelativePath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .SequenceEqual(binding.ManagedRelativePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        if (!WwmStateStore.IsValidBinding(state.InstallationRoot, state.TargetLayout, state.InstallationFingerprint,
                state.Targets.Select(item => item.RelativePath).ToArray())) return false;
        return TrySameFullPath(state.InstallationRoot, binding.GameRoot)
            && string.Equals(state.TargetLayout, binding.Layout.ToString(), StringComparison.Ordinal)
            && state.Targets is { } stateTargets
            && stateTargets.All(item => WwmStateStore.GetLayoutForPath(item.RelativePath) == binding.Layout);
    }

    private static bool SnapshotMatchesBinding(WwmPreHubSnapshot snapshot, WwmInstallationBinding binding)
    {
        if (snapshot.SchemaVersion == 1)
            return HasTrustedLegacyRoot(binding) && binding.Source == WwmInstallationSource.Steam && binding.Layout == WwmInstallationLayout.Package
                && snapshot.Targets.Select(item => item.RelativePath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .SequenceEqual(binding.ManagedRelativePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        if (!WwmStateStore.IsValidBinding(snapshot.InstallationRoot, snapshot.TargetLayout, snapshot.InstallationFingerprint,
                snapshot.Targets?.Where(item => item != null).Select(item => item.RelativePath).ToArray() ?? Array.Empty<string>())) return false;
        return TrySameFullPath(snapshot.InstallationRoot, binding.GameRoot)
            && string.Equals(snapshot.TargetLayout, binding.Layout.ToString(), StringComparison.Ordinal)
            && snapshot.Targets is { } snapshotTargets
            && snapshotTargets.All(item => WwmStateStore.GetLayoutForPath(item.RelativePath) == binding.Layout);
    }

    private static bool JournalMatchesBinding(WwmTransactionJournal journal, WwmInstallationBinding binding)
    {
        var paths = journal.PreviousTargets.Select(item => item.RelativePath)
            .Concat(journal.TargetManifest.Select(item => item.RelativePath)).Concat(journal.ExpectedAbsentTargets).ToArray();
        if (journal.SchemaVersion == 1)
            return HasTrustedLegacyRoot(binding) && binding.Source == WwmInstallationSource.Steam && binding.Layout == WwmInstallationLayout.Package
                && paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2
                && paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .SequenceEqual(binding.ManagedRelativePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var uniquePaths = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!WwmStateStore.IsValidBinding(journal.InstallationRoot, journal.TargetLayout, journal.InstallationFingerprint, uniquePaths)) return false;
        return TrySameFullPath(journal.InstallationRoot, binding.GameRoot)
            && string.Equals(journal.TargetLayout, binding.Layout.ToString(), StringComparison.Ordinal)
            && string.Equals(journal.InstallationFingerprint, binding.Fingerprint, StringComparison.OrdinalIgnoreCase)
            && paths.All(path => WwmStateStore.GetLayoutForPath(path) == binding.Layout);
    }

    private static bool HasTrustedLegacyRoot(WwmInstallationBinding binding)
        => binding.Source == WwmInstallationSource.Steam
            && binding.Layout == WwmInstallationLayout.Package
            && ((!string.IsNullOrWhiteSpace(binding.LegacyPersistedRootEvidence)
                    && TrySameFullPath(binding.LegacyPersistedRootEvidence, binding.GameRoot))
                || binding.IsUniqueAutomaticSteamDetection);

    private static bool TrySameFullPath(string? left, string right)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(left)
                && string.Equals(WwmGameDefinition.NormalizeInstallationRoot(left),
                    WwmGameDefinition.NormalizeInstallationRoot(right), StringComparison.OrdinalIgnoreCase);
        }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static string GetSnapshotStaleMessage(WwmInstallationBinding binding)
        => binding.Source == WwmInstallationSource.Steam
            ? "Steam build відрізняється від build початкового знімка. Відновлення заблоковано, щоб не замінити файли новішої гри."
            : "Інсталяція Where Winds Meet змінилася після створення початкового знімка. Відновлення заблоковано, щоб не застосувати застарілі файли.";

    private static bool IsSnapshotStale(WwmPreHubSnapshot snapshot, WwmInstallationBinding binding)
        => binding.Source == WwmInstallationSource.Steam
            ? string.IsNullOrWhiteSpace(snapshot.GameBuildId) || string.IsNullOrWhiteSpace(binding.BuildId)
                || !string.Equals(snapshot.GameBuildId, binding.BuildId, StringComparison.Ordinal)
            : !string.Equals(snapshot.InstallationFingerprint, binding.Fingerprint, StringComparison.OrdinalIgnoreCase);

    private static bool IsStateStale(WwmInstallationState state, WwmInstallationBinding binding)
        => binding.Source != WwmInstallationSource.Steam
            && state.SchemaVersion == 2
            && !string.Equals(state.InstallationFingerprint, binding.Fingerprint, StringComparison.OrdinalIgnoreCase);

    private sealed class BindingScope(AsyncLocal<WwmInstallationBinding?> context, WwmInstallationBinding? prior) : IDisposable
    {
        public void Dispose() => context.Value = prior;
    }

    private async Task<bool> MatchesExpectedOutcomeAsync(string root, WwmTransactionJournal journal, CancellationToken token)
    {
        var stateMatches = journal.ExpectedStateSha256 == "absent"
            ? !File.Exists(_store.StateFile)
            : File.Exists(_store.StateFile) && string.Equals(
                await HashHelper.ComputeFileSha256Async(_store.StateFile, token).ConfigureAwait(false),
                journal.ExpectedStateSha256, StringComparison.OrdinalIgnoreCase);
        if (!stateMatches) return false;
        foreach (var item in journal.TargetManifest)
        {
            var path = SafeGameTarget(root, item.RelativePath);
            if (!File.Exists(path) || new FileInfo(path).Length != item.SizeBytes
                || !string.Equals(await HashHelper.ComputeFileSha256Async(path, token).ConfigureAwait(false), item.Sha256, StringComparison.OrdinalIgnoreCase)) return false;
        }
        foreach (var relative in journal.ExpectedAbsentTargets)
        {
            if (!WwmStateStore.IsAllowedPath(relative)) return false;
            var path = SafeGameTarget(root, relative);
            try { _ = File.GetAttributes(path); return false; }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return true;
    }

    private async Task<bool> MatchesPreviousOutcomeAsync(string root, WwmTransactionJournal journal, CancellationToken token)
    {
        try
        {
            if (journal.PreviousStateExisted)
            {
                if (!File.Exists(_store.StateFile)
                    || !File.ReadAllBytes(_store.StateFile).AsSpan().SequenceEqual(Convert.FromBase64String(journal.PreviousStateBase64)))
                {
                    LogPreviousOutcomeMismatch(journal, "state", "previous state bytes differ");
                    return false;
                }
            }
            else if (File.Exists(_store.StateFile))
            {
                LogPreviousOutcomeMismatch(journal, "state", "unexpected state file exists");
                return false;
            }

            foreach (var previous in journal.PreviousTargets)
            {
                token.ThrowIfCancellationRequested();
                var target = SafeGameTarget(root, previous.RelativePath);
                FileAttributes? attributes = null;
                try { attributes = File.GetAttributes(target); }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }

                if (attributes == null)
                {
                    if (previous.Existed)
                    {
                        LogPreviousOutcomeMismatch(journal, previous.RelativePath, "previous target is missing");
                        return false;
                    }
                    continue;
                }
                if ((attributes.Value & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 || !previous.Existed)
                {
                    LogPreviousOutcomeMismatch(journal, previous.RelativePath, "target type or existence differs");
                    return false;
                }
                if (previous.Attributes is { } expectedAttributes && (int)attributes.Value != expectedAttributes)
                {
                    LogPreviousOutcomeMismatch(journal, previous.RelativePath, "target attributes differ");
                    return false;
                }
                var size = new FileInfo(target).Length;
                if (size != previous.SizeBytes
                    || !string.Equals(await HashHelper.ComputeFileSha256Async(target, token).ConfigureAwait(false), previous.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    LogPreviousOutcomeMismatch(journal, previous.RelativePath, "target size or hash differs");
                    return false;
                }
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FormatException)
        {
            LogTransactionFailure(journal.OperationId, journal.Operation, null, "compare_previous_outcome", ex);
            return false;
        }
    }

    private void LogPreviousOutcomeMismatch(WwmTransactionJournal journal, string target, string reason)
        => _logger.Error($"WWM transaction stopped operation_id={journal.OperationId} operation={journal.Operation} target={target} phase=compare_previous_outcome reason={reason}");

    private async Task<bool> MatchesJournalKnownTargetStatesAsync(string root, WwmTransactionJournal journal, CancellationToken token)
    {
        var previousByPath = journal.PreviousTargets.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);
        var expectedByPath = journal.TargetManifest.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);
        var expectedAbsent = journal.ExpectedAbsentTargets.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var relative in CurrentTargets)
        {
            token.ThrowIfCancellationRequested();
            if (!previousByPath.TryGetValue(relative, out var previous))
            {
                _logger.Error($"WWM recovery journal has no previous target record for '{relative}'.");
                return false;
            }

            var target = SafeGameTarget(root, relative);
            FileAttributes? currentAttributes = null;
            try { currentAttributes = File.GetAttributes(target); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }

            if (currentAttributes is { } attributes
                && (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                _logger.Error($"WWM recovery refused an unexpected directory or reparse point at managed target '{relative}'.");
                return false;
            }

            var currentMatchesKnownState = false;
            if (currentAttributes == null)
            {
                currentMatchesKnownState = !previous.Existed || expectedAbsent.Contains(relative);
            }
            else
            {
                var size = new FileInfo(target).Length;
                var hash = await HashHelper.ComputeFileSha256Async(target, token).ConfigureAwait(false);
                var previousMatch = previous.Existed && size == previous.SizeBytes
                    && string.Equals(hash, previous.Sha256, StringComparison.OrdinalIgnoreCase);
                var expectedMatch = expectedByPath.TryGetValue(relative, out var expected)
                    && size == expected.SizeBytes
                    && string.Equals(hash, expected.Sha256, StringComparison.OrdinalIgnoreCase);
                currentMatchesKnownState = previousMatch || expectedMatch;
            }

            if (!currentMatchesKnownState)
            {
                _logger.Error($"WWM recovery refused to overwrite target not matching any journal-known state: '{relative}'.");
                return false;
            }
        }

        return true;
    }

    private async Task<bool> MatchesManifestAsync(string root, IReadOnlyList<WwmTargetManifestEntry> manifest, CancellationToken token)
    {
        if (manifest.Count != 2 || !WwmStateStore.ValidateTargets(manifest)) return false;
        try
        {
            foreach (var item in manifest)
            {
                var path = SafeGameTarget(root, item.RelativePath);
                if (!File.Exists(path) || new FileInfo(path).Length != item.SizeBytes
                    || !string.Equals(await HashHelper.ComputeFileSha256Async(path, token).ConfigureAwait(false), item.Sha256, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _logger.Warning($"WWM installed manifest could not be verified: {ex.Message}");
            return false;
        }
    }

    private static async Task CopyVerifiedAsync(string source, string destination, WwmTargetManifestEntry expected, CancellationToken token)
    {
        await CopyFileAsync(source, destination, token).ConfigureAwait(false);
        await VerifyFileAsync(destination, expected, token).ConfigureAwait(false);
    }

    private static async Task VerifyFileAsync(string path, WwmTargetManifestEntry expected, CancellationToken token)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != expected.SizeBytes
            || !string.Equals(await HashHelper.ComputeFileSha256Async(path, token).ConfigureAwait(false), expected.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"WWM target verification failed: {expected.RelativePath}");
    }

    private async Task RestoreStateBytesAsync(byte[]? bytes, CancellationToken token)
    {
        if (bytes == null) { if (File.Exists(_store.StateFile)) File.Delete(_store.StateFile); return; }
        var temp = _store.StateFile + ".rollback.tmp";
        await File.WriteAllBytesAsync(temp, bytes, token).ConfigureAwait(false);
        if (File.Exists(_store.StateFile)) File.Replace(temp, _store.StateFile, null); else File.Move(temp, _store.StateFile);
    }

    private string SafeGameTarget(string root, string relative)
    {
        if (!WwmStateStore.IsAllowedPath(relative)) throw new InvalidDataException("WWM target is not allowlisted.");
        if (WwmStateStore.GetLayoutForPath(relative) != CurrentBinding.Layout)
            throw new InvalidDataException("WWM target does not belong to the selected installation layout.");
        var normalizedRoot = WwmGameDefinition.NormalizeInstallationRoot(root);
        var fullRoot = Path.EndsInDirectorySeparator(normalizedRoot) ? normalizedRoot : normalizedRoot + Path.DirectorySeparatorChar;
        RejectReparsePoint(normalizedRoot);
        var target = Path.GetFullPath(Path.Combine(normalizedRoot, relative));
        if (!target.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("WWM target escaped game root.");
        var localeRelative = CurrentBinding.Layout == WwmInstallationLayout.Package
            ? WwmGameDefinition.Default.LocaleRelativePath : WwmGameDefinition.Default.LocalDataPatchLocaleRelativePath;
        var normalizedLocale = WwmGameDefinition.NormalizeInstallationRoot(Path.Combine(normalizedRoot, localeRelative));
        var locale = Path.EndsInDirectorySeparator(normalizedLocale) ? normalizedLocale : normalizedLocale + Path.DirectorySeparatorChar;
        if (!target.StartsWith(locale, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("WWM target escaped locale directory.");
        var current = normalizedRoot;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current) || File.Exists(current)) RejectReparsePoint(current);
        }
        return target;
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("WWM managed path contains a reparse point.");
    }

    private static string SafeCombine(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains("..", StringComparison.Ordinal) || relative.Contains(':'))
            throw new InvalidDataException("Unsafe WWM persistence path.");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var value = Path.GetFullPath(Path.Combine(root, relative));
        if (!value.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("WWM persistence path escaped its root.");
        return value;
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken token)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await input.CopyToAsync(output, token).ConfigureAwait(false);
        await output.FlushAsync(token).ConfigureAwait(false);
    }

    private static bool IsSha(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private void DeleteDirectoryBestEffort(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (Exception ex) { _logger.Warning($"WWM recovery data cleanup deferred for '{path}': {ex.Message}"); }
    }

    private bool TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            return !Directory.Exists(path);
        }
        catch (Exception ex)
        {
            _logger.Warning($"WWM directory cleanup deferred for '{path}': {ex.Message}");
            return false;
        }
    }
}
