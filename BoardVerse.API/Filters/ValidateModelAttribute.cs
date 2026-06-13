using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using BoardVerse.Core.DTOs.Common;
using System.Text.Json;

namespace BoardVerse.API.Filters
{
    public class ValidateModelAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            if (!context.ModelState.IsValid)
            {
                var errors = context.ModelState
                    .Where(kvp => kvp.Value.Errors.Count > 0)
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()
                    );

                var path = context.HttpContext.Request.Path.Value ?? string.Empty;
                var fieldErrors = string.Join("; ", errors.SelectMany(kvp => kvp.Value));

                var response = new ApiResponse
                {
                    StatusCode = 400,
                    Message = $"Request validation failed for '{path}': {fieldErrors}",
                    Data = errors,
                    Timestamp = DateTime.UtcNow,
                    Path = context.HttpContext.Request.Path,
                };

                context.Result = new BadRequestObjectResult(response);
            }
        }
    }
}