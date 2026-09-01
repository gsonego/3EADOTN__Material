namespace catalog_api.Models;

// One previous turn of the conversation. The model has no memory between HTTP
// calls -- if we want it to remember, WE resend. Role is "user" or "assistant".
public record ChatTurn(string Role, string Content);

// What the browser sends. Note how much of this is plain English: the whole
// point of Module 6 is that SystemPrompt and Grounded change the app's
// behaviour without a single line of code changing.
public record AssistantRequest(string Question, string? SystemPrompt, bool Grounded, List<ChatTurn>? History);

// Returned so the UI can show what the request actually cost. PromptTokens is
// the number that jumps when Grounded is switched on -- that visible jump is
// the "context is not free" lesson.
public record AssistantUsage(int PromptTokens, int CompletionTokens);

public record AssistantResponse(string Answer, string? Model, bool Grounded, int ContextTitles, AssistantUsage Usage);
