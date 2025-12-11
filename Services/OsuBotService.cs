using System.Net.Http.Json;
using Osu_MR_Bot.Models; // 분리한 모델 사용

namespace Osu_MR_Bot.Services
{
    public class OsuBotService
    {
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _firebaseUrl;
        private readonly string _firebaseSecret;
        private readonly HttpClient _httpClient;

        private string _accessToken;

        public OsuBotService(int clientId, string clientSecret, string firebaseUrl, string firebaseSecret)
        {
            _clientId = clientId.ToString();
            _clientSecret = clientSecret;
            _firebaseUrl = firebaseUrl;
            _firebaseSecret = firebaseSecret;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        public async Task ConnectAsync()
        {
            Console.WriteLine("[Bot] 서버 인증을 시도합니다...");
            var tokenUrl = "https://osu.ppy.sh/oauth/token";

            var requestData = new
            {
                client_id = _clientId,
                client_secret = _clientSecret,
                grant_type = "client_credentials",
                scope = "public"
            };

            try
            {
                var response = await _httpClient.PostAsJsonAsync(tokenUrl, requestData);

                if (response.IsSuccessStatusCode)
                {
                    var tokenData = await response.Content.ReadFromJsonAsync<OsuTokenResponse>();
                    if (tokenData != null)
                    {
                        _accessToken = tokenData.AccessToken;
                        _httpClient.DefaultRequestHeaders.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

                        Console.WriteLine($"[Bot] 인증 성공! (토큰 만료: {tokenData.ExpiresIn}초)");
                    }
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[Bot] 인증 실패: {response.StatusCode} / {error}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bot] 연결 오류: {ex.Message}");
            }
        }

        public async Task ExecuteStartCommandAsync(string username)
        {
            if (string.IsNullOrEmpty(_accessToken))
            {
                Console.WriteLine("[Error] 토큰이 없습니다.");
                return;
            }

            Console.WriteLine($"\n[Command] '{username}' 유저에 대한 !m r start 작업 시작...");

            try
            {
                // 1. 유저 정보 조회
                string userUrl = $"https://osu.ppy.sh/api/v2/users/{username}/osu?key=username";
                var userResponse = await _httpClient.GetAsync(userUrl);

                if (!userResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[Error] 유저 조회 실패. 코드: {userResponse.StatusCode}");
                    return;
                }

                var userData = await userResponse.Content.ReadFromJsonAsync<OsuUser>();
                if (userData == null) return;

                Console.WriteLine($"[Info] 유저 식별: {userData.Username} (ID: {userData.Id})");

                // 2. Top 100 조회
                string scoresUrl = $"https://osu.ppy.sh/api/v2/users/{userData.Id}/scores/best?mode=osu&limit=100";
                var scoresResponse = await _httpClient.GetAsync(scoresUrl);

                if (!scoresResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[Error] Top 100 조회 실패. 코드: {scoresResponse.StatusCode}");
                    return;
                }

                var topScores = await scoresResponse.Content.ReadFromJsonAsync<List<OsuScore>>();
                Console.WriteLine($"[Info] Top 100 기록 {topScores?.Count ?? 0}개 수신 완료.");

                // 3. 데이터 패키징
                var dataToSave = new UserBotData
                {
                    UserId = userData.Id,
                    Username = userData.Username,
                    CurrentPp = userData.Statistics.Pp,
                    GlobalRank = userData.Statistics.GlobalRank,
                    Top100Scores = topScores ?? new List<OsuScore>(),
                    LastUpdated = DateTime.UtcNow
                };

                // 4. 저장
                await SaveToFirebaseAsync(dataToSave);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] 작업 중 예외 발생: {ex.Message}");
            }
        }

        private async Task SaveToFirebaseAsync(UserBotData data)
        {
            string dbUrl = $"{_firebaseUrl}/users/{data.UserId}.json?auth={_firebaseSecret}";

            try
            {
                // PUT은 덮어쓰기(갱신) 효과가 있습니다.
                var response = await _httpClient.PutAsJsonAsync(dbUrl, data);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[Success] FireBase 저장 완료! (User: {data.Username})");
                }
                else
                {
                    string msg = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[Error] FireBase 저장 실패: {msg}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] FireBase 연결 오류: {ex.Message}");
            }
        }
    }
}