using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Windows.Forms;
using BdoClient.Logging;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace BdoClient.Services;

internal interface IAppNotificationService
{
    event Action<string?>? NotificationActivated;
    event Action<string, string>? NotificationFailed;

    bool IsRegistered { get; }

    bool TryShow(string type, string identity, string title, string body);
}

internal sealed class WindowsAppNotificationService : IAppNotificationService, IDisposable
{
    private const string AppUserModelId = "MerelyIhor.BDO-UA-Client";
    private static readonly PropertyKey AppUserModelIdProperty = new(
        new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
    private static readonly PropertyKey AppUserModelDisplayNameProperty = new(
        new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 4);

    private readonly ILogger _logger;
    private readonly HashSet<ToastNotification> _activeNotifications = new();
    private readonly object _notificationLock = new();
    private ToastNotifier? _notifier;
    private bool _disposed;

    public event Action<string?>? NotificationActivated;
    public event Action<string, string>? NotificationFailed;

    public bool IsRegistered => _notifier != null;

    public WindowsAppNotificationService(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool Register()
    {
        if (_notifier != null)
            return true;

        try
        {
            EnsureStartMenuShortcut();
            _notifier = ToastNotificationManager.CreateToastNotifier(AppUserModelId);
            _logger.Info($"Windows toast notifications initialized: app_id={AppUserModelId} setting={_notifier.Setting}.");
            return true;
        }
        catch (Exception ex)
        {
            _notifier = null;
            _logger.Error($"Windows toast notification initialization failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public bool TryShow(string type, string identity, string title, string body)
    {
        if (_notifier == null)
        {
            _logger.Warning($"Windows toast not submitted: type={type} identity={identity} reason=not_initialized.");
            return false;
        }

        ToastNotification? notification = null;
        try
        {
            var setting = _notifier.Setting;
            if (setting != NotificationSetting.Enabled)
            {
                _logger.Warning($"Windows toast not submitted: type={type} identity={identity} reason=notifications_{setting.ToString().ToLowerInvariant()}.");
                return false;
            }

            var content = AppNotificationContent.Create(type, identity, title, body);
            var xml = new XmlDocument();
            xml.LoadXml($"<toast launch=\"{EscapeXml($"type={content.Type}&identity={content.Identity}")}\"><visual><binding template=\"ToastGeneric\"><text>{EscapeXml(content.Title)}</text><text>{EscapeXml(content.Body)}</text></binding></visual></toast>");

            notification = new ToastNotification(xml);
            notification.Activated += (_, _) => OnNotificationActivated(notification, content);
            notification.Dismissed += (_, args) => OnNotificationDismissed(notification, content, args.Reason.ToString());
            notification.Failed += (_, args) => OnNotificationFailed(notification, content, args.ErrorCode.ToString());

            lock (_notificationLock)
                _activeNotifications.Add(notification);

            _notifier.Show(notification);
            _logger.Info($"Windows toast submitted to Action Center: type={content.Type} identity={content.Identity} setting={setting}.");
            return true;
        }
        catch (Exception ex)
        {
            if (notification != null)
                RemoveActive(notification);
            _logger.Error($"Windows toast submission failed: type={type} identity={identity} error={ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private void OnNotificationActivated(ToastNotification notification, AppNotificationContent content)
    {
        RemoveActive(notification);
        _logger.Info($"Windows toast activated by user: type={content.Type} identity={content.Identity}.");
        try
        {
            NotificationActivated?.Invoke($"type={content.Type}&identity={content.Identity}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Windows toast activation handler failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnNotificationDismissed(ToastNotification notification, AppNotificationContent content, string reason)
    {
        RemoveActive(notification);
        _logger.Info($"Windows toast dismissed: type={content.Type} identity={content.Identity} reason={reason}.");
    }

    private void OnNotificationFailed(ToastNotification notification, AppNotificationContent content, string errorCode)
    {
        RemoveActive(notification);
        _logger.Error($"Windows toast failed: type={content.Type} identity={content.Identity} error_code={errorCode}.");
        try
        {
            NotificationFailed?.Invoke(content.Type, content.Identity);
        }
        catch (Exception ex)
        {
            _logger.Error($"Windows toast failure handler failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void RemoveActive(ToastNotification notification)
    {
        lock (_notificationLock)
            _activeNotifications.Remove(notification);
    }

    private static string EscapeXml(string value)
        => SecurityElement.Escape(value) ?? string.Empty;

    private static void EnsureStartMenuShortcut()
    {
        var programsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
        Directory.CreateDirectory(programsDirectory);
        var shortcutPath = Path.Combine(programsDirectory, $"{ApplicationBrand.DisplayName}.lnk");
        var executablePath = Application.ExecutablePath;

        var shellLink = (IShellLinkW)Activator.CreateInstance(
            Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), throwOnError: true)!)!;
        try
        {
            shellLink.SetPath(executablePath);
            shellLink.SetDescription(ApplicationBrand.DisplayName);
            shellLink.SetIconLocation(executablePath, 0);
            shellLink.SetWorkingDirectory(Path.GetDirectoryName(executablePath)!);

            var propertyStore = (IPropertyStore)shellLink;
            var appId = PropVariant.FromString(AppUserModelId);
            var appIdKey = AppUserModelIdProperty;
            try
            {
                propertyStore.SetValue(ref appIdKey, ref appId);
            }
            finally
            {
                appId.Dispose();
            }

            var displayName = PropVariant.FromString(ApplicationBrand.DisplayName);
            var displayNameKey = AppUserModelDisplayNameProperty;
            try
            {
                propertyStore.SetValue(ref displayNameKey, ref displayName);
            }
            finally
            {
                displayName.Dispose();
            }
            propertyStore.Commit();

            ((IPersistFile)shellLink).Save(shortcutPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shellLink);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        lock (_notificationLock)
        {
            _activeNotifications.Clear();
        }

        _notifier = null;
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr itemIdList);
        void SetIDList(IntPtr itemIdList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxPath, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint propertyCount);
        void GetAt(uint propertyIndex, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [ComImport]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;

        public PropertyKey(Guid formatId, uint propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PropVariant : IDisposable
    {
        [FieldOffset(0)] public ushort VariantType;
        [FieldOffset(8)] public IntPtr PointerValue;

        public static PropVariant FromString(string value)
        {
            return new PropVariant
            {
                VariantType = 31, // VT_LPWSTR
                PointerValue = Marshal.StringToCoTaskMemUni(value)
            };
        }

        public void Dispose()
        {
            PropVariantClear(ref this);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant variant);
}

internal sealed record AppNotificationContent(string Type, string Identity, string Title, string Body)
{
    public static AppNotificationContent ForApplicationUpdate(string tag)
    {
        var title = $"Доступне оновлення Хабу: {tag}";
        var body = $"Доступна нова версія «{ApplicationBrand.DisplayName}» {tag}. Відкрийте застосунок, щоб оновитися.";
        return Create("application_update", tag, title, body);
    }

    public static AppNotificationContent ForLocalizationUpdate(
        string gameId,
        string modeKey,
        string modeName,
        string gameName,
        string releaseIdentity,
        string releaseVersion)
    {
        var versionText = string.IsNullOrWhiteSpace(releaseVersion) ? string.Empty : $" до {releaseVersion}";
        var title = $"Доступне оновлення: {modeName}";
        var body = $"Для «{modeName}» у {gameName} доступне оновлення{versionText}. Відкрийте Хаб, щоб оновити.";
        return Create("localization", $"{gameId}/{modeKey}/{releaseIdentity}", title, body);
    }

    public static AppNotificationContent Create(string type, string identity, string title, string body)
    {
        if (string.IsNullOrWhiteSpace(type)) throw new ArgumentException("Notification type is required.", nameof(type));
        if (string.IsNullOrWhiteSpace(identity)) throw new ArgumentException("Notification identity is required.", nameof(identity));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Notification title is required.", nameof(title));
        if (string.IsNullOrWhiteSpace(body)) throw new ArgumentException("Notification body is required.", nameof(body));

        return new AppNotificationContent(type, identity, title, body);
    }
}
