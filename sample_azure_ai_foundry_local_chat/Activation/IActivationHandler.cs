namespace sample_azure_ai_foundry_local_chat.Activation;

public interface IActivationHandler
{
    bool CanHandle(object args);

    Task HandleAsync(object args);
}
