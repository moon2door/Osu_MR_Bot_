using System.Net.Http.Json;
using Osu_MR_Bot.Models;
using System.Text.Json.Serialization;
using System.Collections.Generic; // List 사용을 위해 추가
using System.Linq; // Count 등 사용
using System.Text.Json; // JsonSerializer 사용을 위해 추가

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
        private readonly Random _random = new Random();

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

        // [수정] start 명령어: 기존 난이도 설정을 유지하면서 업데이트
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

                // 2. 기존 데이터 확인 (난이도 설정 유지 목적)
                int currentDiffPref = 3; // 기본값 (어려움)
                bool isFirstTime = true;

                string dbUrl = $"{_firebaseUrl}/users/{userData.Id}.json?auth={_firebaseSecret}";
                var dbResponse = await _httpClient.GetAsync(dbUrl);

                if (dbResponse.IsSuccessStatusCode)
                {
                    var content = await dbResponse.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(content) && content != "null")
                    {
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var existingData = JsonSerializer.Deserialize<UserBotData>(content, options);
                        if (existingData != null)
                        {
                            isFirstTime = false;
                            // [핵심] 기존 설정이 있으면 가져옵니다.
                            currentDiffPref = existingData.DifficultyPreference;
                        }
                    }
                }

                // 3. 메시지 전송
                if (isFirstTime && onMessage != null)
                {
                    await onMessage("반갑습니다. 해당 봇은 서버에 저장되어 있는 맵만 추천하니 참고해주시길 바랍니다.");
                    await onMessage("명령어는 !m r help 를 통해 찾아 볼 수 있습니다.");
                    await onMessage("[분석중] 최초 1회에 한하여 유저를 분석중입니다.");
                }
                else if (!isFirstTime && onMessage != null)
                {
                    await onMessage("[분석중] 해당 유저는 최초 실행이 아닙니다.");
                    await onMessage("[업데이트중] 기존에 저장되어 있던 내용을 업데이트 합니다.");
                }

                // 4. 데이터 저장 (기존 난이도 설정 포함)
                var dataToSave = new UserBotData
                {
                    UserId = userData.Id,
                    Username = userData.Username,
                    CurrentPp = userData.Statistics.Pp,
                    GlobalRank = userData.Statistics.GlobalRank,
                    LastUpdated = DateTime.UtcNow,
                    DifficultyPreference = currentDiffPref // 유지된 설정값 적용
                };

                await SaveToFirebaseAsync(dataToSave);

                // 5. 완료 메시지
                if (isFirstTime && onMessage != null)
                {
                    await onMessage("[분석완료] 분석이 완료 되었습니다!");
                    await onMessage("기본 난이도는 '3 (어려움)'으로 설정됩니다.");
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

        // [신규] 난이도 설정 변경 (!m r diff [1~4])
        public async Task SetUserDifficultyAsync(string username, int diffLevel, Func<string, Task>? onMessage = null)
        {
            if (string.IsNullOrEmpty(_accessToken)) return;

            Console.WriteLine($"[Command] '{username}' 난이도 변경 요청: {diffLevel}");

            try
            {
                // 1. 유저 ID 조회
                string userUrl = $"https://osu.ppy.sh/api/v2/users/{username}/osu?key=username";
                var userResponse = await _httpClient.GetAsync(userUrl);
                if (!userResponse.IsSuccessStatusCode)
                {
                    if (onMessage != null) await onMessage("유저 정보를 찾을 수 없습니다.");
                    return;
                }
                var userData = await userResponse.Content.ReadFromJsonAsync<OsuUser>();
                if (userData == null) return;

                // 2. DB에서 유저 데이터 가져오기
                string dbUrl = $"{_firebaseUrl}/users/{userData.Id}.json?auth={_firebaseSecret}";
                var dbResponse = await _httpClient.GetAsync(dbUrl);
                UserBotData? userBotData = null;

                if (dbResponse.IsSuccessStatusCode)
                {
                    var content = await dbResponse.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(content) && content != "null")
                    {
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        userBotData = JsonSerializer.Deserialize<UserBotData>(content, options);
                    }
                }

                if (userBotData == null)
                {
                    if (onMessage != null) await onMessage("!m r start를 먼저 입력해주세요.");
                    return;
                }

                // 3. 설정 변경 및 저장
                userBotData.DifficultyPreference = diffLevel;
                userBotData.Username = userData.Username; // 이름도 최신화

                await SaveToFirebaseAsync(userBotData);

                // 4. 완료 메시지
                string diffName = diffLevel switch
                {
                    1 => "1 (쉬움)",
                    2 => "2 (보통)",
                    3 => "3 (어려움/기본)",
                    4 => "4 (매우 어려움)",
                    _ => diffLevel.ToString()
                };

                if (onMessage != null) await onMessage($"[설정완료] 추천 난이도가 '{diffName}'으로 변경되었습니다.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] 난이도 변경 중 오류: {ex.Message}");
                if (onMessage != null) await onMessage("오류가 발생했습니다.");
            }
        }

        public async Task RegisterMapStyleAsync(string senderUsername, int mapId, string style, Func<string, Task>? onMessage = null)
        {
            if (string.IsNullOrEmpty(_accessToken)) return;

            Console.WriteLine($"[Command] 맵 등록 요청: ID={mapId}, Style={style}, By={senderUsername}");

            try
            {
                // 1. osu! API에서 맵 정보(SR) 가져오기
                string mapUrl = $"https://osu.ppy.sh/api/v2/beatmaps/{mapId}";
                var response = await _httpClient.GetAsync(mapUrl);

                if (!response.IsSuccessStatusCode)
                {
                    if (onMessage != null) await onMessage($"맵 정보를 가져올 수 없습니다. (ID: {mapId})");
                    return;
                }

                var mapData = await response.Content.ReadFromJsonAsync<OsuBeatmap>();
                if (mapData == null) return;

                // [거름망 1] osu!standard 모드인지 확인
                if (mapData.ModeInt != 0)
                {
                    if (onMessage != null) await onMessage($"[등록실패] osu!standard 모드의 맵만 등록 가능합니다.");
                    return;
                }

                // [거름망 2] Ranked / Loved / Approved 맵인지 확인
                var validStatuses = new[] { "ranked", "loved", "approved" };
                if (!validStatuses.Contains(mapData.Status))
                {
                    if (onMessage != null) await onMessage($"[등록실패] Ranked 또는 Loved 상태의 맵만 등록 가능합니다. (현재: {mapData.Status})");
                    return;
                }

                int difficultyFloor = (int)Math.Floor(mapData.DifficultyRating);
                string styleLower = style.ToLower();

                // 3. Firebase 저장 경로 설정
                string dbUrl = $"{_firebaseUrl}/styles/{styleLower}/{difficultyFloor}/{mapId}.json?auth={_firebaseSecret}";

                // [추가] 이미 존재하는지 확인
                var checkResponse = await _httpClient.GetAsync(dbUrl);
                if (checkResponse.IsSuccessStatusCode)
                {
                    string existingContent = await checkResponse.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(existingContent) && existingContent != "null")
                    {
                        Console.WriteLine($"[Info] 이미 등록된 맵입니다: {mapId} ({styleLower})");
                        if (onMessage != null) await onMessage($"[{mapId}] 이미 있는 곡입니다!");
                        return; // 함수 종료
                    }
                }

                // 저장할 데이터
                var mapInfoToSave = new
                {
                    Title = mapData.BeatmapSet.Title,
                    Artist = mapData.BeatmapSet.Artist,
                    StarRating = mapData.DifficultyRating,
                    AddedAt = DateTime.UtcNow,
                    AddedBy = senderUsername
                };

                // PUT으로 저장
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

        public async Task RegisterBatchMapStylesAsync(string senderUsername, List<int> mapIds, string style, Func<string, Task>? onMessage = null)
        {
            if (string.IsNullOrEmpty(_accessToken)) return;

            Console.WriteLine($"[Command] 일괄 맵 등록 요청: {mapIds.Count}개, Style={style}, By={senderUsername}");
            if (onMessage != null) await onMessage($"[처리중] {mapIds.Count}개의 맵을 '{style}' 스타일로 등록을 시도합니다...");

            int successCount = 0;
            int duplicateCount = 0;
            int failCount = 0;
            int filterCount = 0;

            foreach (var mapId in mapIds)
            {
                try
                {
                    string mapUrl = $"https://osu.ppy.sh/api/v2/beatmaps/{mapId}";
                    var response = await _httpClient.GetAsync(mapUrl);

                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[Error] 맵 정보 조회 실패: ID={mapId}");
                        failCount++;
                        continue;
                    }

                    var mapData = await response.Content.ReadFromJsonAsync<OsuBeatmap>();
                    if (mapData == null)
                    {
                        failCount++;
                        continue;
                    }

                    // [거름망 적용]
                    var validStatuses = new[] { "ranked", "loved", "approved" };
                    if (mapData.ModeInt != 0 || !validStatuses.Contains(mapData.Status))
                    {
                        Console.WriteLine($"[Skip] 조건 불충족 맵: {mapId} (Mode: {mapData.ModeInt}, Status: {mapData.Status})");
                        filterCount++;
                        continue;
                    }

                    int difficultyFloor = (int)Math.Floor(mapData.DifficultyRating);
                    string styleLower = style.ToLower();

                    string dbUrl = $"{_firebaseUrl}/styles/{styleLower}/{difficultyFloor}/{mapId}.json?auth={_firebaseSecret}";

                    // 중복 확인
                    var checkResponse = await _httpClient.GetAsync(dbUrl);
                    if (checkResponse.IsSuccessStatusCode)
                    {
                        string existingContent = await checkResponse.Content.ReadAsStringAsync();
                        if (!string.IsNullOrEmpty(existingContent) && existingContent != "null")
                        {
                            duplicateCount++;
                            continue;
                        }
                    }

                    var mapInfoToSave = new
                    {
                        Title = mapData.BeatmapSet.Title,
                        Artist = mapData.BeatmapSet.Artist,
                        StarRating = mapData.DifficultyRating,
                        AddedAt = DateTime.UtcNow,
                        AddedBy = senderUsername
                    };

                    var saveResponse = await _httpClient.PutAsJsonAsync(dbUrl, mapInfoToSave);
                    if (saveResponse.IsSuccessStatusCode)
                    {
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error] 일괄 처리 중 예외 (ID: {mapId}): {ex.Message}");
                    failCount++;
                }
            }

            // 결과 리포트
            string resultMsg = $"[완료] 총 {mapIds.Count}개 중 성공: {successCount}, 중복: {duplicateCount}, 조건미달: {filterCount}, 오류: {failCount}";
            Console.WriteLine(resultMsg);
            if (onMessage != null) await onMessage(resultMsg);
        }

        // [수정] 맵 추천 기능 (!m r req [style]) - DB에 저장된 난이도 설정값 사용
        public async Task RecommendMapAsync(string username, string style, Func<string, Task>? onMessage = null)
        {
            if (string.IsNullOrEmpty(_accessToken)) return;

            Console.WriteLine($"[Command] '{username}' 맵 추천 요청: Style={style}");

            try
            {
                // 1. 유저 정보 조회
                string userUrl = $"https://osu.ppy.sh/api/v2/users/{username}/osu?key=username";
                var userResponse = await _httpClient.GetAsync(userUrl);

                if (!userResponse.IsSuccessStatusCode)
                {
                    if (onMessage != null) await onMessage("유저 정보를 찾을 수 없습니다.");
                    return;
                }

                var userData = await userResponse.Content.ReadFromJsonAsync<OsuUser>();
                if (userData == null) return;

                // 2. Firebase 유저 데이터 조회
                string userDbUrl = $"{_firebaseUrl}/users/{userData.Id}.json?auth={_firebaseSecret}";
                var dbResponse = await _httpClient.GetAsync(userDbUrl);

                UserBotData? userBotData = null;
                if (dbResponse.IsSuccessStatusCode)
                {
                    var content = await dbResponse.Content.ReadAsStringAsync();

                    if (!string.IsNullOrEmpty(content) && content != "null")
                    {
                        // JSON 대소문자 무시 (필수)
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        userBotData = JsonSerializer.Deserialize<UserBotData>(content, options);
                    }
                }

                if (userBotData == null)
                {
                    if (onMessage != null) await onMessage("!m r start를 입력해서 유저 정보를 먼저 저장해 주세요.");
                    return;
                }

                // 3. 추천 난이도 계산
                double currentPp = (double)userBotData.CurrentPp;
                double baseStarRating = Math.Pow(currentPp, 0.4) * 0.195;

                // DB에서 설정값(1~4)을 가져와 오프셋 적용
                double offset = 0.0;
                string diffName = "어려움(기본)";
                int pref = userBotData.DifficultyPreference;

                switch (pref)
                {
                    case 1: offset = -0.7; diffName = "쉬움"; break;
                    case 2: offset = -0.3; diffName = "보통"; break;
                    case 3: offset = 0.0; diffName = "어려움"; break;
                    case 4: offset = 0.2; diffName = "매우 어려움"; break;
                    default: offset = 0.0; diffName = "어려움(초기화)"; break;
                }

                double targetStarRating = Math.Max(0, baseStarRating + offset);
                int difficultyFloor = (int)Math.Floor(targetStarRating);

                Console.WriteLine($"[Info] {username} (PP: {currentPp}, Pref: {pref}) -> Target: {targetStarRating:F2} (Floor: {difficultyFloor})");

                // 4. DB 조회
                string styleLower = style.ToLower();
                string mapDbUrl = $"{_firebaseUrl}/styles/{styleLower}/{difficultyFloor}.json?auth={_firebaseSecret}";

                var mapResponse = await _httpClient.GetAsync(mapDbUrl);
                if (!mapResponse.IsSuccessStatusCode)
                {
                    if (onMessage != null) await onMessage($"[{styleLower}] 해당 난이도({difficultyFloor}성)에 등록된 맵을 가져오는데 실패했습니다.");
                    return;
                }

                var mapContent = await mapResponse.Content.ReadAsStringAsync();
                if (string.IsNullOrEmpty(mapContent) || mapContent == "null")
                {
                    if (onMessage != null) await onMessage($"[{styleLower}] 현재 {diffName} 난이도 ({difficultyFloor}성 구간)에 추천할 맵이 없습니다.");
                    return;
                }

                // JSON 대소문자 무시 (필수)
                var optionsMap = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var mapsDict = JsonSerializer.Deserialize<Dictionary<string, DbMapInfo>>(mapContent, optionsMap);

                if (mapsDict == null || mapsDict.Count == 0)
                {
                    if (onMessage != null) await onMessage($"[{styleLower}] 현재 {diffName} 난이도 ({difficultyFloor}성 구간)에 추천할 맵이 없습니다.");
                    return;
                }

                // 5. 랜덤 선택
                var randomEntry = mapsDict.ElementAt(_random.Next(mapsDict.Count));
                string mapId = randomEntry.Key;
                DbMapInfo mapInfo = randomEntry.Value;

                // 6. 메시지 전송
                string link = $"https://osu.ppy.sh/b/{mapId}";
                // F2 포맷팅 적용
                string message = $"[추천] {mapInfo.Title} [{mapInfo.StarRating:F2}★] - {link}";

                if (onMessage != null) await onMessage(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] 맵 추천 중 오류: {ex.Message}");
                if (onMessage != null) await onMessage("오류가 발생했습니다.");
            }
        }

        public async Task<string> GetMapContributionStatsAsync()
        {
            string dbUrl = $"{_firebaseUrl}/styles.json?auth={_firebaseSecret}";

            try
            {
                var response = await _httpClient.GetFromJsonAsync<Dictionary<string, Dictionary<string, Dictionary<string, DbMapInfo>>>>(dbUrl);

                if (response == null) return "등록된 맵 데이터가 없습니다.";

                var stats = new Dictionary<string, int>();
                int totalMaps = 0;

                foreach (var stylePair in response)
                {
                    foreach (var diffPair in stylePair.Value)
                    {
                        foreach (var mapPair in diffPair.Value)
                        {
                            var info = mapPair.Value;
                            if (info != null && !string.IsNullOrEmpty(info.AddedBy))
                            {
                                if (!stats.ContainsKey(info.AddedBy))
                                    stats[info.AddedBy] = 0;

                                stats[info.AddedBy]++;
                                totalMaps++;
                            }
                        }
                    }
                }

                var sortedStats = stats.OrderByDescending(x => x.Value).ToList();
                var result = new System.Text.StringBuilder();
                result.AppendLine($"=== 맵 기여 현황 (총 {totalMaps}개) ===");

                int rank = 1;
                foreach (var stat in sortedStats)
                {
                    result.AppendLine($"{rank}. {stat.Key}: {stat.Value}개");
                    rank++;
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                return $"[Error] 통계 조회 중 오류 발생: {ex.Message}";
            }
        }

        private async Task<bool> CheckIfUserIsNewAsync(int userId)
        {
            string dbUrl = $"{_firebaseUrl}/users/{userId}.json?auth={_firebaseSecret}";
            try
            {
                var response = await _httpClient.GetAsync(dbUrl);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    if (string.IsNullOrEmpty(content) || content == "null")
                    {
                        return true;
                    }
                    return false;
                }
            }
            catch
            {
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
                    Console.WriteLine($"[Success] FireBase 저장 완료! (User: {data.Username}, DiffPref: {data.DifficultyPreference})");
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

        // [내부 모델 클래스들]
        private class DbMapInfo
        {
            [JsonPropertyName("Title")]
            public string Title { get; set; }

            [JsonPropertyName("Artist")]
            public string Artist { get; set; }

            [JsonPropertyName("StarRating")]
            public double StarRating { get; set; }

            [JsonPropertyName("AddedBy")]
            public string AddedBy { get; set; }
        }

        private class SavedMapInfo
        {
            public string AddedBy { get; set; }
        }
    }
}