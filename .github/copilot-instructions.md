# Azure AI Foundry Local Chat Sample

Azure AI Foundry Local chat sample is a WinUI 3 desktop application that demonstrates how to use the Microsoft.AI.Foundry.Local SDK with Microsoft Semantic Kernel to create a local AI chat interface. The application allows users to download, load, and chat with AI models locally on Windows.

Always reference these instructions first and fallback to search or bash commands only when you encounter unexpected information that does not match the info here.

## Working Effectively

### Prerequisites and Environment Setup
**CRITICAL:** This application can ONLY be built and run on Windows. Do not attempt to build on Linux or macOS.

- Install .NET 9.0 SDK: Download from https://dotnet.microsoft.com/download/dotnet/9.0
- Install Visual Studio 2022 with the following workloads:
  - .NET Desktop Development
  - Windows application development (Windows App SDK)
- Install Windows 10 SDK (19041 or later): Required components are listed in `.vsconfig`
- Verify installation: `dotnet --version` should show 9.0.x

### Build and Restore
- Navigate to repository root containing `sample_azure_ai_foundry_local_chat.sln`
- Restore NuGet packages: `dotnet restore` -- takes 2-5 minutes. NEVER CANCEL. Set timeout to 10+ minutes.
- Build the solution: `dotnet build` -- takes 3-8 minutes. NEVER CANCEL. Set timeout to 15+ minutes.
- Build for specific platform: `dotnet build -p:Platform=x64` (options: x86, x64, arm64)

### Running the Application
- Run in development: `dotnet run --project sample_azure_ai_foundry_local_chat/sample_azure_ai_foundry_local_chat.csproj`
- Or build and run executable from output directory: `bin/Debug/net9.0-windows10.0.19041.0/win-x64/`
- Application requires Azure AI Foundry Local service to be available on the system

### MSIX Packaging
- Create MSIX package: Right-click project in Visual Studio → "Package and Publish" → "Create App Packages..."
- For unpackaged deployment: Follow Microsoft's deployment guide for unpackaged Windows App SDK apps

## Validation

### Manual Testing Workflow
**CRITICAL:** Always manually validate any changes by running through this complete end-to-end scenario:

1. Launch the application
2. Wait for model list to populate (5-15 seconds)
3. Select a model from the dropdown (note: models marked "（要DL）" require download)
4. Click "モデルロード" (Load Model) button
5. If model needs download: Wait for download progress (5-60 minutes depending on model size). NEVER CANCEL.
6. Wait for model loading to complete (30-120 seconds). NEVER CANCEL.
7. Verify "Send" button becomes enabled
8. Enter a test prompt in the text box (supports multi-line input)
9. Click "Send" button
10. Verify streaming response appears in result area (responses typically start within 5-30 seconds)
11. Test multiple conversations to ensure chat history is maintained
12. Close application and verify graceful shutdown (service restart occurs automatically)

### Timing Expectations
- **Model Download:** 5-60 minutes depending on model size and internet speed. NEVER CANCEL. Set timeout to 90+ minutes.
- **Model Loading:** 30-120 seconds depending on system specifications. NEVER CANCEL. Set timeout to 5+ minutes.
- **Chat Response:** First token typically appears within 5-30 seconds, full response streams over 10-180 seconds.
- **Application Startup:** 3-10 seconds for UI to appear and model list to populate.

### Code Quality Validation
- Format code: Use Visual Studio built-in formatter or install EditorConfig extensions
- No automated linting tools are configured in this project
- No unit tests exist - rely entirely on manual testing
- Always verify UI responsiveness during long-running operations

## Configuration

### Application Settings
- **appsettings.json:** Contains OpenAI configuration
  - `OpenAI:MaxTokens`: Default 4096, controls response length limit
  - `OpenAI:ModelId`: Default model to select on startup ("Phi-4-cuda-gpu")
- Configuration is loaded via Microsoft.Extensions.Configuration
- Settings can be modified without rebuilding the application

### Model Management
- Models are managed through Azure AI Foundry Local service
- Downloaded models are cached locally by the service
- Model catalog is retrieved dynamically at application startup
- Default model from configuration is auto-selected if available

## Project Structure

### Key Projects
- **sample_azure_ai_foundry_local_chat:** Main WinUI 3 application (.NET 9, Windows-specific)
- **sample_azure_ai_foundry_local_chat.Core:** Shared services (.NET 9, cross-platform)

### Important Files and Locations
- **MainPage.xaml/.cs:** Primary UI page with chat interface
- **MainViewModel.cs:** MVVM view model handling UI logic and model operations
- **Models/ChatModel.cs:** Core chat functionality using AI Foundry Local SDK and Semantic Kernel
- **appsettings.json:** Application configuration
- **Package.appxmanifest:** Windows app manifest for MSIX packaging
- **App.xaml.cs:** Application initialization and dependency injection setup

### Dependency Injection Architecture
- Services registered in `App.xaml.cs`
- Uses Microsoft.Extensions.Hosting and Microsoft.Extensions.DependencyInjection
- Key services: ChatModel (singleton), NavigationService, FileService
- ViewModels and Pages registered as transient

## Common Tasks

The following are validated commands and their expected behavior:

### Repository Root Structure
```
.
├── .editorconfig
├── .gitignore
├── .vsconfig                              # Visual Studio components
├── LICENSE
├── README.md                              # Japanese description
├── sample_azure_ai_foundry_local_chat/   # Main WinUI 3 project
├── sample_azure_ai_foundry_local_chat.Core/  # Shared services
└── sample_azure_ai_foundry_local_chat.sln    # Solution file
```

### Key Dependencies
From sample_azure_ai_foundry_local_chat.csproj:
- **Microsoft.AI.Foundry.Local** (0.1.0): Core AI Foundry SDK
- **Microsoft.SemanticKernel** (1.59.0): AI orchestration framework
- **Microsoft.WindowsAppSDK** (1.7.x): Windows App SDK for WinUI 3
- **CommunityToolkit.Mvvm** (8.4.0): MVVM framework
- **Microsoft.Extensions.Hosting** (9.0.6): Dependency injection and hosting

### Troubleshooting Common Issues
- **Build Error NETSDK1045:** Install .NET 9.0 SDK
- **Build Error NETSDK1100:** Ensure building on Windows with Windows targeting enabled
- **Model Loading Fails:** Verify Azure AI Foundry Local service is running and accessible
- **Download Timeouts:** Increase timeout values, model downloads can take 60+ minutes
- **UI Freezing:** Check for proper async/await usage in ViewModels, UI updates must be on main thread

### Development Guidelines
- Always use async/await for AI operations to maintain UI responsiveness
- Update progress indicators during long-running operations
- Handle exceptions gracefully with user-friendly messages
- Follow MVVM pattern: UI logic in ViewModels, business logic in Models/Services
- Use dependency injection for service access
- Test with different model sizes to validate timeout handling

### Limitations
- **Windows Only:** Cannot build or run on Linux/macOS due to WinUI 3 dependency
- **No Unit Tests:** Project has no testing infrastructure
- **No CI/CD:** No automated build or deployment pipelines configured
- **Manual Validation Required:** All changes must be validated through complete user scenarios
- **GPU Dependency:** Some models may require CUDA-compatible GPU for optimal performance