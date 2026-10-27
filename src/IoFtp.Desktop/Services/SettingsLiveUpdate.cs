using IoFtp.Desktop.Models;

namespace IoFtp.Desktop.Services;

internal static class SettingsLiveUpdate
{
    public static bool ApiListenerChanged(GlobalSettings running, GlobalSettings saved) =>
        running.EnableHttpsApi != saved.EnableHttpsApi || running.EnableVisionaryBridge != saved.EnableVisionaryBridge ||
        running.HttpsApiPort != saved.HttpsApiPort || running.ApiLocalhostOnly != saved.ApiLocalhostOnly ||
        running.ApiPassword != saved.ApiPassword;
}
