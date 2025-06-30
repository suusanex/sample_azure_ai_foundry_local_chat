namespace sample_azure_ai_foundry_local_chat.Contracts.Services;

public interface IActivationService
{
    Task ActivateAsync(object activationArgs);
}
