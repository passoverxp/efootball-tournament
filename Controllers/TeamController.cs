using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using EFootballWeb.Models;

namespace EFootballWeb.Controllers
{
    public class TeamController : Controller
    {
        private readonly MySqlConnection _connection;
        private readonly IWebHostEnvironment _env;

        public TeamController(MySqlConnection connection, IWebHostEnvironment env)
        {
            _connection = connection;
            _env = env;
        }

        public IActionResult Register()
        {
            if (HttpContext.Session.GetInt32("UserId") == null)
                return RedirectToAction("Login", "Account");

            int userId = HttpContext.Session.GetInt32("UserId")!.Value;
            _connection.Open();

            string checkSql = "SELECT COUNT(*) FROM teams WHERE user_id = @userId";
            using var checkCmd = new MySqlCommand(checkSql, _connection);
            checkCmd.Parameters.AddWithValue("@userId", userId);
            long count = (long)checkCmd.ExecuteScalar()!;

            if (count > 0)
            {
                _connection.Close();
                ViewBag.Error = "You already have a registered team!";
                return View(new TeamRegisterViewModel());
            }

            string tournSql = "SELECT status, registration_deadline FROM tournament WHERE id = 1";
            using var tournCmd = new MySqlCommand(tournSql, _connection);
            using var reader = tournCmd.ExecuteReader();
            reader.Read();
            string status = reader["status"].ToString()!;
            DateTime deadline = Convert.ToDateTime(reader["registration_deadline"]);
            _connection.Close();

            if (status != "registration" || DateTime.Now > deadline)
            {
                ViewBag.Error = "Registration is closed!";
                return View(new TeamRegisterViewModel());
            }

            ViewBag.Deadline = deadline.ToString("MMM dd, yyyy HH:mm");
            return View(new TeamRegisterViewModel());
        }

        [HttpPost]
        public async Task<IActionResult> Register(TeamRegisterViewModel model)
        {
            if (HttpContext.Session.GetInt32("UserId") == null)
                return RedirectToAction("Login", "Account");

            int userId = HttpContext.Session.GetInt32("UserId")!.Value;

            // Handle screenshot upload
            string? screenshotPath = null;
            if (model.SquadScreenshot != null && model.SquadScreenshot.Length > 0)
            {
                string uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "squads");
                Directory.CreateDirectory(uploadsFolder);
                string fileName = $"{userId}_{DateTime.Now.Ticks}{Path.GetExtension(model.SquadScreenshot.FileName)}";
                string filePath = Path.Combine(uploadsFolder, fileName);
                using var stream = new FileStream(filePath, FileMode.Create);
                await model.SquadScreenshot.CopyToAsync(stream);
                screenshotPath = $"/uploads/squads/{fileName}";
            }

            _connection.Open();

            string teamSql = @"INSERT INTO teams (name, user_id, squad_screenshot, league) 
                               VALUES (@name, @userId, @screenshot, 'THE_BEGINNING')";
            using var teamCmd = new MySqlCommand(teamSql, _connection);
            teamCmd.Parameters.AddWithValue("@name", model.TeamName);
            teamCmd.Parameters.AddWithValue("@userId", userId);
            teamCmd.Parameters.AddWithValue("@screenshot", screenshotPath ?? (object)DBNull.Value);
            teamCmd.ExecuteNonQuery();

            long teamId = teamCmd.LastInsertedId;

            for (int i = 0; i < model.PlayerNames.Count; i++)
            {
                string playerName = model.PlayerNames[i];
                if (!string.IsNullOrWhiteSpace(playerName))
                {
                    bool isGK = (i == model.GoalkeeperIndex);
                    string playerSql = @"INSERT INTO players (name, team_id, is_goalkeeper) 
                                        VALUES (@name, @teamId, @isGK)";
                    using var playerCmd = new MySqlCommand(playerSql, _connection);
                    playerCmd.Parameters.AddWithValue("@name", playerName);
                    playerCmd.Parameters.AddWithValue("@teamId", teamId);
                    playerCmd.Parameters.AddWithValue("@isGK", isGK);
                    playerCmd.ExecuteNonQuery();
                }
            }

            _connection.Close();
            ViewBag.Success = $"Team '{model.TeamName}' registered successfully!";
            return View(new TeamRegisterViewModel());
        }
    }
}
