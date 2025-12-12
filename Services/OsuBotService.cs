using System.Net.Http.Json;
using Osu_MR_Bot.Models;

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

        // 메시지를 보내야 할 때 이 함수를 호출합니다.
        public async Task ExecuteStartCommandAsync(string username, Func<string, Task>? onMessage = null)
        {
            if (string.IsNullOrEmpty(_accessToken))
            {
                Console.WriteLine("[Error] 토큰이 없습니다.");
                return;
            }

            Console.WriteLine($"\n[Command] '{username}' 유저에 대한 !m r start 작업 시작...");

            try
            {
                // 1. 유저 정보 조회 (ID를 알아내기 위해 필수)
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

                // 2. 최초 실행인지 확인 (DB 조회)
                bool isFirstTime = await CheckIfUserIsNewAsync(userData.Id);

                // 최초 실행일 경우 메시지 전송
                if (isFirstTime && onMessage != null)
                {
                    await onMessage("반갑습니다. 해당 봇은 5성 이상의 맵만 추천하니 참고해주시길 바랍니다.");
                    await onMessage("[분석중] 최초 1회에 한하여 유저를 분석중입니다.");
                }
                else if (!isFirstTime)
                {
                    Console.WriteLine($"[Info] {userData.Username}님은 이미 DB에 존재합니다. (업데이트 진행)");
                    await onMessage("[분석중] 해당 유저는 최초 실행이 아닙니다.");
                    await onMessage("[업데이트중] 기존에 저장되어 있던 내용을 업데이트 합니다.");
                    // 이미 존재하는 유저에게는 별도 메시지를 보내지 않거나, 필요하면 여기서 추가 가능
                }

                // 3. Top 50 조회
                string scoresUrl = $"https://osu.ppy.sh/api/v2/users/{userData.Id}/scores/best?mode=osu&limit=50";
                var scoresResponse = await _httpClient.GetAsync(scoresUrl);

                if (!scoresResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[Error] Top 50 조회 실패. 코드: {scoresResponse.StatusCode}");
                    return;
                }

                var topScores = await scoresResponse.Content.ReadFromJsonAsync<List<OsuScore>>();
                Console.WriteLine($"[Info] Top 50 기록 {topScores?.Count ?? 0}개 수신 완료.");

                // 4. 데이터 패키징
                var dataToSave = new UserBotData
                {
                    UserId = userData.Id,
                    Username = userData.Username,
                    CurrentPp = userData.Statistics.Pp,
                    GlobalRank = userData.Statistics.GlobalRank,
                    Top100Scores = topScores ?? new List<OsuScore>(),
                    LastUpdated = DateTime.UtcNow
                };

                // 5. 저장
                await SaveToFirebaseAsync(dataToSave);

                // 최초 실행일 경우 완료 메시지 전송
                if (isFirstTime && onMessage != null)
                {
                    await onMessage("[분석완료] 분석이 완료 되었습니다!");
                    await onMessage("자신의 pp현황을 업데이트 하고싶다면 !m r start 를 입력하여 주세요!");
                }
                else
                {
                    await onMessage("[업데이트완료] pp현황을 업데이트 했습니다!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] 작업 중 예외 발생: {ex.Message}");
            }
        }

        // 유저가 Firebase에 이미 있는지 확인하는 헬퍼 메서드
        private async Task<bool> CheckIfUserIsNewAsync(int userId)
        {
            string dbUrl = $"{_firebaseUrl}/users/{userId}.json?auth={_firebaseSecret}";
            try
            {
                var response = await _httpClient.GetAsync(dbUrl);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    // Firebase는 데이터가 없으면 "null" 문자열을 반환합니다.
                    if (string.IsNullOrEmpty(content) || content == "null")
                    {
                        return true; // 데이터 없음 -> 최초 실행
                    }
                    return false; // 데이터 있음 -> 재실행
                }
            }
            catch
            {
                // 에러 발생 시 안전하게 최초 실행으로 간주하거나 로그를 남김
            }
            return true;
        }

        private async Task SaveToFirebaseAsync(UserBotData data)
        {
            string dbUrl = $"{_firebaseUrl}/users/{data.UserId}.json?auth={_firebaseSecret}";

            try
            {
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