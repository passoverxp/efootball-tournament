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
            while (!stoppingToken.IsCancellationRequested)
            {
                using var connection = new MySqlConnection(
                    _config.GetConnectionString("DefaultConnection"));
                await connection.OpenAsync();

                string sql = "SELECT registration_deadline, status FROM tournament WHERE id = 1";
                using var cmd = new MySqlCommand(sql, connection);
                using var reader = await cmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync()) break;

                DateTime deadline = Convert.ToDateTime(reader["registration_deadline"]);
                string status = reader["status"].ToString()!;
                await reader.CloseAsync();
                await connection.CloseAsync();

                // Already started — stop
                if (status != "registration")
                {
                    _logger.LogInformation("✅ Tournament already started.");
                    break;
                }

                TimeSpan timeUntilDeadline = deadline - DateTime.Now;

                if (timeUntilDeadline <= TimeSpan.Zero)
                {
                    // Deadline passed — trigger immediately
                    _logger.LogInformation("⏰ Deadline reached! Assigning groups & fixtures...");
                    using var conn2 = new MySqlConnection(
                        _config.GetConnectionString("DefaultConnection"));
                    await conn2.OpenAsync();
                    var middleware = new TournamentMiddleware(null!, _config);
                    await middleware.RunAsync(conn2);
                    break;
                }
                else
                {
                    // Sleep exactly until deadline
                    _logger.LogInformation($"⏳ Tournament starts in {timeUntilDeadline.TotalHours:F1} hours");
                    await Task.Delay(timeUntilDeadline, stoppingToken);
                }
            }
        }
    }
}
