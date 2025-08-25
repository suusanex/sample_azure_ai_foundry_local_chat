using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AI.Foundry.Local;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Text.Json;

namespace sample_azure_ai_foundry_local_chat.Models
{
    public class ChatModel : IAsyncDisposable
    {
        private FoundryLocalManager? _manager;
        private ModelInfo? _activeModel;
        private bool _disposed;

        public event Action<string>? ProgressChanged;
        public event Action<string>? ResultChanged;

        public string? ActiveModelDisplayName => _activeModel?.DisplayName;
        public bool IsModelLoaded => _activeModel != null && _manager != null;

        public class ModelInfoItem
        {
            public string Id { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public bool IsCached { get; set; }
        }

        private readonly IConfiguration configuration;
        private ChatHistory _history = new ChatHistory();
        private static readonly HttpClient _httpClient = new HttpClient();

        public ChatModel(IConfiguration configuration)
        {
            this.configuration = configuration;
        }

        private async Task EnsureManagerInitializedAsync()
        {
            if (_manager == null)
            {
                _manager = new FoundryLocalManager();
                await _manager.StartServiceAsync(CancellationToken.None);
            }
        }

        public async Task<List<ModelInfoItem>> GetAvailableModelsAsync()
        {
            await EnsureManagerInitializedAsync();
            var cachedModels = await _manager.ListCachedModelsAsync(CancellationToken.None);
            var cachedIds = cachedModels
                .Select(m => m.ModelId)
                .Where(id => !string.IsNullOrEmpty(id))
                .ToHashSet();
            var catalogModels = await _manager.ListCatalogModelsAsync(CancellationToken.None);
            var result = new List<ModelInfoItem>();
            foreach (var m in catalogModels)
            {
                var id = m.ModelId ?? "";
                var display = m.DisplayName ?? id;
                var isCached = cachedIds.Contains(id);
                result.Add(new ModelInfoItem { Id = id, DisplayName = display, IsCached = isCached });
            }
            return result;
        }

        public async Task LoadOrDownloadModelAsync(string modelId)
        {
            await EnsureManagerInitializedAsync();
            var cachedModels = await _manager.ListCachedModelsAsync(CancellationToken.None);
            var isCached = cachedModels.Any(m => m.ModelId == modelId);
            if (!isCached)
            {
                ProgressChanged?.Invoke("Downloading model...");
                await foreach (var progress in _manager.DownloadModelWithProgressAsync(modelId, ct:CancellationToken.None))
                {
                    ProgressChanged?.Invoke($"{(progress.IsCompleted ? "Download complete," : "Downloading...")} ({progress.Percentage}%)");
                }
                ProgressChanged?.Invoke("Download complete. Loading model...");
            }
            var model = await _manager.LoadModelAsync(modelId, TimeSpan.FromSeconds(60), CancellationToken.None);
            ProgressChanged?.Invoke($"Started model: {modelId}");
            _activeModel = model;
            if (_activeModel != null)
            {
                ProgressChanged?.Invoke($"ActiveModel type: {_activeModel.DisplayName}");
            }
        }

        public async Task RestartFoundryServiceAsync()
        {
            if (_manager != null)
            {
                ProgressChanged?.Invoke("Finalize. Stop Service...");
                await _manager.StopServiceAsync();
                ProgressChanged?.Invoke("Finalize. Restart Service...");
                await _manager.StartServiceAsync();
            }
        }

