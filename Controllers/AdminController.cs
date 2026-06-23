using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace EFootballWeb.Controllers
{
    public class AdminController : Controller
    {
        private readonly MySqlConnection _connection;
        private readonly IConfiguration _config;

        public AdminController(MySqlConnection connection, IConfiguration config)
        {
            _connection = connection;
            _config = config;
        }

        // SECRET URL: /Admin/Start?key=yourpassword
        public async Task<IActionResult> Start(string key)
        {
            if (key != "efootball2026secret")
                return Content("❌ Access Denied");

            _connection.Open();

            // Check how many teams registered
            string countSql = "SELECT COUNT(*) FROM teams";
            using var countCmd = new MySqlCommand(countSql, _connection);
            long teamCount = (long)await countCmd.ExecuteScalarAsync()!;

            if (teamCount < 2)
            {
                _connection.Close();
                return Content($"⚠️ Not enough teams to start. Only {teamCount} team(s) registered.");
            }

            // Force start — update deadline to now
            string updateSql = @"UPDATE tournament SET 
                registration_deadline = NOW() WHERE id = 1";
            using var updateCmd = new MySqlCommand(updateSql, _connection);
            await updateCmd.ExecuteNonQueryAsync();
            _connection.Close();

            // Trigger middleware
            var middleware = new EFootballWeb.Services.TournamentMiddleware(null!, _config);
            await middleware.RunAsync(null);

            return Content($"✅ Tournament started with {teamCount} teams! Groups assigned and fixtures generated. Visit /Home/Fixtures to see the schedule.");
        }

        // View tournament status: /Admin/Status?key=yourpassword
        public IActionResult Status(string key)
        {
            if (key != "efootball2026secret")
                return Content("❌ Access Denied");

            _connection.Open();

            string sql = @"SELECT t.status, t.stage, t.current_league, 
                          t.tournament_number, t.registration_deadline,
                          t.next_start_date,
                          (SELECT COUNT(*) FROM teams WHERE league='THE_BEGINNING') as beginning_teams,
                          (SELECT COUNT(*) FROM teams WHERE league='FIGHTERS') as fighters_teams,
                          (SELECT COUNT(*) FROM teams WHERE league='LEGENDS') as legends_teams,
                          (SELECT COUNT(*) FROM matches WHERE status='pending') as pending_matches,
                          (SELECT COUNT(*) FROM matches WHERE status='completed') as completed_matches
                          FROM tournament t WHERE t.id=1";

            using var cmd = new MySqlCommand(sql, _connection);
            using var reader = cmd.ExecuteReader();
            reader.Read();

            string output = $@"
🏆 TOURNAMENT STATUS
━━━━━━━━━━━━━━━━━━━━━━━━━━━
Tournament #  : {reader["tournament_number"]}
Status        : {reader["status"]}
Stage         : {reader["stage"]}
Current League: {reader["current_league"]}
Deadline      : {reader["registration_deadline"]}
Next Start    : {(reader["next_start_date"] == DBNull.Value ? "N/A" : reader["next_start_date"])}

TEAMS:
  THE_BEGINNING : {reader["beginning_teams"]}
  FIGHTERS      : {reader["fighters_teams"]}
  LEGENDS       : {reader["legends_teams"]}

MATCHES:
  Pending   : {reader["pending_matches"]}
  Completed : {reader["completed_matches"]}
━━━━━━━━━━━━━━━━━━━━━━━━━━━";

            _connection.Close();
            return Content(output);
        }

        // Reset tournament: /Admin/Reset?key=yourpassword
        public async Task<IActionResult> Reset(string key)
        {
            if (key != "efootball2026secret")
                return Content("❌ Access Denied");

            _connection.Open();

            string[] sqls = {
                "DELETE FROM match_goals",
                "DELETE FROM match_assists",
                "DELETE FROM score_submissions",
                "DELETE FROM knockout_matches",
                "DELETE FROM matches",
                "DELETE FROM promotions",
                "DELETE FROM tournament_history",
                "UPDATE teams SET group_name=NULL, played=0, wins=0, draws=0, losses=0, goals_for=0, goals_against=0, goal_diff=0, points=0, league='THE_BEGINNING'",
                "UPDATE players SET goals=0, assists=0, clean_sheets=0",
                "UPDATE tournament SET status='registration', stage='group', current_league='THE_BEGINNING', next_start_date=NULL, registration_deadline=DATE_ADD(NOW(), INTERVAL 7 DAY) WHERE id=1"
            };

            foreach (var sql in sqls)
            {
                using var cmd = new MySqlCommand(sql, _connection);
                await cmd.ExecuteNonQueryAsync();
            }

            _connection.Close();
            return Content("✅ Tournament reset! Registration is open for 7 days.");
        }
    }
}
