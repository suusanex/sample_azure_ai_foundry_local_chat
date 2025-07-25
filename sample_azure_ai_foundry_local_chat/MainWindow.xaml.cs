﻿using sample_azure_ai_foundry_local_chat.Helpers;

using Windows.UI.ViewManagement;

namespace sample_azure_ai_foundry_local_chat;

public sealed partial class MainWindow : WindowEx
{
    private Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue;

    private UISettings settings;

    public MainWindow()
    {
        InitializeComponent();

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico"));
        Content = null;
        Title = "AppDisplayName".GetLocalized();

        // Theme change code picked from https://github.com/microsoft/WinUI-Gallery/pull/1239
        dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        settings = new UISettings();
        settings.ColorValuesChanged += Settings_ColorValuesChanged; // cannot use FrameworkElement.ActualThemeChanged event

        this.Closed += MainWindow_Closed;
    }

    private async void MainWindow_Closed(object? sender, object e)
    {
        // アプリ終了前にFoundryサービスをリスタート
        // 回答が終わっていてもFoundryLocalのサービスがGPU等を使用し続ける場合があり、サービス再起動で確実にこれを止められるため。
        var chatModel = App.GetRequiredService<Models.ChatModel>();
        await chatModel.RestartFoundryServiceAsync();
    }

    // this handles updating the caption button colors correctly when indows system theme is changed
    // while the app is open
    private void Settings_ColorValuesChanged(UISettings sender, object args)
    {
        // This calls comes off-thread, hence we will need to dispatch it to current app's thread
        dispatcherQueue.TryEnqueue(() =>
        {
            TitleBarHelper.ApplySystemThemeToCaptionButtons();
        });
    }
}
