namespace Carts.Api.Errors;

/// <summary>
/// Gives the problem documents the framework writes on the API's behalf the same shape as
/// the API's own: every 400 carries a "code", and every status names its current definition.
/// </summary>
public static class ProblemDetailsDefaults
{
    public const string CodeKey = "code";

    /// <summary>A value breaks a rule; the rules broken are listed by field under "errors".</summary>
    public const string ValidationFailed = "validation_failed";

    /// <summary>The body could not be read as the JSON the endpoint expects.</summary>
    public const string InvalidBody = "invalid_body";

    public static void Apply(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;

        // There are two kinds of 400, and a client needs to tell them apart without reading
        // prose. The detail of an unreadable body is fixed text: the reader's own message
        // would name the API's types.
        if (problem.Status == StatusCodes.Status400BadRequest && !problem.Extensions.ContainsKey(CodeKey))
        {
            if (problem is HttpValidationProblemDetails)
            {
                problem.Extensions[CodeKey] = ValidationFailed;
            }
            else
            {
                problem.Extensions[CodeKey] = InvalidBody;
                problem.Detail = "The request body is not the JSON this endpoint expects. Send a JSON object with numbers as numbers.";
            }
        }

        // The framework still points 422 at the WebDAV specification; HTTP itself defines it now.
        if (problem.Status == StatusCodes.Status422UnprocessableEntity)
        {
            problem.Type = "https://tools.ietf.org/html/rfc9110#section-15.5.21";
        }
    }
}
