namespace CopilotLlm.Infrastructure;

public sealed record SecretServiceItem(string ItemPath, string? Label, string? Account, bool IsLocked);
