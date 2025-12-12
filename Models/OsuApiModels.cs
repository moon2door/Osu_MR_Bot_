using System.Text.Json.Serialization;

namespace Osu_MR_Bot.Models
{
    // 토큰 응답
    public class OsuTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; }
    }

    // 유저 정보 응답
    public class OsuUser
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("username")]
        public string Username { get; set; }

        [JsonPropertyName("statistics")]
        public UserStatistics Statistics { get; set; }
    }

    public class UserStatistics
    {
        [JsonPropertyName("pp")]
        public decimal Pp { get; set; }

        [JsonPropertyName("global_rank")]
        public int? GlobalRank { get; set; }
    }

    // 점수(Score) 응답
    public class OsuScore
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("pp")]
        public double? Pp { get; set; }

        [JsonPropertyName("rank")]
        public string Rank { get; set; }

        [JsonPropertyName("accuracy")]
        public double Accuracy { get; set; }

        [JsonPropertyName("beatmapset")]
        public BeatmapSetInfo BeatmapSet { get; set; }
    }

    public class BeatmapSetInfo
    {
        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("artist")]
        public string Artist { get; set; }
    }

    // [추가] 맵 상세 정보 (난이도 확인용)
    public class OsuBeatmap
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("difficulty_rating")]
        public double DifficultyRating { get; set; }

        [JsonPropertyName("beatmapset_id")]
        public int BeatmapSetId { get; set; }

        [JsonPropertyName("beatmapset")]
        public BeatmapSetInfo BeatmapSet { get; set; }

        // [신규] 맵 상태 (ranked, loved, etc.)
        [JsonPropertyName("status")]
        public string Status { get; set; }

        // [신규] 게임 모드 (0 = osu!, 1 = taiko, ...)
        [JsonPropertyName("mode_int")]
        public int ModeInt { get; set; }
    }
}