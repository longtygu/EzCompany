using Microsoft.Win32;

namespace EzCompany.App.Services;

/// <summary>
/// Reads and configures HKCU Run auto-start entry for Ez Company without requiring admin privileges.
/// </summary>
public sealed class WindowsStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "EzCompany";

    public bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            object? val = key?.GetValue(ValueName);
            return val is string s && !string.IsNullOrWhiteSpace(s);
        }
        catch
        {
            return false;
        }
    }

    public (bool Ok, string Message) SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return (false, "Không thể mở khóa Registry HKCU Run.");
            }

            if (enabled)
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    return (false, "Không xác định được đường dẫn thực thi của ứng dụng.");
                }

                key.SetValue(ValueName, $"\"{exePath}\"", RegistryValueKind.String);
                return (true, "Đã bật tự động khởi động cùng Windows.");
            }

            if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return (true, "Đã tắt tự động khởi động cùng Windows.");
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi thiết lập khởi động cùng Windows: {ex.Message}");
        }
    }
}
