using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SharpPress.Services.Database
{
    public class DatabaseHealthCheck : IHealthCheck
    {
        private class PingRow
        {
            public int Value { get; set; }
        }

        private readonly FeatherDatabase _database;

        public DatabaseHealthCheck(FeatherDatabase database)
        {
            _database = database;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var rows = await _database.ExecuteQuery<PingRow>("SELECT 1 AS Value", cancellationToken);

                if (rows.Count == 1 && rows[0].Value == 1)
                    return HealthCheckResult.Healthy("MySQL is responding");

                return HealthCheckResult.Degraded("MySQL returned an unexpected response");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("MySQL is unreachable", ex);
            }
        }
    }
}
