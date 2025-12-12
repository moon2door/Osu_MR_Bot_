using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Osu_MR_Bot.Models
{
    public class UserBotData
    {
        [JsonPropertyName("user_id")]
        public int UserId { get; set; }

        [JsonPropertyName("username")]
        public string Username { get; set; }

        [JsonPropertyName("current_pp")]
        public decimal CurrentPp { get; set; }

        [JsonPropertyName("global_rank")]
        public int? GlobalRank { get; set; }

        // [삭제] Top 50 퍼포먼스 저장 부분 삭제
        // [JsonPropertyName("top_100_scores")]
        // public List<OsuScore> Top100Scores { get; set; } 

        [JsonPropertyName("last_updated")]
        public DateTime LastUpdated { get; set; }
    }
}