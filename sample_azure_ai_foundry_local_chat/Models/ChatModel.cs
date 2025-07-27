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

        public async Task SendAsync(string input)
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
                _history.AddUserMessage(input);
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
