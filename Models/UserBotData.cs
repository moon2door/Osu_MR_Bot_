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

        [JsonPropertyName("last_updated")]
        public DateTime LastUpdated { get; set; }

        [JsonPropertyName("difficulty_preference")]
        public int DifficultyPreference { get; set; } = 3;

        [JsonPropertyName("recent_maps")]
        public List<int> RecentMaps { get; set; } = new List<int>();
    }
}