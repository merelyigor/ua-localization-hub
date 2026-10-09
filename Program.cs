using System.Windows.Forms;
using BdoClient.Logging;
using BdoClient.Services;
using BdoClient.Storage;
using BdoClient.Update;

namespace BdoClient;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var commandLine = ApplicationCommandLine.Parse(args);

        switch (commandLine.Mode)
        {
            case CommandLineMode.ApplyUpdate:
                RunHelperMode(commandLine.ApplyUpdateSessionId!);
                return;

            case CommandLineMode.InvalidApplyUpdate:
                Console.Error.WriteLine("Invalid --apply-update arguments. Expected: --apply-update <session-id>");
                Environment.Exit(ApplicationCommandLine.ExitCodeInvalidArgs);
                return;

            case CommandLineMode.Normal:
            default:
                RunNormalModeEntry(commandLine.StartInBackground);
                return;
        }
    }

    private static void RunNormalModeEntry(bool startInBackground)
    {
        // Single-instance gate for normal clients only. The self-update helper
        // (ApplyUpdate / InvalidApplyUpdate) must never acquire this coordinator.
        // Command line is already parsed above, so this runs before any MainForm,
        // HTTP client, release poller or startup lifecycle is created.
        using var coordinator = new SingleInstanceCoordinator();

        if (!coordinator.IsPrimary)
        {
            // A primary normal/background instance already exists. A manual launch
            // asks the primary to restore; a background launch stays silent.
            if (!startInBackground)
                coordinator.SignalActivation();

            return;
        }

        RunNormalMode(startInBackground, coordinator);
    }

    private static void RunHelperMode(string sessionId)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var paths = new AppPaths();
        paths.EnsureGlobalDirectories();
        ILogger log = new FileLogger(paths.LogsDir);

        log.Info($"Self-update helper mode: session={sessionId}");
        var sessionStore = new UpdateSessionStore(paths, log);
        var applier = new SelfUpdateApplier(sessionStore, log);

        using var progressForm = new UpdateApplyingForm(async () =>
        {
            try
            {
                return await applier.RunAsync(sessionId);
            }
            catch (Exception ex)
            {
                log.Error($"Self-update helper unexpected failure: {ex.Message}");
                return SelfUpdateApplier.ExitCodeReplaceFailed;
            }
        });
        Application.Run(progressForm);
        var exitCode = progressForm.ExitCode;
        log.Info($"Self-update helper exited with code {exitCode}");

        ShowHelperResultMessage(exitCode);

        Environment.Exit(exitCode);
    }

    private static void ShowHelperResultMessage(int exitCode)
    {
        if (exitCode == SelfUpdateApplier.ExitCodeSuccess)
            return;

        var message = exitCode switch
        {
            SelfUpdateApplier.ExitCodeRestartFailedRecovered =>
                "Не вдалося застосувати оновлення.\nПопередню версію відновлено та запущено.",
            SelfUpdateApplier.ExitCodeParentTimeout =>
                "Не вдалося застосувати оновлення: попередній процес не завершився вчасно.\nПоточна версія не змінена.",
            SelfUpdateApplier.ExitCodeVerificationFailed =>
                "Не вдалося застосувати оновлення: перевірка цілісності не пройдена.\nВідкрийте папку журналів для деталей.",
            SelfUpdateApplier.ExitCodeReplaceFailed =>
                "Не вдалося застосувати оновлення: помилка заміни файлу.\nВідкрийте папку журналів для деталей.",
            SelfUpdateApplier.ExitCodeRestartFailed =>
                "Не вдалося застосувати оновлення та автоматично відновити запуск програми.\nВідкрийте папку журналів для деталей.",
            _ =>
                "Не вдалося застосувати оновлення.\nВідкрийте папку журналів для деталей."
        };

        try
        {
            MessageBox.Show(message, "Оновлення", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch
        {
            Console.Error.WriteLine(message);
        }
    }

    private static void RunNormalMode(bool startInBackground, SingleInstanceCoordinator coordinator)
    {
        var appPaths = new AppPaths();
        appPaths.EnsureGlobalDirectories();

        ILogger logger = new FileLogger(appPaths.LogsDir);
        var applicationConfigStore = new ApplicationConfigStore(appPaths, logger);
        var appVersionInfo = AppVersionInfo.Detect();
        var gameDefinition = BdoGameDefinition.Default;
        var gameCatalog = GameCatalog.Create(gameDefinition);
        IGameSession CreateSession(GameDescriptor descriptor)
        {
            if (string.Equals(descriptor.Id, gameDefinition.Id, StringComparison.Ordinal))
                return BdoGameSession.CreateProduction(appPaths, logger, appVersionInfo);
            if (string.Equals(descriptor.Id, WwmGameDefinition.Default.Id, StringComparison.Ordinal))
                return WwmGameSession.CreateProduction(appPaths, logger);
            throw new InvalidOperationException($"Unsupported production game '{descriptor.Id}'.");
        }

        var initialSession = CreateSession(gameCatalog.DefaultGame);
        using var gameSessionHost = new SelectedGameSessionHost(initialSession, CreateSession);

        logger.Info($"Application started. version={appVersionInfo.RawVersion}");

        var gitHubHttpClient = new HttpClient(new HttpClientHandler
        {
            UseProxy = false
        });
        var gitHubClient = new GitHubUpdateClient(gitHubHttpClient, logger);
        var selectionPolicy = new UpdateSelectionPolicy(logger);

        var autostartService = new WindowsAutostartService(Application.ExecutablePath, logger);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            Application.Run(new MainForm(
                applicationConfigStore, gameCatalog, gameSessionHost, logger,
                appVersionInfo, gitHubClient, selectionPolicy, appPaths,
                autostartService, startInBackground, coordinator));
        }
        catch (Exception ex)
        {
            logger.Error($"Unhandled application exception: {ex.Message}");
            throw;
        }
        finally
        {
            coordinator.Dispose();
            logger.Info("Application exited.");
        }
    }
}
