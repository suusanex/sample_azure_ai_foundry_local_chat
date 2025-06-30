using Microsoft.UI.Xaml.Controls;

namespace sample_azure_ai_foundry_local_chat.Helpers;

public static class FrameExtensions
{
    public static object? GetPageViewModel(this Frame frame) => frame?.Content?.GetType().GetProperty("ViewModel")?.GetValue(frame.Content, null);
}
