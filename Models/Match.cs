namespace EFootballWeb.Models
{
    public class Match
    {
        public int Id { get; set; }
        public string GroupName { get; set; } = "";
        public int HomeTeamId { get; set; }
        public int AwayTeamId { get; set; }
        public string HomeTeamName { get; set; } = "";
        public string AwayTeamName { get; set; } = "";
        public int? HomeScore { get; set; }
        public int? AwayScore { get; set; }
        public string Status { get; set; } = "pending";
        public DateTime MatchDate { get; set; }
    }

    public class PlayerStatViewModel
    {
        public int PlayerId { get; set; }
        public string PlayerName { get; set; } = "";
        public int TeamId { get; set; }
        public bool IsGoalkeeper { get; set; }
        public int Goals { get; set; }
        public int Assists { get; set; }
    }

    public class ScoreSubmissionViewModel
    {
        public int MatchId { get; set; }
        public string HomeTeamName { get; set; } = "";
        public string AwayTeamName { get; set; } = "";
        public int HomeTeamId { get; set; }
        public int AwayTeamId { get; set; }
        public int HomeScore { get; set; }
        public int AwayScore { get; set; }
        public List<PlayerStatViewModel> HomePlayers { get; set; } = new();
        public List<PlayerStatViewModel> AwayPlayers { get; set; } = new();
    }
}
