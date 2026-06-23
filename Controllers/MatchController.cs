using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using EFootballWeb.Models;

namespace EFootballWeb.Controllers
{
    public class MatchController : Controller
    {
        private readonly MySqlConnection _connection;

        public MatchController(MySqlConnection connection)
        {
            _connection = connection;
        }

        public IActionResult MyMatches()
        {
            if (HttpContext.Session.GetInt32("UserId") == null)
                return RedirectToAction("Login", "Account");

            int userId = HttpContext.Session.GetInt32("UserId")!.Value;
            _connection.Open();

            string teamSql = "SELECT id FROM teams WHERE user_id = @userId";
            using var teamCmd = new MySqlCommand(teamSql, _connection);
            teamCmd.Parameters.AddWithValue("@userId", userId);
            object? teamResult = teamCmd.ExecuteScalar();

            if (teamResult == null)
            {
                _connection.Close();
                ViewBag.Error = "You don't have a registered team yet!";
                return View(new List<Match>());
            }

            int teamId = Convert.ToInt32(teamResult);

            string matchSql = @"
                SELECT m.id, m.group_name, m.status,
                       m.home_score, m.away_score, m.match_date,
                       ht.name as home_team, at.name as away_team,
                       m.home_team_id, m.away_team_id
                FROM matches m
                JOIN teams ht ON m.home_team_id = ht.id
                JOIN teams at ON m.away_team_id = at.id
                WHERE m.home_team_id = @teamId OR m.away_team_id = @teamId
                ORDER BY m.match_date";

            using var matchCmd = new MySqlCommand(matchSql, _connection);
            matchCmd.Parameters.AddWithValue("@teamId", teamId);
            using var reader = matchCmd.ExecuteReader();

            List<Match> matches = new List<Match>();
            while (reader.Read())
            {
                matches.Add(new Match
                {
                    Id = Convert.ToInt32(reader["id"]),
                    GroupName = reader["group_name"].ToString()!,
                    HomeTeamId = Convert.ToInt32(reader["home_team_id"]),
                    AwayTeamId = Convert.ToInt32(reader["away_team_id"]),
                    HomeTeamName = reader["home_team"].ToString()!,
                    AwayTeamName = reader["away_team"].ToString()!,
                    HomeScore = reader["home_score"] == DBNull.Value ? null : Convert.ToInt32(reader["home_score"]),
                    AwayScore = reader["away_score"] == DBNull.Value ? null : Convert.ToInt32(reader["away_score"]),
                    Status = reader["status"].ToString()!,
                    MatchDate = Convert.ToDateTime(reader["match_date"])
                });
            }

            _connection.Close();
            return View(matches);
        }

        public IActionResult SubmitScore(int id)
        {
            if (HttpContext.Session.GetInt32("UserId") == null)
                return RedirectToAction("Login", "Account");

            _connection.Open();

            string matchSql = @"
                SELECT m.*, ht.name as home_team, at.name as away_team,
                       m.home_team_id, m.away_team_id
                FROM matches m
                JOIN teams ht ON m.home_team_id = ht.id
                JOIN teams at ON m.away_team_id = at.id
                WHERE m.id = @id";

            using var matchCmd = new MySqlCommand(matchSql, _connection);
            matchCmd.Parameters.AddWithValue("@id", id);
            using var reader = matchCmd.ExecuteReader();
            reader.Read();

            var model = new ScoreSubmissionViewModel
            {
                MatchId = id,
                HomeTeamName = reader["home_team"].ToString()!,
                AwayTeamName = reader["away_team"].ToString()!,
                HomeTeamId = Convert.ToInt32(reader["home_team_id"]),
                AwayTeamId = Convert.ToInt32(reader["away_team_id"])
            };
            reader.Close();

            model.HomePlayers = GetPlayers(model.HomeTeamId);
            model.AwayPlayers = GetPlayers(model.AwayTeamId);

            _connection.Close();
            return View(model);
        }

        private List<PlayerStatViewModel> GetPlayers(int teamId)
        {
            string sql = "SELECT id, name, is_goalkeeper FROM players WHERE team_id = @teamId";
            using var cmd = new MySqlCommand(sql, _connection);
            cmd.Parameters.AddWithValue("@teamId", teamId);
            using var reader = cmd.ExecuteReader();

            var players = new List<PlayerStatViewModel>();
            while (reader.Read())
            {
                players.Add(new PlayerStatViewModel
                {
                    PlayerId = Convert.ToInt32(reader["id"]),
                    PlayerName = reader["name"].ToString()!,
                    TeamId = teamId,
                    IsGoalkeeper = Convert.ToBoolean(reader["is_goalkeeper"])
                });
            }
            return players;
        }

