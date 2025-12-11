using System.Text.Json.Serialization;

namespace Osu_MR_Bot.Models
{
    public class UserBotData
    {
        public int UserId { get; set; }
        public string Username { get; set; }
        public decimal CurrentPp { get; set; }
        public int? GlobalRank { get; set; }
        public List<OsuScore> Top100Scores { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}