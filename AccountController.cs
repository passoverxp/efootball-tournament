using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using EFootballWeb.Models;
using System.Security.Cryptography;
using System.Text;

namespace EFootballWeb.Controllers
{
    public class AccountController : Controller
    {
        private readonly MySqlConnection _connection;

        public AccountController(MySqlConnection connection)
        {
            _connection = connection;
        }

        // ── REGISTER ──────────────────────────────────────────
        public IActionResult Register() => View();

        [HttpPost]
        public IActionResult Register(RegisterViewModel model)
        {
            if (model.Password != model.ConfirmPassword)
            {
                ViewBag.Error = "Passwords do not match!";
                return View(model);
            }

            try
            {
                _connection.Open();

                string hash = HashPassword(model.Password);

                string sql = @"INSERT INTO users (username, email, password_hash, role)
                               VALUES (@username, @email, @hash, 'player')";

                using var cmd = new MySqlCommand(sql, _connection);
                cmd.Parameters.AddWithValue("@username", model.Username);
                cmd.Parameters.AddWithValue("@email", model.Email);
                cmd.Parameters.AddWithValue("@hash", hash);
                cmd.ExecuteNonQuery();

                ViewBag.Success = "Account created! Please login.";
                return View();
            }
            catch (MySqlException ex) when (ex.Message.Contains("Duplicate"))
            {
                ViewBag.Error = "Email or username already exists!";
                return View(model);
            }
            finally
            {
                _connection.Close();
            }
        }

        // ── LOGIN ─────────────────────────────────────────────
        public IActionResult Login() => View();

        [HttpPost]
        public IActionResult Login(LoginViewModel model)
        {
            _connection.Open();

            string sql = "SELECT * FROM users WHERE email = @email";
            using var cmd = new MySqlCommand(sql, _connection);
            cmd.Parameters.AddWithValue("@email", model.Email);

            using var reader = cmd.ExecuteReader();

            if (reader.Read())
            {
                string storedHash = reader["password_hash"].ToString()!;
                if (storedHash == HashPassword(model.Password))
                {
                    // Save session
                    HttpContext.Session.SetInt32("UserId", Convert.ToInt32(reader["id"]));
                    HttpContext.Session.SetString("Username", reader["username"].ToString()!);
                    HttpContext.Session.SetString("Role", reader["role"].ToString()!);

                    _connection.Close();
                    return RedirectToAction("Index", "Home");
                }
            }

            _connection.Close();
            ViewBag.Error = "Invalid email or password!";
            return View(model);
        }

        // ── LOGOUT ────────────────────────────────────────────
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        // ── HELPER ────────────────────────────────────────────
        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToHexString(bytes);
        }
    }
}