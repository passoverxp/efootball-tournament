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

        public async Task<IActionResult> Start(string key)
        {
            if (key != "efootball2026secret")
                return Content("❌ Access Denied");

            _connection.Open();

            // Check current status
            string statusSql = "SELECT status FROM tournament WHERE id=1";
            using var statusCmd = new MySqlCommand(statusSql, _connection);
            string currentStatus = (await statusCmd.ExecuteScalarAsync())?.ToString() ?? "";

            // Check match count
            string matchCountSql = "SELECT COUNT(*) FROM matches";
            using var matchCountCmd = new MySqlCommand(matchCountSql, _connection);
            long matchCount = (long)await matchCountCmd.ExecuteScalarAsync()!;

            // If ongoing but no matches — reset and restart
            if (currentStatus == "ongoing" && matchCount == 0)
            {
                string resetSql = @"UPDATE tournament SET 
                    status='registration', stage='group',
                    current_league='THE_BEGINNING',
                    registration_deadline=NULL
                    WHERE id=1";
                using var resetCmd = new MySqlCommand(resetSql, _connection);
                await resetCmd.ExecuteNonQueryAsync();
                currentStatus = "registration";
            }

            if (currentStatus == "ongoing")
            {
                _connection.Close();
                return Content($"⚠️ Tournament already started with {matchCount} matches! Visit /Admin/Reset?key=efootball2026secret to reset.");
            }

            // Check teams
            string countSql = "SELECT COUNT(*) FROM teams";
            using var countCmd = new MySqlCommand(countSql, _connection);
            long teamCount = (long)await countCmd.ExecuteScalarAsync()!;

            if (teamCount < 2)
            {
                _connection.Close();
                return Content($"⚠️ Not enough teams. Only {teamCount} registered.");
            }

            // Reset team stats before assigning groups
            string resetTeamsSql = @"UPDATE teams SET 
                group_name=NULL, played=0, wins=0, draws=0, losses=0,
                goals_for=0, goals_against=0, goal_diff=0, points=0,
                league='THE_BEGINNING' WHERE id > 0";
            using var resetTeamsCmd = new MySqlCommand(resetTeamsSql, _connection);
            await resetTeamsCmd.ExecuteNonQueryAsync();

            // Get teams per group
            string tpgSql = "SELECT teams_per_group FROM tournament WHERE id=1";
            using var tpgCmd = new MySqlCommand(tpgSql, _connection);
            int teamsPerGroup = Convert.ToInt32(await tpgCmd.ExecuteScalarAsync());

            // Assign groups randomly
            string teamsSql = "SELECT id FROM teams ORDER BY RAND()";
            using var teamsCmd = new MySqlCommand(teamsSql, _connection);
            using var teamsReader = await teamsCmd.ExecuteReaderAsync();

            List<int> teamIds = new();
            while (await teamsReader.ReadAsync())
                teamIds.Add(Convert.ToInt32(teamsReader["id"]));
            await teamsReader.CloseAsync();

            string[] labels = { "A","B","C","D","E","F","G","H","I","J","K","L","M","N","O","P" };
            int groupIndex = 0, countInGroup = 0;

            foreach (var teamId in teamIds)
            {
                string group = labels[groupIndex];
                string updateSql = "UPDATE teams SET group_name=@group WHERE id=@id";
                using var updateCmd = new MySqlCommand(updateSql, _connection);
                updateCmd.Parameters.AddWithValue("@group", group);
                updateCmd.Parameters.AddWithValue("@id", teamId);
                await updateCmd.ExecuteNonQueryAsync();

                countInGroup++;
                if (countInGroup >= teamsPerGroup)
                {
                    countInGroup = 0;
                    groupIndex++;
                }
            }

            // Get groups
            string groupsSql = "SELECT DISTINCT group_name FROM teams WHERE group_name IS NOT NULL";
            using var groupsCmd = new MySqlCommand(groupsSql, _connection);
            using var groupsReader = await groupsCmd.ExecuteReaderAsync();

            List<string> groups = new();
            while (await groupsReader.ReadAsync())
                groups.Add(groupsReader["group_name"].ToString()!);
            await groupsReader.CloseAsync();

            // Generate fixtures with scheduling
            var teamSchedule = new Dictionary<int, HashSet<DateTime>>();
            DateTime startDate = DateTime.Today;
            int fixtureCount = 0;

            foreach (var group in groups)
            {
                string gTeamsSql = "SELECT id FROM teams WHERE group_name=@group";
                using var gTeamsCmd = new MySqlCommand(gTeamsSql, _connection);
                gTeamsCmd.Parameters.AddWithValue("@group", group);
                using var gTeamsReader = await gTeamsCmd.ExecuteReaderAsync();

                List<int> gTeamIds = new();
                while (await gTeamsReader.ReadAsync())
                    gTeamIds.Add(Convert.ToInt32(gTeamsReader["id"]));
                await gTeamsReader.CloseAsync();

                for (int i = 0; i < gTeamIds.Count; i++)
                {
                    for (int j = i + 1; j < gTeamIds.Count; j++)
                    {
                        int home = gTeamIds[i];
                        int away = gTeamIds[j];

                        if (!teamSchedule.ContainsKey(home))
                            teamSchedule[home] = new HashSet<DateTime>();
                        if (!teamSchedule.ContainsKey(away))
                            teamSchedule[away] = new HashSet<DateTime>();

                        DateTime day = startDate;
                        while (teamSchedule[home].Contains(day) ||
                               teamSchedule[away].Contains(day))
                            day = day.AddDays(1);

                        teamSchedule[home].Add(day);
                        teamSchedule[away].Add(day);

                        string fixtureSql = @"INSERT INTO matches 
                            (group_name, home_team_id, away_team_id, scheduled_date)
                            VALUES (@group, @home, @away, @date)";
                        using var fixtureCmd = new MySqlCommand(fixtureSql, _connection);
                        fixtureCmd.Parameters.AddWithValue("@group", group);
                        fixtureCmd.Parameters.AddWithValue("@home", home);
                        fixtureCmd.Parameters.AddWithValue("@away", away);
                        fixtureCmd.Parameters.AddWithValue("@date", day);
                        await fixtureCmd.ExecuteNonQueryAsync();
                        fixtureCount++;
                    }
                }
            }

            // Update tournament status
            string startSql = @"UPDATE tournament SET 
                status='ongoing', stage='group',
                current_league='THE_BEGINNING',
                registration_deadline=NOW()
                WHERE id=1";
            using var startCmd = new MySqlCommand(startSql, _connection);
            await startCmd.ExecuteNonQueryAsync();

            _connection.Close();

            return Content($@"✅ Tournament started successfully!
━━━━━━━━━━━━━━━━━━━━━━━
Teams registered  : {teamCount}
Groups created    : {groups.Count}
Fixtures generated: {fixtureCount}
━━━━━━━━━━━━━━━━━━━━━━━
Visit /Home/Fixtures to see the schedule!");
        }

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
                          (SELECT COUNT(*) FROM matches WHERE status='completed') as completed_matches,
                          (SELECT COUNT(*) FROM matches WHERE status='disputed') as disputed_matches
                          FROM tournament t WHERE t.id=1";

            using var cmd = new MySqlCommand(sql, _connection);
            using var reader = cmd.ExecuteReader();
            reader.Read();

            string output = $@"
🏆 TOURNAMENT STATUS
━━━━━━━━━━━━━━━━━━━━━━━━━━━
Tournament #   : {reader["tournament_number"]}
Status         : {reader["status"]}
Stage          : {reader["stage"]}
Current League : {reader["current_league"]}
Deadline       : {(reader["registration_deadline"] == DBNull.Value ? "Not set" : reader["registration_deadline"])}
Next Start     : {(reader["next_start_date"] == DBNull.Value ? "N/A" : reader["next_start_date"])}

TEAMS:
  THE_BEGINNING : {reader["beginning_teams"]}
  FIGHTERS      : {reader["fighters_teams"]}
  LEGENDS       : {reader["legends_teams"]}

MATCHES:
  Pending   : {reader["pending_matches"]}
  Completed : {reader["completed_matches"]}
  Disputed  : {reader["disputed_matches"]}
━━━━━━━━━━━━━━━━━━━━━━━━━━━";

            _connection.Close();
            return Content(output);
        }

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
                "UPDATE tournament SET status='registration', stage='group', current_league='THE_BEGINNING', next_start_date=NULL, registration_deadline=NULL WHERE id=1"
            };

            foreach (var sql in sqls)
            {
                using var cmd = new MySqlCommand(sql, _connection);
                await cmd.ExecuteNonQueryAsync();
            }

            _connection.Close();
            return Content("✅ Tournament reset! Registration is now open.");
        }
    }
}
