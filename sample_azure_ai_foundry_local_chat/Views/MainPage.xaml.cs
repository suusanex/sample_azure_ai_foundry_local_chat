using Microsoft.UI.Xaml.Controls;

using sample_azure_ai_foundry_local_chat.ViewModels;

namespace sample_azure_ai_foundry_local_chat.Views;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel
    {
        get;
    }

    public MainPage()
    {
        ViewModel = App.GetRequiredService<MainViewModel>();
        InitializeComponent();
    }
}