        [HttpPost]
        public IActionResult SubmitScore(ScoreSubmissionViewModel model)
        {
            if (HttpContext.Session.GetInt32("UserId") == null)
                return RedirectToAction("Login", "Account");

            int userId = HttpContext.Session.GetInt32("UserId")!.Value;
            _connection.Open();

            string checkSql = @"SELECT COUNT(*) FROM score_submissions 
                                WHERE match_id = @matchId AND submitted_by = @userId";
            using var checkCmd = new MySqlCommand(checkSql, _connection);
            checkCmd.Parameters.AddWithValue("@matchId", model.MatchId);
            checkCmd.Parameters.AddWithValue("@userId", userId);
            long already = (long)checkCmd.ExecuteScalar()!;

            if (already > 0)
            {
                _connection.Close();
                ViewBag.Error = "You already submitted a score for this match!";
                return View(model);
            }

            string submitSql = @"INSERT INTO score_submissions 
                                (match_id, submitted_by, home_score, away_score)
                                VALUES (@matchId, @userId, @homeScore, @awayScore)";
            using var submitCmd = new MySqlCommand(submitSql, _connection);
            submitCmd.Parameters.AddWithValue("@matchId", model.MatchId);
            submitCmd.Parameters.AddWithValue("@userId", userId);
            submitCmd.Parameters.AddWithValue("@homeScore", model.HomeScore);
            submitCmd.Parameters.AddWithValue("@awayScore", model.AwayScore);
            submitCmd.ExecuteNonQuery();

            var allPlayers = model.HomePlayers.Concat(model.AwayPlayers).ToList();
            foreach (var player in allPlayers)
            {
                string statSql = @"INSERT INTO match_goals 
                                  (match_id, player_id, team_id, goals, assists)
                                  VALUES (@matchId, @playerId, @teamId, @goals, @assists)";
                using var statCmd = new MySqlCommand(statSql, _connection);
                statCmd.Parameters.AddWithValue("@matchId", model.MatchId);
                statCmd.Parameters.AddWithValue("@playerId", player.PlayerId);
                statCmd.Parameters.AddWithValue("@teamId", player.TeamId);
                statCmd.Parameters.AddWithValue("@goals", player.Goals);
                statCmd.Parameters.AddWithValue("@assists", player.Assists);
                statCmd.ExecuteNonQuery();
            }

            string bothSql = @"SELECT submitted_by, home_score, away_score 
                               FROM score_submissions WHERE match_id = @matchId";
            using var bothCmd = new MySqlCommand(bothSql, _connection);
            bothCmd.Parameters.AddWithValue("@matchId", model.MatchId);
            using var bothReader = bothCmd.ExecuteReader();

            var submissions = new List<(int uid, int home, int away)>();
            while (bothReader.Read())
                submissions.Add((
                    Convert.ToInt32(bothReader["submitted_by"]),
                    Convert.ToInt32(bothReader["home_score"]),
                    Convert.ToInt32(bothReader["away_score"])
                ));
            bothReader.Close();

            if (submissions.Count == 2)
            {
                bool scoresMatch = submissions[0].home == submissions[1].home &&
                                   submissions[0].away == submissions[1].away;

                if (scoresMatch && CheckStatsMatch(model.MatchId))
                {
                    int homeScore = submissions[0].home;
                    int awayScore = submissions[0].away;

                    string confirmSql = @"UPDATE matches SET 
                        home_score=@hs, away_score=@as, status='completed' WHERE id=@id";
                    using var confirmCmd = new MySqlCommand(confirmSql, _connection);
                    confirmCmd.Parameters.AddWithValue("@hs", homeScore);
                    confirmCmd.Parameters.AddWithValue("@as", awayScore);
                    confirmCmd.Parameters.AddWithValue("@id", model.MatchId);
                    confirmCmd.ExecuteNonQuery();

                    UpdateStandings(model.MatchId, homeScore, awayScore);
                    UpdatePlayerStats(model.MatchId, homeScore, awayScore,
                        model.HomeTeamId, model.AwayTeamId);

                    _connection.Close();
                    ViewBag.Success = "✅ Score & stats confirmed! Standings updated.";
                }
                else
                {
                    string disputeSql = "UPDATE matches SET status='disputed' WHERE id=@id";
                    using var disputeCmd = new MySqlCommand(disputeSql, _connection);
                    disputeCmd.Parameters.AddWithValue("@id", model.MatchId);
                    disputeCmd.ExecuteNonQuery();
                    _connection.Close();
                    ViewBag.Error = scoresMatch
                        ? "⚠️ Scores match but player stats don't match! Disputed."
                        : "⚠️ Scores don't match! Match marked as disputed.";
                }
            }
            else
            {
                _connection.Close();
                ViewBag.Success = "✅ Score submitted! Waiting for opponent to confirm.";
            }

            return View(model);
        }

