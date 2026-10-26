using Delegat.Services.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests.Ai;

public class AnthropicMessageParamsTests
{
    [Fact]
    public void BuildAnthropicParams_PreservesUserAndAssistantRoles()
    {
        var client = new AiApiClient(new HttpClient(), NullLogger<AiApiClient>.Instance);
        var request = new AiRequest
        {
            model = "test-model",
            messages =
            [
                new AiMessage { role = "user", content = "Hello" },
                new AiMessage { role = "assistant", content = "Hi" },
            ],
        };

        var parameters = client.BuildAnthropicParams(request);

        Assert.True(parameters.Messages[0].TryPickUser(out var user));
        Assert.True(user.Content.TryPickString(out var userText));
        Assert.Equal("Hello", userText);
        Assert.True(parameters.Messages[1].TryPickAssistant(out var assistant));
        Assert.True(assistant.Content.TryPickString(out var assistantText));
        Assert.Equal("Hi", assistantText);
    }
}
