using Ecommerce_Backend.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ecommerce_Backend.HealthChecks
{
    public class DatabaseHealthCheck : IHealthCheck
    {
        private readonly AppDbContext _context;

        public DatabaseHealthCheck(AppDbContext context)
        {
            _context = context;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var canConnect = await Task.Run(() => _context.Database.CanConnectAsync(cancellationToken), cancellationToken)
                    .WaitAsync(cancellationToken);
                return canConnect
                    ? HealthCheckResult.Healthy("Database is reachable.")
                    : new HealthCheckResult(context.Registration.FailureStatus, "Database is not reachable.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return new HealthCheckResult(context.Registration.FailureStatus, "Database is not reachable.", ex);
            }
        }
    }
}
