using System.Net;

namespace SharpPress.Helpers
{
    public static class StatusPages
    {
        public static string Banned(string supportEmail)
            => Render("Account suspended", "Your account has been suspended. Contact support if you think this is a mistake.", Contact(supportEmail));

        public static string Maintenance(string supportEmail)
            => Render("Down for maintenance", "The site is being updated. Please try again shortly.", Contact(supportEmail));

        public static string NotFound()
            => Render("Page not found", "The page you are looking for does not exist or has moved.", "<a href=\"/\">Go to the home page</a>");

        private static string Contact(string supportEmail)
            => $"<a href=\"mailto:{WebUtility.HtmlEncode(supportEmail)}\">Contact support</a>";

        private static string Render(string title, string message, string link)
            => $"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                    <meta charset="utf-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1.0">
                    <title>{title}</title>
                    <link rel="stylesheet" href="/assets/app.css">
                </head>
                <body>
                    <main class="status-page">
                        <h1>{title}</h1>
                        <p>{message}</p>
                        <p>{link}</p>
                    </main>
                </body>
                </html>
                """;
    }
}
