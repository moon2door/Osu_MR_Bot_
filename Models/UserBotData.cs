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

        // [신규] 난이도 선호도 (1:쉬움, 2:보통, 3:어려움, 4:매우어려움)
        // 기본값은 3 (어려움/기본)
        [JsonPropertyName("difficulty_preference")]
        public int DifficultyPreference { get; set; } = 3;
    }
}