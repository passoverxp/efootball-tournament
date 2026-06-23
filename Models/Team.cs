namespace EFootballWeb.Models
{
    public class Team
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int UserId { get; set; }
        public string? GroupName { get; set; }
        public string League { get; set; } = "THE_BEGINNING";
        public int TournamentNumber { get; set; } = 1;
        public int Played { get; set; }
        public int Wins { get; set; }
        public int Draws { get; set; }
        public int Losses { get; set; }
        public int GoalsFor { get; set; }
        public int GoalsAgainst { get; set; }
        public int GoalDiff { get; set; }
        public int Points { get; set; }
        public bool Eliminated { get; set; }
        public string? SquadScreenshot { get; set; }
    }

    public class TeamRegisterViewModel
    {
        public string TeamName { get; set; } = "";
        public List<string> PlayerNames { get; set; } = new List<string>();
        public int GoalkeeperIndex { get; set; } = 0;
        public IFormFile? SquadScreenshot { get; set; }
    }
}
