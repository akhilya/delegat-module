using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Delegat.Infrastructure;

/// <summary>
/// Drops AI controllers from the app model when <c>AI_ENABLED</c> is false at startup.
/// </summary>
public sealed class AiControllersConvention : IApplicationModelConvention
{
    private static readonly HashSet<string> AiControllerTypeNames = new(StringComparer.Ordinal)
    {
        "AiController",
        "AiConversationController",
        "AiSettingsController",
    };

    private readonly bool _aiEnabled;

    public AiControllersConvention(bool aiEnabled) => _aiEnabled = aiEnabled;

    public void Apply(ApplicationModel application)
    {
        if (_aiEnabled) return;

        for (var i = application.Controllers.Count - 1; i >= 0; i--)
        {
            var name = application.Controllers[i].ControllerType.Name;
            if (AiControllerTypeNames.Contains(name))
                application.Controllers.RemoveAt(i);
        }
    }
}
