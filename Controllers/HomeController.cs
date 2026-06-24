using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace EFootballWeb.Controllers
{
    public class HomeController : Controller
    {
        private readonly MySqlConnection _connection;

        public HomeController(MySqlConnection connection)
        {
            _connection = connection;
        }

        public IActionResult Index() => View();

        public IActionResult Standings()
        {
            try
            {
                _connection.Open();
                string sql = @"
                    SELECT name, group_name, played, wins, draws, losses,
                           goals_for, goals_against, goal_diff, points
                    FROM teams
                    WHERE group_name IS NOT NULL
                    ORDER BY group_name, points DESC, goal_diff DESC, goals_for DESC";

                using var cmd = new MySqlCommand(sql, _connection);
                using var reader = cmd.ExecuteReader();

                var groups = new Dictionary<string, List<dynamic>>();
                while (reader.Read())
                {
                    string group = reader["group_name"].ToString()!;
                    if (!groups.ContainsKey(group))
                        groups[group] = new List<dynamic>();

                    groups[group].Add(new
                    {
                        Name = reader["name"].ToString(),
                        Played = Convert.ToInt32(reader["played"]),
                        Wins = Convert.ToInt32(reader["wins"]),
                        Draws = Convert.ToInt32(reader["draws"]),
                        Losses = Convert.ToInt32(reader["losses"]),
                        GoalsFor = Convert.ToInt32(reader["goals_for"]),
                        GoalsAgainst = Convert.ToInt32(reader["goals_against"]),
                        GoalDiff = Convert.ToInt32(reader["goal_diff"]),
                        Points = Convert.ToInt32(reader["points"])
                    });
                }

                _connection.Close();
                ViewBag.Groups = groups;
                return View();
            }
            catch
            {
                ViewBag.Groups = new Dictionary<string, List<dynamic>>();
                return View();
            }
        }

        public IActionResult Fixtures()
        {
            try
            {
                _connection.Open();
                string sql = @"
                    SELECT m.id, m.group_name, m.status,
                           m.home_score, m.away_score, m.scheduled_date,
                           ht.name as home_team, at.name as away_team
                    FROM matches m
                    JOIN teams ht ON m.home_team_id = ht.id
                    JOIN teams at ON m.away_team_id = at.id
                    ORDER BY m.group_name, m.scheduled_date, m.id";

                using var cmd = new MySqlCommand(sql, _connection);
                using var reader = cmd.ExecuteReader();

                var groups = new Dictionary<string, List<dynamic>>();
                while (reader.Read())
                {
                    string group = reader["group_name"].ToString()!;
                    if (!groups.ContainsKey(group))
                        groups[group] = new List<dynamic>();

                    groups[group].Add(new
                    {
                        Id = Convert.ToInt32(reader["id"]),
                        HomeTeam = reader["home_team"].ToString(),
                        AwayTeam = reader["away_team"].ToString(),
                        HomeScore = reader["home_score"] == DBNull.Value ? "-" : reader["home_score"].ToString(),
                        AwayScore = reader["away_score"] == DBNull.Value ? "-" : reader["away_score"].ToString(),
                        Status = reader["status"].ToString(),
                        ScheduledDate = reader["scheduled_date"] == DBNull.Value ? "TBD" : Convert.ToDateTime(reader["scheduled_date"]).ToString("MMM dd, yyyy")
                    });
                }

                _connection.Close();
                ViewBag.Groups = groups;
                return View();
            }
            catch
            {
                ViewBag.Groups = new Dictionary<string, List<dynamic>>();
                return View();
            }
        }

        public IActionResult TopScorers()
        {
            try
            {
                _connection.Open();

                // Top scorers
                string scorersSql = @"
                    SELECT p.name as player, t.name as team, p.goals
                    FROM players p JOIN teams t ON p.team_id = t.id
                    WHERE p.goals > 0 ORDER BY p.goals DESC LIMIT 20";
                using var scorersCmd = new MySqlCommand(scorersSql, _connection);
                using var scorersReader = scorersCmd.ExecuteReader();
                var scorers = new List<dynamic>();
                while (scorersReader.Read())
                    scorers.Add(new {
                        Player = scorersReader["player"].ToString(),
                        Team = scorersReader["team"].ToString(),
                        Goals = Convert.ToInt32(scorersReader["goals"])
                    });
                scorersReader.Close();

                // Top assisters
                string assistsSql = @"
                    SELECT p.name as player, t.name as team, p.assists
                    FROM players p JOIN teams t ON p.team_id = t.id
                    WHERE p.assists > 0 ORDER BY p.assists DESC LIMIT 20";
                using var assistsCmd = new MySqlCommand(assistsSql, _connection);
                using var assistsReader = assistsCmd.ExecuteReader();
                var assisters = new List<dynamic>();
                while (assistsReader.Read())
                    assisters.Add(new {
                        Player = assistsReader["player"].ToString(),
                        Team = assistsReader["team"].ToString(),
                        Assists = Convert.ToInt32(assistsReader["assists"])
                    });
                assistsReader.Close();

                // Clean sheets - use 1 instead of TRUE for compatibility
                string csSql = @"
                    SELECT p.name as player, t.name as team, p.clean_sheets
                    FROM players p JOIN teams t ON p.team_id = t.id
                    WHERE p.is_goalkeeper = 1 AND p.clean_sheets > 0
                    ORDER BY p.clean_sheets DESC LIMIT 20";
                using var csCmd = new MySqlCommand(csSql, _connection);
                using var csReader = csCmd.ExecuteReader();
                var cleanSheets = new List<dynamic>();
                while (csReader.Read())
                    cleanSheets.Add(new {
                        Player = csReader["player"].ToString(),
                        Team = csReader["team"].ToString(),
                        CleanSheets = Convert.ToInt32(csReader["clean_sheets"])
                    });
                csReader.Close();

                _connection.Close();

                ViewBag.Scorers = scorers;
                ViewBag.Assisters = assisters;
                ViewBag.CleanSheets = cleanSheets;
                return View();
            }
            catch (Exception ex)
            {
                ViewBag.Scorers = new List<dynamic>();
                ViewBag.Assisters = new List<dynamic>();
                ViewBag.CleanSheets = new List<dynamic>();
                ViewBag.Error = ex.Message;
                return View();
            }
        }
    }
}
