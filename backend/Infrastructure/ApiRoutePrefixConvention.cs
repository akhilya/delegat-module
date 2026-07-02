using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Delegat.Infrastructure;

/// <summary>
/// Prefixes all controller routes with /api so they don't conflict with SPA (Vue Router) routes.
/// Handles both controller-level and action-level absolute routes.
/// </summary>
public class ApiRoutePrefixConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            var hasControllerRoute = controller.Selectors
                .Any(s => s.AttributeRouteModel?.Template != null);

            // Prefix controller-level routes
            foreach (var selector in controller.Selectors)
            {
                if (selector.AttributeRouteModel?.Template != null)
                {
                    var template = selector.AttributeRouteModel.Template;
                    selector.AttributeRouteModel.Template = template.StartsWith("/")
                        ? "/api" + template
                        : "api/" + template;
                }
            }

            // Prefix action-level routes
            foreach (var action in controller.Actions)
            {
                foreach (var selector in action.Selectors)
                {
                    if (selector.AttributeRouteModel?.Template != null)
                    {
                        var template = selector.AttributeRouteModel.Template;
                        if (template.StartsWith("/"))
                            selector.AttributeRouteModel.Template = "/api" + template;
                        else if (!hasControllerRoute)
                            selector.AttributeRouteModel.Template = "api/" + template;
                    }
                }
            }
        }
    }
}