        private async Task<string> PerformWebSearchAsync(string query)
        {
            try
            {
                // DuckDuckGoのInstant Answer APIを使用（APIキー不要）
                var encodedQuery = Uri.EscapeDataString(query);
                var url = $"https://api.duckduckgo.com/?q={encodedQuery}&format=json&no_html=1&skip_disambig=1";
                
                var response = await _httpClient.GetStringAsync(url);
                var searchResult = JsonSerializer.Deserialize<JsonElement>(response);
                
                var results = new List<string>();
                
                // Abstract（要約）を取得
                if (searchResult.TryGetProperty("Abstract", out var abstractElement) && 
                    !string.IsNullOrEmpty(abstractElement.GetString()))
                {
                    results.Add($"概要: {abstractElement.GetString()}");
                }
                
                // Definition（定義）を取得
                if (searchResult.TryGetProperty("Definition", out var definitionElement) && 
                    !string.IsNullOrEmpty(definitionElement.GetString()))
                {
                    results.Add($"定義: {definitionElement.GetString()}");
                }
                
                // Answer（回答）を取得
                if (searchResult.TryGetProperty("Answer", out var answerElement) && 
                    !string.IsNullOrEmpty(answerElement.GetString()))
                {
                    results.Add($"回答: {answerElement.GetString()}");
                }
                
                // RelatedTopics（関連トピック）から上位数件を取得
                if (searchResult.TryGetProperty("RelatedTopics", out var relatedTopicsElement) && 
                    relatedTopicsElement.ValueKind == JsonValueKind.Array)
                {
                    var topics = relatedTopicsElement.EnumerateArray().Take(3);
                    foreach (var topic in topics)
                    {
                        if (topic.TryGetProperty("Text", out var textElement) && 
                            !string.IsNullOrEmpty(textElement.GetString()))
                        {
                            results.Add($"関連情報: {textElement.GetString()}");
                        }
                    }
                }
                
                return results.Count > 0 ? string.Join("\n", results) : "関連する情報が見つかりませんでした。";
            }
            catch (Exception ex)
            {
                return $"検索エラー: {ex.Message}";
            }
        }

        public async Task SendAsync(string input, bool enableWebSearch = false)
        {
            await EnsureManagerInitializedAsync();
            if (string.IsNullOrEmpty(input))
            {
                ResultChanged?.Invoke("Input is empty.\n");
                return;
            }
            if (_activeModel == null)
            {
                ResultChanged?.Invoke("No active model loaded.\n");
                return;
            }
            if (_manager == null)
            {
                ResultChanged?.Invoke("No manager available.\n");
                return;
            }
            try
            {
                var builder = Kernel.CreateBuilder();
                builder.AddOpenAIChatCompletion(
                    _activeModel.ModelId,
                    _manager.Endpoint,
                    "unused"
                );

                var kernel = builder.Build();
                var chat = kernel.GetRequiredService<IChatCompletionService>();

                // Web検索が有効な場合、検索結果を含めてプロンプトを構築
                string finalInput = input;
                if (enableWebSearch)
                {
                    try
                    {
                        ResultChanged?.Invoke("[Web検索を実行中...]\n");
                        var searchResults = await PerformWebSearchAsync(input);
                        
                        if (!string.IsNullOrEmpty(searchResults))
                        {
                            finalInput = $"ユーザーの質問: {input}\n\n関連するWeb検索結果:\n{searchResults}\n\n上記の検索結果を参考にして、ユーザーの質問に答えてください。";
                            ResultChanged?.Invoke("[Web検索完了]\n");
                        }
                    }
                    catch (Exception ex)
                    {
                        ResultChanged?.Invoke($"[Web検索エラー: {ex.Message}]\n");
                        // 検索に失敗した場合は元の入力を使用
                        finalInput = input;
                    }
                }

                _history.AddUserMessage(finalInput);
                int maxTokens = configuration.GetValue<int>("OpenAI:MaxTokens", 4096);
                var settings = new OpenAIPromptExecutionSettings
                {
                    MaxTokens = maxTokens,
                };
                ResultChanged?.Invoke("[Start]\n");
                await foreach (var message in chat.GetStreamingChatMessageContentsAsync(_history, kernel: kernel, executionSettings: settings))
                {
                    if (string.IsNullOrEmpty(message.Content))
                    {
                        continue;
                    }
                    ResultChanged?.Invoke(message.Content);
                }
                ResultChanged?.Invoke("\n[End]\n");
            }
            catch (Exception ex)
            {
                ResultChanged?.Invoke($"Error generating response: {ex.Message}\n");
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                if (_manager != null)
                {
                    await _manager.DisposeAsync();
                    _manager = null;
                }
                _disposed = true;
            }
        }
    }
}
