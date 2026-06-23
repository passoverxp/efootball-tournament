using MySqlConnector;

namespace EFootballWeb.Services
{
    public class TournamentMiddleware
    {
        private readonly RequestDelegate? _next;
        private readonly IConfiguration _config;

        public TournamentMiddleware(RequestDelegate? next, IConfiguration config)
        {
            _next = next;
            _config = config;
        }

        public async Task InvokeAsync(HttpContext? context)
        {
            await RunAsync(null);
            if (_next != null && context != null)
                await _next(context);
        }

        public async Task RunAsync(MySqlConnection? existingConnection)
        {
            var connection = existingConnection ?? new MySqlConnection(
                _config.GetConnectionString("DefaultConnection"));

            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            // Check tournament status
            string tournSql = @"SELECT status, stage, current_league, 
                                teams_per_group, tournament_number, next_start_date
                                FROM tournament WHERE id = 1";
            using var tournCmd = new MySqlCommand(tournSql, connection);
            using var tournReader = await tournCmd.ExecuteReaderAsync();
            if (!await tournReader.ReadAsync()) return;

            string status = tournReader["status"].ToString()!;
            string stage = tournReader["stage"].ToString()!;
            string currentLeague = tournReader["current_league"].ToString()!;
            int teamsPerGroup = Convert.ToInt32(tournReader["teams_per_group"]);
            int tournNumber = Convert.ToInt32(tournReader["tournament_number"]);
            DateTime? nextStart = tournReader["next_start_date"] == DBNull.Value
                ? null : Convert.ToDateTime(tournReader["next_start_date"]);
            await tournReader.CloseAsync();

            // Check if next tournament should start
            if (status == "finished" && nextStart.HasValue && DateTime.Now >= nextStart.Value)
            {
                await StartNextTournament(connection, tournNumber);
                return;
            }

            // Check if deadline passed — assign groups
            string checkSql = @"SELECT teams_per_group FROM tournament 
                                WHERE id=1 AND status='registration' 
                                AND registration_deadline < NOW()";
            using var checkCmd = new MySqlCommand(checkSql, connection);
            object? result = await checkCmd.ExecuteScalarAsync();

            if (result != null)
            {
                teamsPerGroup = Convert.ToInt32(result);
                await AssignGroups(connection, teamsPerGroup, "THE_BEGINNING", tournNumber);
                await GenerateGroupFixtures(connection, "THE_BEGINNING", tournNumber);
                await ScheduleMatches(connection, tournNumber);

                string updateSql = @"UPDATE tournament SET status='ongoing', 
                                    stage='group', current_league='THE_BEGINNING' WHERE id=1";
                using var updateCmd = new MySqlCommand(updateSql, connection);
                await updateCmd.ExecuteNonQueryAsync();
                return;
            }

            // Check if group stage is complete
            if (status == "ongoing" && stage == "group")
            {
                bool groupDone = await IsGroupStageDone(connection, currentLeague, tournNumber);
                if (groupDone)
                {
                    if (currentLeague == "THE_BEGINNING")
                        await PromoteTeams(connection, tournNumber, teamsPerGroup);
                    await GenerateKnockout(connection, currentLeague, tournNumber);
                }
            }

            // Check if knockout is complete
            if (status == "ongoing" && stage == "knockout")
            {
                await CheckKnockoutProgress(connection, currentLeague, tournNumber);
            }

            if (existingConnection == null)
                await connection.CloseAsync();
        }

