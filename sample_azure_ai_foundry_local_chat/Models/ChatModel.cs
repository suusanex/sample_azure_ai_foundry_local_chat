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
using Microsoft.SemanticKernel.Data;
using Microsoft.SemanticKernel.Plugins.Web.Google;
using Microsoft.SemanticKernel.PromptTemplates.Handlebars;

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
        private readonly HttpClient _httpClient; // Reused HttpClient for the app lifetime
        // システムプロンプトをチャット履歴に一度だけ付加するためのフラグ
        private bool _systemPromptAddedToHistory;

        // Using 0 as a sentinel value meaning "no timeout" for config-driven timeouts.
        private const int INFINITE_TIMEOUT_SECONDS = 0;

        public ChatModel(IConfiguration configuration)
        {
            this.configuration = configuration;

            // Initialize a single HttpClient instance to be reused
            var requestTimeoutSeconds = configuration.GetValue<int>("OpenAI:RequestTimeoutSeconds", INFINITE_TIMEOUT_SECONDS);
            _httpClient = new HttpClient
            {
                Timeout = requestTimeoutSeconds > INFINITE_TIMEOUT_SECONDS ? TimeSpan.FromSeconds(requestTimeoutSeconds) : Timeout.InfiniteTimeSpan
            };
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
            // Extend load timeout (configurable)
            var loadTimeoutSeconds = configuration.GetValue<int>("Foundry:LoadModelTimeoutSeconds", 600);
            var model = await _manager.LoadModelAsync(modelId, TimeSpan.FromSeconds(loadTimeoutSeconds), CancellationToken.None);
            ProgressChanged?.Invoke($"Started model: {modelId}");
            _activeModel = model;
            if (_activeModel != null)
            {
                ProgressChanged?.Invoke($"ActiveModel type: {_activeModel.DisplayName}");
            }

            // 新しくモデルをロードした場合は会話履歴とシステムプロンプト追加フラグをリセット
            _history = new ChatHistory();
            _systemPromptAddedToHistory = false;
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

        public async Task SendAsync(string input, bool useWebSearch = false)
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
                    "unused",
                    httpClient: _httpClient
                );
                var kernel = builder.Build();

                // appsettings.json のシステムプロンプト（自然言語）を取得
                var systemPrompt = configuration["OpenAI:SystemPrompt"];

                if (useWebSearch)
                {
                    // Use Text Search plugin (Google) + Handlebars template with citations
#pragma warning disable SKEXP0050
                    var apiKey = configuration["Search:Google:ApiKey"];
                    var searchEngineId = configuration["Search:Google:SearchEngineId"];
                    if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(searchEngineId))
                    {
                        ResultChanged?.Invoke("[Google検索の設定が見つかりません。User Secrets または appsettings.json を確認してください]\n");
                        return;
                    }

                    var textSearch = new GoogleTextSearch(searchEngineId: searchEngineId, apiKey: apiKey);
                    var searchPlugin = textSearch.CreateWithGetTextSearchResults("SearchPlugin");
                    kernel.Plugins.Add(searchPlugin);

                    string promptTemplate = """
{{#with (SearchPlugin-GetTextSearchResults query)}}  
    {{#each this}}  
    Name: {{Name}}
    Value: {{Value}}
    Link: {{Link}}
    -----------------
    {{/each}}  
{{/with}}  

{{query}}

Include citations to the relevant information where it is referenced in the response.
""";
                    int maxTokens = configuration.GetValue<int>("OpenAI:MaxTokens", 4096);
                    var exec = new OpenAIPromptExecutionSettings { MaxTokens = maxTokens };

                    // システムプロンプトは LLM の system ロールとして渡す
                    if (!string.IsNullOrWhiteSpace(systemPrompt))
                    {
                        exec.ChatSystemPrompt = systemPrompt;
                    }

                    var arguments = new KernelArguments(exec) { { "query", input } };
                    var promptFactory = new HandlebarsPromptTemplateFactory();

                    ResultChanged?.Invoke("[Start]\n");
                    var response = await kernel.InvokePromptAsync(
                        promptTemplate,
                        arguments,
                        templateFormat: HandlebarsPromptTemplateFactory.HandlebarsTemplateFormat,
                        promptTemplateFactory: promptFactory
                    );
                    ResultChanged?.Invoke(response.ToString());
                    ResultChanged?.Invoke("\n[End]\n");
#pragma warning restore SKEXP0050
                }
                else
                {
                    var chat = kernel.GetRequiredService<IChatCompletionService>();

                    // システムプロンプトを一度だけ履歴に追加
                    if (!_systemPromptAddedToHistory && !string.IsNullOrWhiteSpace(systemPrompt))
                    {
                        _history.AddSystemMessage(systemPrompt);
                        _systemPromptAddedToHistory = true;
                    }

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
                _httpClient.Dispose();
                _disposed = true;
            }
        }
    }
}
