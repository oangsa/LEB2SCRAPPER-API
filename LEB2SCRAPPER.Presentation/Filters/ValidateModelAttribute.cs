using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using LEB2SCRAPPER.Entity.Models.Response;

namespace LEB2SCRAPPER.Presentation.Filters;

/// <summary>
/// Custom validation filter that automatically validates model state and returns error responses
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ValidateModelAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.ModelState.IsValid)
        {
            var validationErrorResponse = new ValidationErrorResponse
            {
                StatusCode = 400,
                Message = "Validation failed",
                ResponseCode = ApiErrorCodes.InvalidRequest,
                TraceId = context.HttpContext.TraceIdentifier,
                ValidationErrors = context.ModelState
                    .Where(x => x.Value?.Errors.Count > 0)
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value?.Errors.Select(e => e.ErrorMessage).ToArray() ?? Array.Empty<string>()
                    )
            };

            context.Result = new BadRequestObjectResult(validationErrorResponse);
        }
    }
}
