using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using sample_azure_ai_foundry_local_chat.Models;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace sample_azure_ai_foundry_local_chat.ViewModels;

public partial class MainViewModel : ObservableRecipient
{
    private readonly ChatModel _model;
    private readonly IConfiguration _configuration;
    public ChatModel Model => _model;

    // モデル情報クラス
    public class ModelItem
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsCached { get; set; }
        public override string ToString() => DisplayName;
    }

    [ObservableProperty] private string? _result;
    [ObservableProperty] private string? _input;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SendCommand))] private bool _isEnableSend;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(LoadSelectedModelCommand))] private bool _isEnableLoadModel;
    [ObservableProperty] private string? _progressStr;

    // Web検索のON/OFF
    [ObservableProperty] private bool _useWebSearch;

    // モデル一覧と選択
    [ObservableProperty] private ObservableCollection<ModelItem> _modelList = new();
    [ObservableProperty] private ModelItem? _selectedModel;

    public MainViewModel(IConfiguration configuration, ChatModel model)
    {
        _configuration = configuration;
        _model = model;
        _model.ProgressChanged += msg => ProgressStr = msg;
        _model.ResultChanged += msg => Result += msg;
    }

    [RelayCommand]
    private async Task OnLoadedAsync()
    {
        try
        {
            Result = string.Empty;
            ProgressStr = string.Empty;
            // モデル一覧取得（ロードはしない）
            await LoadModelListAsync();
            IsEnableSend = false;
        }
        catch (Exception e)
        {
            Result += e.ToString() + "\n";
            throw;
        }
    }

    // モデル一覧取得
    private async Task LoadModelListAsync()
    {
        ModelList.Clear();
        var models = await _model.GetAvailableModelsAsync();
        foreach (var m in models)
        {
            var display = m.IsCached ? m.DisplayName : m.DisplayName + "（要DL）";
            ModelList.Add(new ModelItem { Id = m.Id, DisplayName = display, IsCached = m.IsCached });
        }
        // appsettings.jsonのModelIdと一致するモデルを選択
        var configModelId = _configuration["OpenAI:ModelId"];
        var match = ModelList.FirstOrDefault(x => x.Id == configModelId);
        SelectedModel = match ?? ModelList.FirstOrDefault();
        IsEnableLoadModel = true;
    }

    // モデルロードコマンド
    [RelayCommand(CanExecute = nameof(IsEnableLoadModel))]
    private async Task LoadSelectedModelAsync()
    {
        if (SelectedModel == null) return;
        Result = string.Empty;
        ProgressStr = string.Empty;
        await _model.LoadOrDownloadModelAsync(SelectedModel.Id);
        IsEnableSend = _model.IsModelLoaded;
    }

    [RelayCommand(CanExecute = nameof(IsEnableSend))]
    private async Task OnSendAsync()
    {
        IsEnableSend = false;
        try
        {
            Result += $"\n{Input}\n";
            var input = Input;
            var useWeb = UseWebSearch; // 現在のトグル状態を取得
            Input = string.Empty; // 入力欄をクリア
            await _model.SendAsync(input ?? string.Empty, useWeb);
        }
        finally
        {
            IsEnableSend = true;
        }
    }
}
