using System.Reflection;
using System.Runtime.InteropServices;
using BdoClient.Services;

namespace BdoClient.Tests.Services;

public sealed class WindowsAppNotificationServiceTests
{
    [Fact]
    public void PropVariant_HasNativeSizeForCurrentProcessArchitecture()
    {
        var propVariant = typeof(WindowsAppNotificationService)
            .GetNestedType("PropVariant", BindingFlags.NonPublic);

        Assert.NotNull(propVariant);

        var actualSize = Marshal.SizeOf(propVariant!);
        var expectedSize = IntPtr.Size == 8 ? 24 : 16;

        Assert.Equal(expectedSize, actualSize);
    }

    [Fact]
    public void PropertyStore_AcceptsBothAppUserModelProperties()
    {
        // The AUMID shortcut registration writes two properties through
        // IPropertyStore.SetValue. A truncated PROPVARIANT layout made the
        // second call throw, which aborted the whole toast registration and
        // silently disabled every Windows notification.
        var propertyStore = typeof(WindowsAppNotificationService)
            .GetNestedType("IPropertyStore", BindingFlags.NonPublic);
        var propVariant = typeof(WindowsAppNotificationService)
            .GetNestedType("PropVariant", BindingFlags.NonPublic);

        Assert.NotNull(propertyStore);
        Assert.NotNull(propVariant);

        var setValue = propertyStore!.GetMethod("SetValue");
        Assert.NotNull(setValue);

        var parameters = setValue!.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(propVariant, parameters[1].ParameterType.GetElementType());
    }

    [Fact]
    public void NotificationSetting_ReadFailure_IsTolerated()
    {
        var service = new WindowsAppNotificationService(new NullLogger());

        var tryGet = typeof(WindowsAppNotificationService)
            .GetMethod("TryGetNotificationSetting", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(tryGet);

        // Without a registered notifier the helper returns null instead of
        // throwing, so a missing setting can never break the update path.
        var result = tryGet!.Invoke(service, Array.Empty<object>());

        Assert.Null(result);
    }

    [Fact]
    public void TryShow_WithoutRegisteredNotifier_FailsClosedWithoutThrowing()
    {
        var service = new WindowsAppNotificationService(new NullLogger());

        Assert.False(service.IsRegistered);
        Assert.False(service.TryShow("application_update", "v1.2.27", "title", "body"));
    }

    private sealed class NullLogger : BdoClient.Logging.ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}
