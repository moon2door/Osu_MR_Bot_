using System.Net.Http.Json;
using Osu_MR_Bot.Models;
using System.Text.Json.Serialization;

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
                    if (onMessage != null) await onMessage("유저를 찾을 수 없습니다.");
                    return;
                }

                var userData = await userResponse.Content.ReadFromJsonAsync<OsuUser>();
                if (userData == null) return;

                Console.WriteLine($"[Info] 유저 식별: {userData.Username} (ID: {userData.Id})");

                // 2. 최초 실행인지 확인 (DB 조회)
                bool isFirstTime = await CheckIfUserIsNewAsync(userData.Id);

                // 3. 메시지 전송 (상황별)
                if (isFirstTime && onMessage != null)
                {
                    await onMessage("반갑습니다. 해당 봇은 5성 이상의 맵만 추천하니 참고해주시길 바랍니다.");
                    await onMessage("[분석중] 최초 1회에 한하여 유저를 분석중입니다.");
                }
                else if (!isFirstTime && onMessage != null)
                {
                    await onMessage("[분석중] 해당 유저는 최초 실행이 아닙니다.");
                    await onMessage("[업데이트중] 기존에 저장되어 있던 내용을 업데이트 합니다.");
                }

                // [삭제] Top 50 조회 및 저장 로직을 완전히 제거했습니다.

                // 4. 데이터 패키징 (Top 50 점수 목록 제외, 메타데이터만 포함)
                var dataToSave = new UserBotData
                {
                    UserId = userData.Id,
                    Username = userData.Username,
                    CurrentPp = userData.Statistics.Pp,
                    GlobalRank = userData.Statistics.GlobalRank,
                    LastUpdated = DateTime.UtcNow
                };

                // 5. 저장
                await SaveToFirebaseAsync(dataToSave);

                // 6. 완료 메시지 전송
                if (isFirstTime && onMessage != null)
                {
                    await onMessage("[분석완료] 분석이 완료 되었습니다!");
                    await onMessage("자신의 pp현황을 업데이트 하고싶다면 !m r start 를 입력하여 주세요!");
                }
                else if (onMessage != null)
                {
                    await onMessage("[업데이트완료] pp현황을 업데이트 했습니다!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] 작업 중 예외 발생: {ex.Message}");
                if (onMessage != null) await onMessage("작업 중 예상치 못한 오류가 발생했습니다.");
            }
        }

        // [신규] !m o [맵ID] [스타일] 처리 로직
        public async Task RegisterMapStyleAsync(string senderUsername, int mapId, string style, Func<string, Task>? onMessage = null)
        {
            if (string.IsNullOrEmpty(_accessToken)) return;

            Console.WriteLine($"[Command] 맵 등록 요청: ID={mapId}, Style={style}, By={senderUsername}");

            try
            {
                // 1. osu! API에서 맵 정보(SR) 가져오기
                string mapUrl = $"https://osu.ppy.sh/api/v2/beatmaps/{mapId}";
                var response = await _httpClient.GetAsync(mapUrl);

                // ... (맵 정보 조회 및 난이도 계산 로직 유지)
                if (!response.IsSuccessStatusCode)
                {
                    if (onMessage != null) await onMessage($"맵 정보를 가져올 수 없습니다. (ID: {mapId})");
                    return;
                }

                var mapData = await response.Content.ReadFromJsonAsync<OsuBeatmap>();
                if (mapData == null) return;

                int difficultyFloor = (int)Math.Floor(mapData.DifficultyRating);
                string styleLower = style.ToLower(); // 스타일은 소문자로 통일

                // 3. Firebase 저장 경로: styles/{style}/{difficultyFloor}/{mapId}
                string dbUrl = $"{_firebaseUrl}/styles/{styleLower}/{difficultyFloor}/{mapId}.json?auth={_firebaseSecret}";

                // 저장할 데이터 (맵 제목, 아티스트, 정확한 SR)
                var mapInfoToSave = new
                {
                    Title = mapData.BeatmapSet.Title,
                    Artist = mapData.BeatmapSet.Artist,
                    StarRating = mapData.DifficultyRating,
                    AddedAt = DateTime.UtcNow,
                    AddedBy = senderUsername // [추가] 맵을 추가한 사람의 닉네임
                };

                // PUT으로 저장 (덮어쓰기)
                var saveResponse = await _httpClient.PutAsJsonAsync(dbUrl, mapInfoToSave);

                if (saveResponse.IsSuccessStatusCode)
                {
                    string msg = $"[등록완료] '{mapData.BeatmapSet.Title}' 맵이 '{styleLower}' 스타일 / '{difficultyFloor}성' 구간에 등록되었습니다. (추가자: {senderUsername})";
                    Console.WriteLine(msg);
                    if (onMessage != null) await onMessage(msg);
                }
                else
                {
                    if (onMessage != null) await onMessage("DB 저장에 실패했습니다.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] 맵 등록 중 오류: {ex.Message}");
                if (onMessage != null) await onMessage("오류가 발생했습니다.");
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