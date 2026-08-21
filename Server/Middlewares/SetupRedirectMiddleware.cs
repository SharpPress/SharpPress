using SharpPress.Models;
using SharpPress.Services.Database;

namespace SharpPress.Middlewares
{
    public class SetupGate
    {
        private volatile bool _completed;

        public bool Completed => _completed;

        public void MarkCompleted()
        {
            _completed = true;
        }
    }

    public class SetupRedirectMiddleware
    {
        private static readonly string[] AllowedPaths = { "/setup", "/health" };

        private readonly RequestDelegate _next;

        public SetupRedirectMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, FeatherDatabase database, SetupGate gate)
        {
            var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

            if (!gate.Completed && !AllowedPaths.Contains(path))
            {
                var admin = await database.Query<User>().Where(u => (int)u.Roles >= (int)UserRole.Admin).FirstOrDefaultAsync();

                if (admin == null)
                {
                    context.Response.Redirect("/setup");
                    return;
                }

                gate.MarkCompleted();
            }

            await _next(context);
        }
    }
}
