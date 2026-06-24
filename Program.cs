using MySqlConnector;

var builder = WebApplication.CreateBuilder(args);
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddControllersWithViews();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = "EFootball.Session";
});

// Disable antiforgery token validation issues
builder.Services.AddDataProtection()
    .SetApplicationName("EFootballTournament");

builder.Services.AddTransient<MySqlConnection>(_ =>
    new MySqlConnection(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddHostedService<EFootballWeb.Services.TournamentScheduler>();

var app = builder.Build();

using (var connection = new MySqlConnection(
    builder.Configuration.GetConnectionString("DefaultConnection")))
{
    connection.Open();

    string[] tables = {
        @"CREATE TABLE IF NOT EXISTS users (
            id INT AUTO_INCREMENT PRIMARY KEY,
            username VARCHAR(100) NOT NULL UNIQUE,
            email VARCHAR(100) NOT NULL UNIQUE,
            password_hash VARCHAR(255) NOT NULL,
            role ENUM('player','viewer') DEFAULT 'player',
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
        );",

        @"CREATE TABLE IF NOT EXISTS tournament (
            id INT AUTO_INCREMENT PRIMARY KEY,
            name VARCHAR(100) NOT NULL,
            registration_deadline DATETIME NULL,
            teams_per_group INT DEFAULT 4,
            status ENUM('registration','ongoing','finished') DEFAULT 'registration',
            tournament_number INT DEFAULT 1,
            next_start_date DATETIME DEFAULT NULL,
            current_league ENUM('THE_BEGINNING','FIGHTERS','LEGENDS') DEFAULT 'THE_BEGINNING',
            stage ENUM('group','knockout','finished') DEFAULT 'group'
        );",

        @"CREATE TABLE IF NOT EXISTS teams (
            id INT AUTO_INCREMENT PRIMARY KEY,
            name VARCHAR(100) NOT NULL,
            user_id INT NOT NULL UNIQUE,
            group_name VARCHAR(10) DEFAULT NULL,
            league ENUM('THE_BEGINNING','FIGHTERS','LEGENDS') DEFAULT 'THE_BEGINNING',
            tournament_number INT DEFAULT 1,
            squad_screenshot VARCHAR(255) DEFAULT NULL,
            played INT DEFAULT 0,
            wins INT DEFAULT 0,
            draws INT DEFAULT 0,
            losses INT DEFAULT 0,
            goals_for INT DEFAULT 0,
            goals_against INT DEFAULT 0,
            goal_diff INT DEFAULT 0,
            points INT DEFAULT 0,
            eliminated BOOLEAN DEFAULT FALSE,
            FOREIGN KEY (user_id) REFERENCES users(id)
        );",

        @"CREATE TABLE IF NOT EXISTS players (
            id INT AUTO_INCREMENT PRIMARY KEY,
            name VARCHAR(100) NOT NULL,
            team_id INT NOT NULL,
            goals INT DEFAULT 0,
            assists INT DEFAULT 0,
            clean_sheets INT DEFAULT 0,
            is_goalkeeper BOOLEAN DEFAULT FALSE,
            FOREIGN KEY (team_id) REFERENCES teams(id)
        );",

        @"CREATE TABLE IF NOT EXISTS matches (
            id INT AUTO_INCREMENT PRIMARY KEY,
            group_name VARCHAR(10) NOT NULL,
            home_team_id INT NOT NULL,
            away_team_id INT NOT NULL,
            home_score INT DEFAULT NULL,
            away_score INT DEFAULT NULL,
            status ENUM('pending','disputed','completed') DEFAULT 'pending',
            scheduled_date DATE DEFAULT NULL,
            match_date DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (home_team_id) REFERENCES teams(id),
            FOREIGN KEY (away_team_id) REFERENCES teams(id)
        );",

        @"CREATE TABLE IF NOT EXISTS score_submissions (
            id INT AUTO_INCREMENT PRIMARY KEY,
            match_id INT NOT NULL,
            submitted_by INT NOT NULL,
            home_score INT NOT NULL,
            away_score INT NOT NULL,
            submitted_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (match_id) REFERENCES matches(id),
            FOREIGN KEY (submitted_by) REFERENCES users(id)
        );",

        @"CREATE TABLE IF NOT EXISTS match_goals (
            id INT AUTO_INCREMENT PRIMARY KEY,
            match_id INT NOT NULL,
            player_id INT NOT NULL,
            team_id INT NOT NULL,
            goals INT DEFAULT 1,
            assists INT DEFAULT 0,
            FOREIGN KEY (match_id) REFERENCES matches(id),
            FOREIGN KEY (player_id) REFERENCES players(id),
            FOREIGN KEY (team_id) REFERENCES teams(id)
        );",

        @"CREATE TABLE IF NOT EXISTS match_assists (
            id INT AUTO_INCREMENT PRIMARY KEY,
            match_id INT NOT NULL,
            player_id INT NOT NULL,
            team_id INT NOT NULL,
            assists INT DEFAULT 1,
            FOREIGN KEY (match_id) REFERENCES matches(id),
            FOREIGN KEY (player_id) REFERENCES players(id),
            FOREIGN KEY (team_id) REFERENCES teams(id)
        );",

        @"CREATE TABLE IF NOT EXISTS knockout_matches (
            id INT AUTO_INCREMENT PRIMARY KEY,
            tournament_number INT NOT NULL,
            league ENUM('THE_BEGINNING','FIGHTERS','LEGENDS'),
            round ENUM('quarterfinal','semifinal','final'),
            home_team_id INT NOT NULL,
            away_team_id INT NOT NULL,
            home_score INT DEFAULT NULL,
            away_score INT DEFAULT NULL,
            winner_team_id INT DEFAULT NULL,
            status ENUM('pending','disputed','completed') DEFAULT 'pending',
            scheduled_date DATE DEFAULT NULL,
            FOREIGN KEY (home_team_id) REFERENCES teams(id),
            FOREIGN KEY (away_team_id) REFERENCES teams(id)
        );",

        @"CREATE TABLE IF NOT EXISTS tournament_history (
            id INT AUTO_INCREMENT PRIMARY KEY,
            tournament_number INT NOT NULL,
            league ENUM('THE_BEGINNING','FIGHTERS','LEGENDS'),
            winner_team_id INT,
            runner_up_team_id INT,
            started_at DATETIME,
            ended_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (winner_team_id) REFERENCES teams(id),
            FOREIGN KEY (runner_up_team_id) REFERENCES teams(id)
        );",

        @"CREATE TABLE IF NOT EXISTS promotions (
            id INT AUTO_INCREMENT PRIMARY KEY,
            team_id INT NOT NULL,
            from_league ENUM('THE_BEGINNING','FIGHTERS'),
            to_league ENUM('FIGHTERS','LEGENDS'),
            tournament_number INT NOT NULL,
            promoted_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (team_id) REFERENCES teams(id)
        );"
    };

    foreach (var sql in tables)
    {
        using var cmd = new MySqlCommand(sql, connection);
        cmd.ExecuteNonQuery();
    }

    string insertTournament = @"
        INSERT IGNORE INTO tournament (id, name, registration_deadline, teams_per_group, status)
        VALUES (1, 'eFootball Tournament 2026', NULL, 4, 'registration');";
    using var tCmd = new MySqlCommand(insertTournament, connection);
    tCmd.ExecuteNonQuery();

    Console.WriteLine("✅ Database tables ready!");
}

app.UseExceptionHandler("/Home/Error");
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