        private bool CheckStatsMatch(int matchId)
        {
            string sql = @"SELECT player_id, goals, assists FROM match_goals 
                          WHERE match_id = @matchId ORDER BY player_id";
            using var cmd = new MySqlCommand(sql, _connection);
            cmd.Parameters.AddWithValue("@matchId", matchId);
            using var reader = cmd.ExecuteReader();

            var stats = new List<(int pid, int goals, int assists)>();
            while (reader.Read())
                stats.Add((
                    Convert.ToInt32(reader["player_id"]),
                    Convert.ToInt32(reader["goals"]),
                    Convert.ToInt32(reader["assists"])
                ));

            var grouped = stats.GroupBy(s => s.pid);
            foreach (var group in grouped)
            {
                var list = group.ToList();
                if (list.Count == 2)
                    if (list[0].goals != list[1].goals || list[0].assists != list[1].assists)
                        return false;
            }
            return true;
        }

        private void UpdatePlayerStats(int matchId, int homeScore, int awayScore,
            int homeTeamId, int awayTeamId)
        {
            string sql = @"
                SELECT mg.player_id, mg.team_id, AVG(mg.goals) as goals, 
                       AVG(mg.assists) as assists, p.is_goalkeeper
                FROM match_goals mg
                JOIN players p ON mg.player_id = p.id
                WHERE mg.match_id = @matchId
                GROUP BY mg.player_id, mg.team_id, p.is_goalkeeper";
            using var cmd = new MySqlCommand(sql, _connection);
            cmd.Parameters.AddWithValue("@matchId", matchId);
            using var reader = cmd.ExecuteReader();

            var stats = new List<(int id, int teamId, int goals, int assists, bool isGK)>();
            while (reader.Read())
                stats.Add((
                    Convert.ToInt32(reader["player_id"]),
                    Convert.ToInt32(reader["team_id"]),
                    Convert.ToInt32(reader["goals"]),
                    Convert.ToInt32(reader["assists"]),
                    Convert.ToBoolean(reader["is_goalkeeper"])
                ));
            reader.Close();

            foreach (var stat in stats)
            {
                string updateSql = @"UPDATE players SET 
                    goals=goals+@goals, assists=assists+@assists WHERE id=@id";
                using var updateCmd = new MySqlCommand(updateSql, _connection);
                updateCmd.Parameters.AddWithValue("@goals", stat.goals);
                updateCmd.Parameters.AddWithValue("@assists", stat.assists);
                updateCmd.Parameters.AddWithValue("@id", stat.id);
                updateCmd.ExecuteNonQuery();

                if (stat.isGK)
                {
                    bool cleanSheet = (stat.teamId == homeTeamId && awayScore == 0) ||
                                      (stat.teamId == awayTeamId && homeScore == 0);
                    if (cleanSheet)
                    {
                        string csSql = "UPDATE players SET clean_sheets=clean_sheets+1 WHERE id=@id";
                        using var csCmd = new MySqlCommand(csSql, _connection);
                        csCmd.Parameters.AddWithValue("@id", stat.id);
                        csCmd.ExecuteNonQuery();
                    }
                }
            }
        }

        private void UpdateStandings(int matchId, int homeScore, int awayScore)
        {
            string matchSql = "SELECT home_team_id, away_team_id FROM matches WHERE id=@id";
            using var matchCmd = new MySqlCommand(matchSql, _connection);
            matchCmd.Parameters.AddWithValue("@id", matchId);
            using var reader = matchCmd.ExecuteReader();
            reader.Read();
            int homeTeamId = Convert.ToInt32(reader["home_team_id"]);
            int awayTeamId = Convert.ToInt32(reader["away_team_id"]);
            reader.Close();

            UpdateTeamStanding(homeTeamId, homeScore, awayScore);
            UpdateTeamStanding(awayTeamId, awayScore, homeScore);
        }

        private void UpdateTeamStanding(int teamId, int goalsFor, int goalsAgainst)
        {
            string result = goalsFor > goalsAgainst ? "win"
                          : goalsFor == goalsAgainst ? "draw" : "loss";

            string sql = result == "win"
                ? "UPDATE teams SET played=played+1,wins=wins+1,points=points+3,goals_for=goals_for+@gf,goals_against=goals_against+@ga,goal_diff=goal_diff+(@gf-@ga) WHERE id=@id"
                : result == "draw"
                ? "UPDATE teams SET played=played+1,draws=draws+1,points=points+1,goals_for=goals_for+@gf,goals_against=goals_against+@ga,goal_diff=goal_diff+(@gf-@ga) WHERE id=@id"
                : "UPDATE teams SET played=played+1,losses=losses+1,goals_for=goals_for+@gf,goals_against=goals_against+@ga,goal_diff=goal_diff+(@gf-@ga) WHERE id=@id";

            using var cmd = new MySqlCommand(sql, _connection);
            cmd.Parameters.AddWithValue("@gf", goalsFor);
            cmd.Parameters.AddWithValue("@ga", goalsAgainst);
            cmd.Parameters.AddWithValue("@id", teamId);
            cmd.ExecuteNonQuery();
        }
    }
}
