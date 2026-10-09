using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    internal Action<int>? AfterTargetAppliedForTest { get; set; }
    internal Action? BeforeStateSaveForTest { get; set; }
    internal Action? BeforeRestoreJournalForTest { get; set; }

    public WwmInstallService(HttpClient httpClient, GamePersistencePaths persistencePaths, WwmStateStore store, ILogger logger,
        WwmGameDefinition? definition = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _persistencePaths = persistencePaths ?? throw new ArgumentNullException(nameof(persistencePaths));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _definition = definition ?? WwmGameDefinition.Default;
    }

    public async Task<WwmMutationResult> RecoverAsync(string gameRoot, string? currentBuildId, CancellationToken token = default)
    {
        if (!_definition.ValidateGameRoot(gameRoot)) return WwmMutationResult.Failure(WwmMutationError.InvalidRoot, "Перевірений шлях Where Winds Meet недоступний.");
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

        if (journal.GameBuildId != null && !string.Equals(journal.GameBuildId, currentBuildId, StringComparison.Ordinal))
            return WwmMutationResult.Failure(WwmMutationError.SnapshotStale, "Steam build змінився під час незавершеної операції; потрібна ручна перевірка.");

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
        _logger.Warning("Recovered incomplete WWM transaction to its exact pre-operation state.");
        return WwmMutationResult.Success();
    }

    public async Task<WwmMutationResult> InstallAsync(string gameRoot, string? buildId, WwmApiPackage package,
        CancellationToken cancellationToken = default)
    {
        if (!_definition.ValidateGameRoot(gameRoot)) return WwmMutationResult.Failure(WwmMutationError.InvalidRoot, "Перевірений шлях Where Winds Meet недоступний.");
        var recovered = await RecoverAsync(gameRoot, buildId, cancellationToken).ConfigureAwait(false);
        if (!recovered.IsSuccess) return recovered;

        var state = _store.Load(out var stateError);
        if (stateError != null) return WwmMutationResult.Failure(WwmMutationError.InvalidState, "Стан WWM пошкоджено; встановлення заблоковано.");
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
            if (state != null && !await MatchesManifestAsync(gameRoot, state.Targets, cancellationToken).ConfigureAwait(false))
                return WwmMutationResult.Failure(WwmMutationError.ModifiedFiles, "Файли локалізації змінилися під час підготовки пакета. Операцію заблоковано.");
            var operationId = Guid.NewGuid().ToString("N");
            var snapshotResult = await EnsurePreHubSnapshotAsync(gameRoot, buildId, state != null, operationId, cancellationToken).ConfigureAwait(false);
            if (snapshotResult.Snapshot == null)
                return WwmMutationResult.Failure(WwmMutationError.Snapshot, "Знімок попереднього стану відсутній або не належить активному циклу Хабу. Встановлення заблоковано.");
            return await RunTransactionAsync(gameRoot, buildId, staged, snapshotResult.Snapshot,
                snapshotResult.Created, operationId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            new WwmPackageService(_httpClient, _persistencePaths, _logger).Cleanup(staged.WorkDirectory);
        }
    }

    public async Task<WwmMutationResult> RestorePreHubAsync(string gameRoot, string? currentBuildId, CancellationToken token = default)
    {
        if (!_definition.ValidateGameRoot(gameRoot)) return WwmMutationResult.Failure(WwmMutationError.InvalidRoot, "Перевірений шлях Where Winds Meet недоступний.");
        var recovered = await RecoverAsync(gameRoot, currentBuildId, token).ConfigureAwait(false);
        if (!recovered.IsSuccess) return recovered;
        var state = _store.Load(out var stateError);
        if (stateError != null) return WwmMutationResult.Failure(WwmMutationError.InvalidState, "Стан WWM пошкоджено; відновлення заблоковано.");
        if (state == null) return WwmMutationResult.Failure(WwmMutationError.InvalidState, "Керований стан WWM не знайдено.");
        if (!await MatchesManifestAsync(gameRoot, state.Targets, token).ConfigureAwait(false))
            return WwmMutationResult.Failure(WwmMutationError.ModifiedFiles, "Керовані файли змінені поза Хабом; безпечне відновлення заблоковано.");

        var snapshot = LoadPreHubSnapshot(out var snapshotError);
        if (snapshot == null) return WwmMutationResult.Failure(WwmMutationError.Snapshot, snapshotError ?? "Знімок попереднього стану відсутній або пошкоджений.");
        if (string.IsNullOrWhiteSpace(snapshot.GameBuildId) || string.IsNullOrWhiteSpace(currentBuildId)
            || !string.Equals(snapshot.GameBuildId, currentBuildId, StringComparison.Ordinal))
            return WwmMutationResult.Failure(WwmMutationError.SnapshotStale, "Steam build відрізняється від build знімка. Відновлення заблоковано, щоб не замінити файли новішої гри.");
        return await RestoreSnapshotTransactionAsync(gameRoot, currentBuildId, snapshot, token).ConfigureAwait(false);
    }

    public async Task<WwmInstalledStateResult> ResolveStateAsync(string gameRoot, WwmApiPackage? current, CancellationToken token = default)
    {
        var state = _store.Load(out var error);
        if (error != null) return new(WwmInstalledStateKind.Modified, "Збережений стан WWM пошкоджено.");
        if (state == null) return new(WwmInstalledStateKind.Unknown);
        if (!await MatchesManifestAsync(gameRoot, state.Targets, token).ConfigureAwait(false))
            return new(WwmInstalledStateKind.Modified, "Файли локалізації змінено або втрачено.");
        if (current != null && state.ModeSlug == current.Mode.Slug && state.ModeVariant == current.Mode.Variant
            && state.ReleaseVersion == current.Version && state.ArchiveSizeBytes == current.SizeBytes
            && state.ArchiveSha256 == current.Sha256)
            return new(WwmInstalledStateKind.Current);
        return new(WwmInstalledStateKind.UpdateAvailable);
    }

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
        try
        {
            Directory.CreateDirectory(operationDir);
            var previousState = File.Exists(_store.StateFile) ? await File.ReadAllBytesAsync(_store.StateFile, token).ConfigureAwait(false) : null;
            var previous = await CaptureTargetsAsync(gameRoot, operationDir, token).ConfigureAwait(false);
            var newState = new WwmInstallationState
            {
                ModeSlug = staged.Package.Mode.Slug,
                ModeVariant = staged.Package.Mode.Variant,
                ReleaseVersion = staged.Package.Version,
                ArchiveSizeBytes = staged.Package.SizeBytes,
                ArchiveSha256 = staged.Package.Sha256,
                PreHubSnapshotCycleId = snapshot.CycleOperationId,
                GameBuildId = buildId,
                InstalledAt = DateTimeOffset.UtcNow,
                Targets = staged.Targets.ToList()
            };
            var stateJson = WwmStateStore.SerializeState(newState);
            journal = new WwmTransactionJournal
            {
                OperationId = operationId,
                Operation = "install",
                CreatedPreHubSnapshot = createdPreHubSnapshot,
                PreviousStateExisted = previousState != null,
                PreviousStateBase64 = previousState == null ? "" : Convert.ToBase64String(previousState),
                PreviousTargets = previous,
                TargetManifest = staged.Targets.ToList(),
                ExpectedStateSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stateJson))).ToLowerInvariant(),
                GameBuildId = buildId
            };
            await _store.WriteJournalAsync(journal, token).ConfigureAwait(false);

            var appliedTargets = 0;
            foreach (var item in staged.Targets)
            {
                token.ThrowIfCancellationRequested();
                mutationStarted = true;
                var source = SafeCombine(Path.Combine(staged.WorkDirectory, "staged"), item.RelativePath);
                var target = SafeGameTarget(gameRoot, item.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var replacement = PrepareGameTemp(gameRoot, item.RelativePath, operationId, ".hub-");
                await CopyVerifiedAsync(source, replacement, item, token).ConfigureAwait(false);
                if (File.Exists(target)) File.Replace(replacement, target, null);
                else File.Move(replacement, target);
                await VerifyFileAsync(target, item, token).ConfigureAwait(false);
                AfterTargetAppliedForTest?.Invoke(++appliedTargets);
            }

            BeforeStateSaveForTest?.Invoke();
            await _store.SaveAsync(newState, token).ConfigureAwait(false);
            if (!await MatchesExpectedOutcomeAsync(gameRoot, journal, token).ConfigureAwait(false))
                throw new IOException("WWM transaction post-commit verification failed.");
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
                if (journal != null && !await RetireUnmutatedTransactionAsync(operationDir, createdPreHubSnapshot, operationId).ConfigureAwait(false))
                    return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Підготовку скасовано, але тимчасові дані WWM ще потребують відновлення.");
                if (journal == null)
                {
                    DeleteDirectoryBestEffort(operationDir);
                    if (createdPreHubSnapshot && !RetireCreatedSnapshot(operationId))
                        return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Підготовку скасовано, але новий знімок не вдалося безпечно прибрати.");
                }
                return WwmMutationResult.Failure(WwmMutationError.Cancelled, "Операцію скасовано.");
            }
            var rolledBack = await CompleteRollbackAsync(gameRoot, journal, operationDir).ConfigureAwait(false);
            return WwmMutationResult.Failure(rolledBack ? WwmMutationError.Cancelled : WwmMutationError.RollbackFailed,
                rolledBack ? "Операцію скасовано; попередній стан відновлено." : "Критична помилка: не вдалося відкотити файли WWM.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _logger.Error($"WWM transaction failed: {ex.Message}");
            if (!mutationStarted || journal == null)
            {
                if (journal != null)
                {
                    if (!await RetireUnmutatedTransactionAsync(operationDir, createdPreHubSnapshot, operationId).ConfigureAwait(false))
                        return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Підготовка WWM не завершилась; безпечне очищення буде повторено під час відновлення.");
                }
                else
                {
                    DeleteDirectoryBestEffort(operationDir);
                    if (createdPreHubSnapshot && !RetireCreatedSnapshot(operationId))
                        return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Підготовка WWM не завершилась; новий знімок не вдалося безпечно прибрати.");
                }
                return WwmMutationResult.Failure(WwmMutationError.Mutation, "Операцію WWM не вдалося підготувати; файли гри не змінено.");
            }
            var rolledBack = await CompleteRollbackAsync(gameRoot, journal, operationDir).ConfigureAwait(false);
            return WwmMutationResult.Failure(rolledBack ? WwmMutationError.Mutation : WwmMutationError.RollbackFailed,
                rolledBack ? "Не вдалося встановити пакет; попередній стан відновлено." : "Критична помилка: відкат WWM не завершився. Дані відновлення збережено.");
        }
    }

    private async Task<bool> RetireUnmutatedTransactionAsync(string operationDir, bool createdPreHubSnapshot, string operationId)
    {
        try
        {
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
        var committed = false;
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
                OperationId = id,
                Operation = "restore",
                PreviousStateExisted = previousState != null,
                PreviousStateBase64 = previousState == null ? "" : Convert.ToBase64String(previousState),
                PreviousTargets = previousTargets,
                TargetManifest = restoredTargets,
                ExpectedAbsentTargets = absent,
                GameBuildId = buildId,
                ExpectedStateSha256 = "absent"
            };
            BeforeRestoreJournalForTest?.Invoke();
            await _store.WriteJournalAsync(journal, token).ConfigureAwait(false);

            foreach (var item in snapshot.Targets)
            {
                token.ThrowIfCancellationRequested();
                var target = SafeGameTarget(gameRoot, item.RelativePath);
                if (!item.Existed)
                {
                    if (File.Exists(target))
                    {
                        mutationStarted = true;
                        File.Delete(target);
                    }
                    continue;
                }
                var expected = restoredTargets.Single(entry => entry.RelativePath == item.RelativePath);
                var replacement = PrepareGameTemp(gameRoot, item.RelativePath, id, ".hub-");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                mutationStarted = true;
                await CopyVerifiedAsync(SafeCombine(_store.SnapshotDirectory, item.BackupFile!), replacement, expected, token).ConfigureAwait(false);
                if (File.Exists(target)) File.Replace(replacement, target, null); else File.Move(replacement, target);
                await VerifyFileAsync(target, expected, token).ConfigureAwait(false);
            }
            await _store.ClearAsync(token).ConfigureAwait(false);
            if (!await MatchesExpectedOutcomeAsync(gameRoot, journal, token).ConfigureAwait(false))
                throw new IOException("WWM restore verification failed.");
            committed = true;
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
                    : await RetireUnmutatedTransactionAsync(dir, createdPreHubSnapshot: false, id).ConfigureAwait(false);
                return WwmMutationResult.Failure(cleaned ? WwmMutationError.Cancelled : WwmMutationError.RecoveryRequired,
                    cleaned ? "Операцію відновлення скасовано до зміни файлів." : "Відновлення скасовано; безпечне очищення буде повторено.");
            }
            var rollback = journal != null && await CompleteRollbackAsync(gameRoot, journal, dir).ConfigureAwait(false);
            return WwmMutationResult.Failure(rollback ? WwmMutationError.Cancelled : WwmMutationError.RollbackFailed,
                rollback ? "Відновлення скасовано; стан перед операцією повернуто." : "Критична помилка відновлення WWM; журнал збережено.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            _logger.Error($"WWM pre-Hub restore failed: {ex.Message}");
            if (committed)
                return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, "Попередній стан уже відновлено, але очищення транзакції не завершилося. Відновлення повториться під час наступного запуску.");
            if (!mutationStarted)
            {
                var cleaned = journal == null
                    ? CleanupUnmutatedOperationDirectory(dir)
                    : await RetireUnmutatedTransactionAsync(dir, createdPreHubSnapshot: false, id).ConfigureAwait(false);
                return WwmMutationResult.Failure(cleaned ? WwmMutationError.Snapshot : WwmMutationError.RecoveryRequired,
                    cleaned ? "Не вдалося підготувати відновлення; файли гри не змінено." : "Підготовка не завершилася; безпечне очищення буде повторено.");
            }
            var rollback = journal != null && await CompleteRollbackAsync(gameRoot, journal, dir).ConfigureAwait(false);
            return WwmMutationResult.Failure(rollback ? WwmMutationError.Mutation : WwmMutationError.RollbackFailed,
                rollback ? "Не вдалося відновити попередній стан; файли повернуто до стану перед операцією." : "Критична помилка відновлення WWM; журнал збережено.");
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
            foreach (var relative in _definition.ManagedRelativePaths)
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
                targets.Add(new WwmPreviousTarget { RelativePath = relative, Existed = true, SizeBytes = size, Sha256 = sha, BackupFile = name });
                temporaryFiles.Add((temp, final));
                index++;
            }
            foreach (var pair in temporaryFiles) File.Move(pair.Temp, pair.Final, overwrite: false);
            var snapshot = new WwmPreHubSnapshot { CycleOperationId = cycleOperationId, GameBuildId = buildId, Targets = targets };
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
            var activeSnapshot = LoadPreHubSnapshot(out _);
            return activeSnapshot != null && string.Equals(activeSnapshot.CycleOperationId, state.PreHubSnapshotCycleId, StringComparison.Ordinal)
                ? WwmMutationResult.Success()
                : WwmMutationResult.Failure(WwmMutationError.RecoveryRequired,
                    "Керований стан WWM не має відповідного знімка активного циклу; встановлення та відновлення заблоковано.");
        }
        if (!Directory.Exists(_store.SnapshotDirectory)) return WwmMutationResult.Success();

        var snapshot = LoadPreHubSnapshot(out var snapshotError);
        if (snapshot == null)
            return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired,
                "Знімок WWM існує без керованого стану й не має валідного доказу незавершеного циклу; потрібна ручна перевірка.");
        if (snapshot.GameBuildId != null && !string.Equals(snapshot.GameBuildId, currentBuildId, StringComparison.Ordinal))
            return WwmMutationResult.Failure(WwmMutationError.SnapshotStale, "Steam build змінився після створення незавершеного знімка WWM.");
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

    private async Task<List<WwmPreviousTarget>> CaptureTargetsAsync(string root, string operationDir, CancellationToken token)
    {
        var result = new List<WwmPreviousTarget>();
        for (var i = 0; i < _definition.ManagedRelativePaths.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var relative = _definition.ManagedRelativePaths[i];
            var target = SafeGameTarget(root, relative);
            if (!File.Exists(target)) { result.Add(new WwmPreviousTarget { RelativePath = relative, Existed = false }); continue; }
            var backupName = i + ".bin";
            var backupPath = Path.Combine(operationDir, backupName);
            await CopyFileAsync(target, backupPath, token).ConfigureAwait(false);
            result.Add(new WwmPreviousTarget { RelativePath = relative, Existed = true, SizeBytes = new FileInfo(backupPath).Length,
                Sha256 = await HashHelper.ComputeFileSha256Async(backupPath, token).ConfigureAwait(false), BackupFile = backupName });
        }
        return result;
    }

    private async Task<bool> RollbackAsync(string root, WwmTransactionJournal journal, string operationDir, CancellationToken token)
    {
        try
        {
            foreach (var item in journal.PreviousTargets)
            {
                token.ThrowIfCancellationRequested();
                var target = SafeGameTarget(root, item.RelativePath);
                if (!item.Existed)
                {
                    if (File.Exists(target)) File.Delete(target);
                    continue;
                }
                var backup = SafeCombine(operationDir, item.BackupFile!);
                if (!File.Exists(backup) || new FileInfo(backup).Length != item.SizeBytes
                    || !string.Equals(HashHelper.ComputeFileSha256(backup), item.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Error($"WWM rollback backup failed integrity validation: {item.RelativePath}");
                    return false;
                }
                var temp = PrepareGameTemp(root, item.RelativePath, journal.OperationId, ".rollback-");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await CopyFileAsync(backup, temp, token).ConfigureAwait(false);
                if (File.Exists(target)) File.Replace(temp, target, null); else File.Move(temp, target);
                if (new FileInfo(target).Length != item.SizeBytes
                    || !string.Equals(await HashHelper.ComputeFileSha256Async(target, token).ConfigureAwait(false), item.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Error($"WWM rollback target verification failed: {item.RelativePath}");
                    return false;
                }
            }
            var oldState = journal.PreviousStateExisted ? Convert.FromBase64String(journal.PreviousStateBase64) : null;
            await RestoreStateBytesAsync(oldState, token).ConfigureAwait(false);
            return CleanupGameTemps(root, journal.OperationId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException or FormatException)
        {
            _logger.Error($"WWM rollback failed: {ex.Message}");
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
        try
        {
            foreach (var relativePath in _definition.ManagedRelativePaths)
            {
                foreach (var kind in new[] { ".hub-", ".rollback-" })
                {
                    var target = SafeGameTarget(root, relativePath);
                    var temp = Path.GetFullPath(target + kind + operationId + ".tmp");
                    if (!string.Equals(Path.GetDirectoryName(temp), Path.GetFullPath(Path.GetDirectoryName(target)!), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("WWM game-temp path escaped the managed target directory.");
                    DeleteOwnedGameTemp(temp);
                }
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _logger.Warning($"WWM operation temp cleanup deferred: {ex.Message}");
            return false;
        }
    }

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
        if (snapshot.SchemaVersion != 1 || !Guid.TryParseExact(snapshot.CycleOperationId, "N", out _)
            || snapshot.Targets == null || snapshot.Targets.Count != _definition.ManagedRelativePaths.Length
            || snapshot.Targets.Any(item => item == null || !WwmStateStore.IsAllowedPath(item.RelativePath)
                || (item.Existed
                    ? item.SizeBytes <= 0 || !IsSha(item.Sha256)
                        || item.BackupFile != (Array.IndexOf(_definition.ManagedRelativePaths, item.RelativePath) + ".bin")
                    : item.SizeBytes != null || item.Sha256 != null || item.BackupFile != null))
            || snapshot.Targets.Select(item => item.RelativePath).Distinct(StringComparer.Ordinal).Count() != _definition.ManagedRelativePaths.Length)
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

    private async Task<bool> MatchesJournalKnownTargetStatesAsync(string root, WwmTransactionJournal journal, CancellationToken token)
    {
        var previousByPath = journal.PreviousTargets.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);
        var expectedByPath = journal.TargetManifest.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);
        var expectedAbsent = journal.ExpectedAbsentTargets.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var relative in _definition.ManagedRelativePaths)
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

    private static string SafeGameTarget(string root, string relative)
    {
        if (!WwmStateStore.IsAllowedPath(relative)) throw new InvalidDataException("WWM target is not allowlisted.");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        RejectReparsePoint(Path.GetFullPath(root));
        var target = Path.GetFullPath(Path.Combine(root, relative));
        if (!target.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("WWM target escaped game root.");
        var locale = Path.GetFullPath(Path.Combine(root, WwmGameDefinition.Default.LocaleRelativePath)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(locale, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("WWM target escaped locale directory.");
        var current = Path.GetFullPath(root);
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
