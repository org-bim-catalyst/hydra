using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Newtonsoft.Json;

namespace AskLucy.Classes
{
    public class ErrorHandlingMiddleware : IMiddleware
    {
       private readonly ILogger<ErrorHandlingMiddleware> _logger;

        public ErrorHandlingMiddleware(ILogger<ErrorHandlingMiddleware> logger)
        {
            _logger = logger;
        }        
        
        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            try
            {
                await next(context);
            }
            catch (Exception ex)
            {
                var msg = ex.Message.ToString();
                _logger.LogInformation(msg);
                //log error message here
                HandleExceptionAsync(context, ex);
            }
        }

        //https://stackoverflow.com/questions/56127508/how-to-a-i-redirect-to-custom-error-handler-page
        private static void HandleExceptionAsync(HttpContext context, Exception exception)
        {

            context.Response.StatusCode = 500;

            //when request page 
            context.Response.Redirect("/Home/Error");
        }
    }
}