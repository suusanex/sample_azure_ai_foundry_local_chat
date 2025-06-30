using Microsoft.Windows.ApplicationModel.Resources;

namespace sample_azure_ai_foundry_local_chat.Helpers;

public static class ResourceExtensions
{
    private static readonly ResourceLoader _resourceLoader = new();

    public static string GetLocalized(this string resourceKey) => _resourceLoader.GetString(resourceKey);
}
