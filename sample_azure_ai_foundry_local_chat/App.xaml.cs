using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using sample_azure_ai_foundry_local_chat.Activation;
using sample_azure_ai_foundry_local_chat.Contracts.Services;
using sample_azure_ai_foundry_local_chat.Core.Contracts.Services;
using sample_azure_ai_foundry_local_chat.Core.Services;
using sample_azure_ai_foundry_local_chat.Helpers;
using sample_azure_ai_foundry_local_chat.Services;
using sample_azure_ai_foundry_local_chat.ViewModels;
using sample_azure_ai_foundry_local_chat.Views;

namespace sample_azure_ai_foundry_local_chat;

// To learn more about WinUI 3, see https://docs.microsoft.com/windows/apps/winui/winui3/.
public partial class App : Application
{
    // The .NET Generic Host provides dependency injection, configuration, logging, and other services.
    // https://docs.microsoft.com/dotnet/core/extensions/generic-host
    // https://docs.microsoft.com/dotnet/core/extensions/dependency-injection
    // https://docs.microsoft.com/dotnet/core/extensions/configuration
    // https://docs.microsoft.com/dotnet/core/extensions/logging
    public IHost Host
    {
        get;
    }

    public static T GetRequiredService<T>()
        where T : class
    {
        if ((App.Current as App)!.Host.Services.GetRequiredService(typeof(T)) is not T service)
        {
            throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices within App.xaml.cs.");
        }

        return service;
    }

    public static WindowEx MainWindow { get; } = new MainWindow();

    public static UIElement? AppTitlebar { get; set; }

    public App()
    {
        InitializeComponent();

        Host = Microsoft.Extensions.Hosting.Host.
        CreateDefaultBuilder().
        UseContentRoot(AppContext.BaseDirectory).
        ConfigureAppConfiguration((context, config) =>
        {
            config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
        }).
        ConfigureServices((context, services) =>
        {
            // Default Activation Handler
            services.AddTransient<ActivationHandler<LaunchActivatedEventArgs>, DefaultActivationHandler>();

            // Other Activation Handlers

            // Services
            services.AddSingleton<IActivationService, ActivationService>();
            services.AddSingleton<IPageService, PageService>();
            services.AddSingleton<INavigationService, NavigationService>();

            // Core Services
            services.AddSingleton<IFileService, FileService>();

            // Views and ViewModels
            services.AddTransient<MainViewModel>();
            services.AddTransient<MainPage>();
            // ChatModel DI登録
            services.AddSingleton<sample_azure_ai_foundry_local_chat.Models.ChatModel>();

        }).
        Build();

        UnhandledException += App_UnhandledException;
    }

    private async void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        var dlg = new ContentDialog
        {
            Title = "Unhandled Exception",
            Content = e.Exception.ToString(),
            PrimaryButtonText = "Close"
        };
        await dlg.ShowAsync();

    }

    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);

        await App.GetRequiredService<IActivationService>().ActivateAsync(args);
    }
}