        // ── ASSIGN GROUPS ─────────────────────────────────────
        private async Task AssignGroups(MySqlConnection conn, int teamsPerGroup,
            string league, int tournNumber)
        {
            string teamsSql = @"SELECT id FROM teams 
                                WHERE league=@league AND group_name IS NULL 
                                ORDER BY RAND()";
            using var cmd = new MySqlCommand(teamsSql, conn);
            cmd.Parameters.AddWithValue("@league", league);
            using var reader = await cmd.ExecuteReaderAsync();

            List<int> teamIds = new();
            while (await reader.ReadAsync())
                teamIds.Add(Convert.ToInt32(reader["id"]));
            await reader.CloseAsync();

            if (teamIds.Count == 0) return;

            string[] labels = { "A","B","C","D","E","F","G","H",
                                 "I","J","K","L","M","N","O","P" };
            int groupIndex = 0, countInGroup = 0;

            foreach (var teamId in teamIds)
            {
                string group = labels[groupIndex];
                string updateSql = "UPDATE teams SET group_name=@group WHERE id=@id";
                using var updateCmd = new MySqlCommand(updateSql, conn);
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
        }

        // ── GENERATE GROUP FIXTURES ───────────────────────────
        private async Task GenerateGroupFixtures(MySqlConnection conn,
            string league, int tournNumber)
        {
            string countSql = "SELECT COUNT(*) FROM matches WHERE group_name IS NOT NULL";
            using var countCmd = new MySqlCommand(countSql, conn);
            long existing = (long)await countCmd.ExecuteScalarAsync()!;
            if (existing > 0) return;

            string groupsSql = @"SELECT DISTINCT group_name FROM teams 
                                WHERE league=@league AND group_name IS NOT NULL";
            using var groupsCmd = new MySqlCommand(groupsSql, conn);
            groupsCmd.Parameters.AddWithValue("@league", league);
            using var groupsReader = await groupsCmd.ExecuteReaderAsync();

            List<string> groups = new();
            while (await groupsReader.ReadAsync())
                groups.Add(groupsReader["group_name"].ToString()!);
            await groupsReader.CloseAsync();

            foreach (var group in groups)
            {
                string teamsSql = @"SELECT id FROM teams 
                                   WHERE group_name=@group AND league=@league";
                using var teamsCmd = new MySqlCommand(teamsSql, conn);
                teamsCmd.Parameters.AddWithValue("@group", group);
                teamsCmd.Parameters.AddWithValue("@league", league);
                using var teamsReader = await teamsCmd.ExecuteReaderAsync();

                List<int> teamIds = new();
                while (await teamsReader.ReadAsync())
                    teamIds.Add(Convert.ToInt32(teamsReader["id"]));
                await teamsReader.CloseAsync();

                for (int i = 0; i < teamIds.Count; i++)
                    for (int j = i + 1; j < teamIds.Count; j++)
                    {
                        string fixtureSql = @"INSERT INTO matches 
                            (group_name, home_team_id, away_team_id) 
                            VALUES (@group, @home, @away)";
                        using var fixtureCmd = new MySqlCommand(fixtureSql, conn);
                        fixtureCmd.Parameters.AddWithValue("@group", group);
                        fixtureCmd.Parameters.AddWithValue("@home", teamIds[i]);
                        fixtureCmd.Parameters.AddWithValue("@away", teamIds[j]);
                        await fixtureCmd.ExecuteNonQueryAsync();
                    }
            }
        }

        // ── SCHEDULE MATCHES (1 per team per day) ─────────────
        private async Task ScheduleMatches(MySqlConnection conn, int tournNumber)
        {
            string matchesSql = @"SELECT id, home_team_id, away_team_id 
                                 FROM matches WHERE scheduled_date IS NULL";
            using var matchesCmd = new MySqlCommand(matchesSql, conn);
            using var matchesReader = await matchesCmd.ExecuteReaderAsync();

            var matches = new List<(int id, int home, int away)>();
            while (await matchesReader.ReadAsync())
                matches.Add((
                    Convert.ToInt32(matchesReader["id"]),
                    Convert.ToInt32(matchesReader["home_team_id"]),
                    Convert.ToInt32(matchesReader["away_team_id"])
                ));
            await matchesReader.CloseAsync();

            // Schedule: track which days each team is busy
            var teamSchedule = new Dictionary<int, HashSet<DateTime>>();
            DateTime startDate = DateTime.Today;

            foreach (var match in matches)
            {
                if (!teamSchedule.ContainsKey(match.home))
                    teamSchedule[match.home] = new HashSet<DateTime>();
                if (!teamSchedule.ContainsKey(match.away))
                    teamSchedule[match.away] = new HashSet<DateTime>();

                // Find earliest day both teams are free
                DateTime day = startDate;
                while (teamSchedule[match.home].Contains(day) ||
                       teamSchedule[match.away].Contains(day))
                    day = day.AddDays(1);

                teamSchedule[match.home].Add(day);
                teamSchedule[match.away].Add(day);

                string updateSql = "UPDATE matches SET scheduled_date=@date WHERE id=@id";
                using var updateCmd = new MySqlCommand(updateSql, conn);
                updateCmd.Parameters.AddWithValue("@date", day);
                updateCmd.Parameters.AddWithValue("@id", match.id);
                await updateCmd.ExecuteNonQueryAsync();
            }
        }

        // ── CHECK IF GROUP STAGE DONE ─────────────────────────
        private async Task<bool> IsGroupStageDone(MySqlConnection conn,
            string league, int tournNumber)
        {
            string sql = @"SELECT COUNT(*) FROM matches 
                          WHERE status != 'completed'";
            using var cmd = new MySqlCommand(sql, conn);
            long pending = (long)await cmd.ExecuteScalarAsync()!;
            return pending == 0;
        }

        // ── PROMOTE TEAMS FROM THE_BEGINNING TO FIGHTERS ──────
        private async Task PromoteTeams(MySqlConnection conn,
            int tournNumber, int teamsPerGroup)
        {
            // Get all groups in THE_BEGINNING
            string groupsSql = @"SELECT DISTINCT group_name FROM teams 
                                WHERE league='THE_BEGINNING' AND group_name IS NOT NULL";
            using var groupsCmd = new MySqlCommand(groupsSql, conn);
            using var groupsReader = await groupsCmd.ExecuteReaderAsync();

            List<string> groups = new();
            while (await groupsReader.ReadAsync())
                groups.Add(groupsReader["group_name"].ToString()!);
            await groupsReader.CloseAsync();

            foreach (var group in groups)
            {
                // Get teams sorted by points — promote top 50%
                string teamsSql = @"SELECT id FROM teams 
                                   WHERE group_name=@group AND league='THE_BEGINNING'
                                   ORDER BY points DESC, goal_diff DESC, goals_for DESC";
                using var teamsCmd = new MySqlCommand(teamsSql, conn);
                teamsCmd.Parameters.AddWithValue("@group", group);
                using var teamsReader = await teamsCmd.ExecuteReaderAsync();

                List<int> teamIds = new();
                while (await teamsReader.ReadAsync())
                    teamIds.Add(Convert.ToInt32(teamsReader["id"]));
                await teamsReader.CloseAsync();

                int promoteCount = teamIds.Count / 2;

                for (int i = 0; i < promoteCount; i++)
                {
                    // Promote to FIGHTERS
                    string promoteSql = @"UPDATE teams SET 
                        league='FIGHTERS', group_name=NULL,
                        played=0, wins=0, draws=0, losses=0,
                        goals_for=0, goals_against=0, goal_diff=0, points=0
                        WHERE id=@id";
                    using var promoteCmd = new MySqlCommand(promoteSql, conn);
                    promoteCmd.Parameters.AddWithValue("@id", teamIds[i]);
                    await promoteCmd.ExecuteNonQueryAsync();

                    // Record promotion
                    string recordSql = @"INSERT INTO promotions 
                        (team_id, from_league, to_league, tournament_number)
                        VALUES (@teamId, 'THE_BEGINNING', 'FIGHTERS', @tournNumber)";
                    using var recordCmd = new MySqlCommand(recordSql, conn);
                    recordCmd.Parameters.AddWithValue("@teamId", teamIds[i]);
                    recordCmd.Parameters.AddWithValue("@tournNumber", tournNumber);
                    await recordCmd.ExecuteNonQueryAsync();
                }
            }

            // Now start FIGHTERS group stage
            string updateSql = @"UPDATE tournament SET 
                                current_league='FIGHTERS', stage='group' WHERE id=1";
            using var updateCmd = new MySqlCommand(updateSql, conn);
            await updateCmd.ExecuteNonQueryAsync();

            await AssignGroups(conn, teamsPerGroup, "FIGHTERS", tournNumber);
            await GenerateGroupFixtures(conn, "FIGHTERS", tournNumber);
            await ScheduleMatches(conn, tournNumber);
        }

        // ── GENERATE KNOCKOUT ─────────────────────────────────
        private async Task GenerateKnockout(MySqlConnection conn,
            string league, int tournNumber)
        {
            // Get top teams per group for knockout
            string groupsSql = @"SELECT DISTINCT group_name FROM teams 
                                WHERE league=@league AND group_name IS NOT NULL";
            using var groupsCmd = new MySqlCommand(groupsSql, conn);
            groupsCmd.Parameters.AddWithValue("@league", league);
            using var groupsReader = await groupsCmd.ExecuteReaderAsync();

            List<string> groups = new();
            while (await groupsReader.ReadAsync())
                groups.Add(groupsReader["group_name"].ToString()!);
            await groupsReader.CloseAsync();

            List<int> knockoutTeams = new();
            foreach (var group in groups)
            {
                string topSql = @"SELECT id FROM teams 
                                 WHERE group_name=@group AND league=@league
                                 ORDER BY points DESC, goal_diff DESC LIMIT 2";
                using var topCmd = new MySqlCommand(topSql, conn);
                topCmd.Parameters.AddWithValue("@group", group);
                topCmd.Parameters.AddWithValue("@league", league);
                using var topReader = await topCmd.ExecuteReaderAsync();
                while (await topReader.ReadAsync())
                    knockoutTeams.Add(Convert.ToInt32(topReader["id"]));
                await topReader.CloseAsync();
            }

            // Generate knockout matches
            string round = knockoutTeams.Count > 4 ? "quarterfinal"
                         : knockoutTeams.Count > 2 ? "semifinal" : "final";

            for (int i = 0; i < knockoutTeams.Count - 1; i += 2)
            {
                string kSql = @"INSERT INTO knockout_matches 
                    (tournament_number, league, round, home_team_id, away_team_id)
                    VALUES (@tn, @league, @round, @home, @away)";
                using var kCmd = new MySqlCommand(kSql, conn);
                kCmd.Parameters.AddWithValue("@tn", tournNumber);
                kCmd.Parameters.AddWithValue("@league", league);
                kCmd.Parameters.AddWithValue("@round", round);
                kCmd.Parameters.AddWithValue("@home", knockoutTeams[i]);
                kCmd.Parameters.AddWithValue("@away", knockoutTeams[i + 1]);
                await kCmd.ExecuteNonQueryAsync();
            }

            string updateSql = "UPDATE tournament SET stage='knockout' WHERE id=1";
            using var updateCmd = new MySqlCommand(updateSql, conn);
            await updateCmd.ExecuteNonQueryAsync();
        }

        // ── CHECK KNOCKOUT PROGRESS ───────────────────────────
        private async Task CheckKnockoutProgress(MySqlConnection conn,
            string league, int tournNumber)
        {
            // Check if all knockout matches in current round are done
            string pendingSql = @"SELECT COUNT(*) FROM knockout_matches 
                                 WHERE league=@league AND tournament_number=@tn 
                                 AND status != 'completed'";
            using var pendingCmd = new MySqlCommand(pendingSql, conn);
            pendingCmd.Parameters.AddWithValue("@league", league);
            pendingCmd.Parameters.AddWithValue("@tn", tournNumber);
            long pending = (long)await pendingCmd.ExecuteScalarAsync()!;

            if (pending > 0) return;

            // Check if final is done
            string finalSql = @"SELECT COUNT(*) FROM knockout_matches 
                               WHERE league=@league AND tournament_number=@tn 
                               AND round='final' AND status='completed'";
            using var finalCmd = new MySqlCommand(finalSql, conn);
            finalCmd.Parameters.AddWithValue("@league", league);
            finalCmd.Parameters.AddWithValue("@tn", tournNumber);
            long finalDone = (long)await finalCmd.ExecuteScalarAsync()!;

            if (finalDone > 0)
            {
                if (league == "FIGHTERS")
                    await RecordLegends(conn, tournNumber);
                else
                    await StartNextLeague(conn, league, tournNumber);
            }
            else
            {
                // Generate next round
                await GenerateNextKnockoutRound(conn, league, tournNumber);
            }
        }

        // ── RECORD LEGENDS ────────────────────────────────────
        private async Task RecordLegends(MySqlConnection conn, int tournNumber)
        {
            string finalSql = @"SELECT winner_team_id, 
                CASE WHEN home_team_id = winner_team_id THEN away_team_id 
                     ELSE home_team_id END as runner_up
                FROM knockout_matches 
                WHERE tournament_number=@tn AND round='final' AND league='FIGHTERS'";
            using var finalCmd = new MySqlCommand(finalSql, conn);
            finalCmd.Parameters.AddWithValue("@tn", tournNumber);
            using var reader = await finalCmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            int winnerId = Convert.ToInt32(reader["winner_team_id"]);
            int runnerUpId = Convert.ToInt32(reader["runner_up"]);
            await reader.CloseAsync();

            // Record in history
            string historySql = @"INSERT INTO tournament_history 
                (tournament_number, league, winner_team_id, runner_up_team_id)
                VALUES (@tn, 'FIGHTERS', @winner, @runnerUp)";
            using var historyCmd = new MySqlCommand(historySql, conn);
            historyCmd.Parameters.AddWithValue("@tn", tournNumber);
            historyCmd.Parameters.AddWithValue("@winner", winnerId);
            historyCmd.Parameters.AddWithValue("@runnerUp", runnerUpId);
            await historyCmd.ExecuteNonQueryAsync();

            // Label winner and runner-up as LEGENDS
            string legendSql = @"UPDATE teams SET league='LEGENDS' 
                                WHERE id IN (@winner, @runnerUp)";
            using var legendCmd = new MySqlCommand(legendSql, conn);
            legendCmd.Parameters.AddWithValue("@winner", winnerId);
            legendCmd.Parameters.AddWithValue("@runnerUp", runnerUpId);
            await legendCmd.ExecuteNonQueryAsync();

            // Tournament finished — set next start date (3 days later)
            string finishSql = @"UPDATE tournament SET 
                status='finished', 
                next_start_date=DATE_ADD(NOW(), INTERVAL 3 DAY)
                WHERE id=1";
            using var finishCmd = new MySqlCommand(finishSql, conn);
            await finishCmd.ExecuteNonQueryAsync();
        }

        // ── START NEXT TOURNAMENT ─────────────────────────────
        private async Task StartNextTournament(MySqlConnection conn, int tournNumber)
        {
            int nextTournNumber = tournNumber + 1;

            // Legends drop to FIGHTERS
            string legendSql = @"UPDATE teams SET league='FIGHTERS', 
                group_name=NULL, played=0, wins=0, draws=0, losses=0,
                goals_for=0, goals_against=0, goal_diff=0, points=0
                WHERE league='LEGENDS'";
            using var legendCmd = new MySqlCommand(legendSql, conn);
            await legendCmd.ExecuteNonQueryAsync();

            // Reset THE_BEGINNING teams stats
            string resetSql = @"UPDATE teams SET 
                group_name=NULL, played=0, wins=0, draws=0, losses=0,
                goals_for=0, goals_against=0, goal_diff=0, points=0
                WHERE league='THE_BEGINNING'";
            using var resetCmd = new MySqlCommand(resetSql, conn);
            await resetCmd.ExecuteNonQueryAsync();

            // Reset FIGHTERS teams stats
            string resetFightersSql = @"UPDATE teams SET 
                group_name=NULL, played=0, wins=0, draws=0, losses=0,
                goals_for=0, goals_against=0, goal_diff=0, points=0
                WHERE league='FIGHTERS'";
            using var resetFightersCmd = new MySqlCommand(resetFightersSql, conn);
            await resetFightersCmd.ExecuteNonQueryAsync();

            // Update tournament record
            string updateSql = @"UPDATE tournament SET 
                status='ongoing', stage='group',
                current_league='THE_BEGINNING',
                tournament_number=@nextTn,
                next_start_date=NULL
                WHERE id=1";
            using var updateCmd = new MySqlCommand(updateSql, conn);
            updateCmd.Parameters.AddWithValue("@nextTn", nextTournNumber);
            await updateCmd.ExecuteNonQueryAsync();

            // Assign groups and generate fixtures for THE_BEGINNING
            string tpgSql = "SELECT teams_per_group FROM tournament WHERE id=1";
            using var tpgCmd = new MySqlCommand(tpgSql, conn);
            int teamsPerGroup = Convert.ToInt32(await tpgCmd.ExecuteScalarAsync());

            await AssignGroups(conn, teamsPerGroup, "THE_BEGINNING", nextTournNumber);
            await GenerateGroupFixtures(conn, "THE_BEGINNING", nextTournNumber);
            await ScheduleMatches(conn, nextTournNumber);
        }

        // ── GENERATE NEXT KNOCKOUT ROUND ──────────────────────
        private async Task GenerateNextKnockoutRound(MySqlConnection conn,
            string league, int tournNumber)
        {
            // Get winners of current round
            string currentRoundSql = @"SELECT MAX(round) as current_round 
                                      FROM knockout_matches 
                                      WHERE league=@league AND tournament_number=@tn";
            using var crCmd = new MySqlCommand(currentRoundSql, conn);
            crCmd.Parameters.AddWithValue("@league", league);
            crCmd.Parameters.AddWithValue("@tn", tournNumber);
            string currentRound = (await crCmd.ExecuteScalarAsync())?.ToString() ?? "quarterfinal";

            string winnersSql = @"SELECT winner_team_id FROM knockout_matches 
                                 WHERE league=@league AND tournament_number=@tn 
                                 AND round=@round AND status='completed'";
            using var winnersCmd = new MySqlCommand(winnersSql, conn);
            winnersCmd.Parameters.AddWithValue("@league", league);
            winnersCmd.Parameters.AddWithValue("@tn", tournNumber);
            winnersCmd.Parameters.AddWithValue("@round", currentRound);
            using var winnersReader = await winnersCmd.ExecuteReaderAsync();

            List<int> winners = new();
            while (await winnersReader.ReadAsync())
                winners.Add(Convert.ToInt32(winnersReader["winner_team_id"]));
            await winnersReader.CloseAsync();

            string nextRound = currentRound == "quarterfinal" ? "semifinal" : "final";

            for (int i = 0; i < winners.Count - 1; i += 2)
            {
                string kSql = @"INSERT INTO knockout_matches 
                    (tournament_number, league, round, home_team_id, away_team_id)
                    VALUES (@tn, @league, @round, @home, @away)";
                using var kCmd = new MySqlCommand(kSql, conn);
                kCmd.Parameters.AddWithValue("@tn", tournNumber);
                kCmd.Parameters.AddWithValue("@league", league);
                kCmd.Parameters.AddWithValue("@round", nextRound);
                kCmd.Parameters.AddWithValue("@home", winners[i]);
                kCmd.Parameters.AddWithValue("@away", winners[i + 1]);
                await kCmd.ExecuteNonQueryAsync();
            }
        }

        private async Task StartNextLeague(MySqlConnection conn,
            string currentLeague, int tournNumber)
        {
            string nextLeague = currentLeague == "THE_BEGINNING" ? "FIGHTERS" : "LEGENDS";
            string updateSql = @"UPDATE tournament SET 
                current_league=@league, stage='group' WHERE id=1";
            using var updateCmd = new MySqlCommand(updateSql, conn);
            updateCmd.Parameters.AddWithValue("@league", nextLeague);
            await updateCmd.ExecuteNonQueryAsync();
        }
    }
}
