using MySqlConnector;

namespace EFootballWeb.Services
{
    public class TournamentScheduler : BackgroundService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<TournamentScheduler> _logger;

        public TournamentScheduler(IConfiguration config, ILogger<TournamentScheduler> logger)
        {
            _config = config;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Wait 10 seconds for app to fully start and tables to be created
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var connection = new MySqlConnection(
                        _config.GetConnectionString("DefaultConnection"));
                    await connection.OpenAsync();

                    string sql = "SELECT registration_deadline, status FROM tournament WHERE id = 1";
                    using var cmd = new MySqlCommand(sql, connection);
                    using var reader = await cmd.ExecuteReaderAsync();

                    if (!await reader.ReadAsync())
                    {
                        await reader.CloseAsync();
                        await connection.CloseAsync();
                        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                        continue;
                    }

                    string status = reader["status"].ToString()!;
                    bool hasDeadline = reader["registration_deadline"] != DBNull.Value;
                    DateTime? deadline = hasDeadline
                        ? Convert.ToDateTime(reader["registration_deadline"])
                        : null;
                    await reader.CloseAsync();
                    await connection.CloseAsync();

                    if (status != "registration")
                    {
                        _logger.LogInformation("✅ Tournament already started.");
                        break;
                    }

                    if (deadline.HasValue && DateTime.Now >= deadline.Value)
                    {
                        _logger.LogInformation("⏰ Deadline reached! Starting tournament...");
                        var middleware = new TournamentMiddleware(null!, _config);
                        await middleware.RunAsync(null);
                        break;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Scheduler error: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
        }
    }
}
