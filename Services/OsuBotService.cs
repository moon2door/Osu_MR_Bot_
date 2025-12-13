using System.Net.Http.Json;
using Osu_MR_Bot.Models;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

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

        // [신규] 유저 밴 처리 (콘솔 전용)
        public async Task BanUserAsync(int userId)
        {
            if (string.IsNullOrEmpty(_accessToken)) return;

            try
            {
                string dbUrl = $"{_firebaseUrl}/banned_users/{userId}.json?auth={_firebaseSecret}";
                var banData = new { BannedAt = DateTime.UtcNow, Reason = "Manual Ban" };

                var response = await _httpClient.PutAsJsonAsync(dbUrl, banData);
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[System] 유저({userId})를 성공적으로 밴 처리했습니다.");
                }
                else
                {
                    Console.WriteLine($"[Error] 밴 처리 실패: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] 밴 처리 중 오류: {ex.Message}");
            }
        }

        // [신규] 밴 여부 확인 헬퍼
        private async Task<bool> IsUserBannedAsync(int userId)
        {
            try
            {
                string dbUrl = $"{_firebaseUrl}/banned_users/{userId}.json?auth={_firebaseSecret}";
                var response = await _httpClient.GetAsync(dbUrl);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    // 데이터가 존재하면 밴 된 상태
                    return !string.IsNullOrEmpty(content) && content != "null";
                }
            }
            catch { }
            return false;
        }

        // [신규] 유저 정보 조회 헬퍼 (중복 코드 제거용)
        private async Task<OsuUser?> GetOsuUserAsync(string username)
        {
            try
            {
                string userUrl = $"https://osu.ppy.sh/api/v2/users/{username}/osu?key=username";
                var userResponse = await _httpClient.GetAsync(userUrl);
                if (userResponse.IsSuccessStatusCode)
                {
                    return await userResponse.Content.ReadFromJsonAsync<OsuUser>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] 유저 조회 오류: {ex.Message}");
            }
            return null;
        }

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
                var userData = await GetOsuUserAsync(username);
                if (userData == null)
                {
                    if (onMessage != null) await onMessage("유저를 찾을 수 없습니다.");
                    return;
                }

                // [밴 확인]
                if (await IsUserBannedAsync(userData.Id))
                {
                    Console.WriteLine($"[Ignore] Banned User: {username} ({userData.Id})");
                    return; // 밴 된 유저는 무시
                }

                Console.WriteLine($"[Info] 유저 식별: {userData.Username} (ID: {userData.Id})");

                // 기존 데이터 확인
                int currentDiffPref = 3;
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
                            currentDiffPref = existingData.DifficultyPreference;
                        }
                    }
                }

                // 메시지 전송
                if (isFirstTime && onMessage != null)
                {
                    await onMessage("반갑습니다. 해당 봇은 유저를 분석하여 수준에 맞는 곡을 추천해주는 봇입니다.");
                    await onMessage("명령어는 !m r help 를 통해 찾아 볼 수 있습니다.");
                    await onMessage("[분석중] 최초 1회에 한하여 유저를 분석중입니다.");
                }
                else if (!isFirstTime && onMessage != null)
                {
                    await onMessage("[분석중] 해당 유저는 최초 실행이 아닙니다.");
                    await onMessage("[업데이트중] 기존에 저장되어 있던 내용을 업데이트 합니다.");
                    await onMessage("명령어는 !m r help 를 통해 찾아 볼 수 있습니다.");
                }

                var dataToSave = new UserBotData
                {
                    UserId = userData.Id,
                    Username = userData.Username,
                    CurrentPp = userData.Statistics.Pp,
                    GlobalRank = userData.Statistics.GlobalRank,
                    LastUpdated = DateTime.UtcNow,
                    DifficultyPreference = currentDiffPref
                };

                await SaveToFirebaseAsync(dataToSave);

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

        public async Task SetUserDifficultyAsync(string username, int diffLevel, Func<string, Task>? onMessage = null)
        {
            if (string.IsNullOrEmpty(_accessToken)) return;

            Console.WriteLine($"[Command] '{username}' 난이도 변경 요청: {diffLevel}");

            try
            {
                var userData = await GetOsuUserAsync(username);
                if (userData == null)
                {
                    if (onMessage != null) await onMessage("유저 정보를 찾을 수 없습니다.");
                    return;
                }

                // [밴 확인]
                if (await IsUserBannedAsync(userData.Id))
                {
                    Console.WriteLine($"[Ignore] Banned User: {username} ({userData.Id})");
                    return;
                }

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

                userBotData.DifficultyPreference = diffLevel;
                userBotData.Username = userData.Username;

                await SaveToFirebaseAsync(userBotData);

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
                // [수정] 등록 전 유저 ID 확인 및 밴 체크
                var userData = await GetOsuUserAsync(senderUsername);
                if (userData == null)
                {
                    // 유저 정보를 못 찾으면 등록을 막거나 경고할 수 있음 (일단 로그만 남기고 진행할 수도 있으나 보안상 막는게 좋음)
                    Console.WriteLine($"[Warning] 맵 등록 시도자의 정보를 찾을 수 없음: {senderUsername}");
                    return;
                }

                if (await IsUserBannedAsync(userData.Id))
                {
                    Console.WriteLine($"[Ignore] Banned User trying to register map: {senderUsername} ({userData.Id})");
                    return;
                }

                // 1. 맵 정보 가져오기
                string mapUrl = $"https://osu.ppy.sh/api/v2/beatmaps/{mapId}";
                var response = await _httpClient.GetAsync(mapUrl);

                if (!response.IsSuccessStatusCode)
                {
                    if (onMessage != null) await onMessage($"맵 정보를 가져올 수 없습니다. (ID: {mapId})");
                    return;
                }

                var mapData = await response.Content.ReadFromJsonAsync<OsuBeatmap>();
                if (mapData == null) return;

                if (mapData.ModeInt != 0)
                {
                    if (onMessage != null) await onMessage($"[등록실패] osu!standard 모드의 맵만 등록 가능합니다.");
                    return;
                }

                var validStatuses = new[] { "ranked", "loved", "approved" };
                if (!validStatuses.Contains(mapData.Status))
                {
                    if (onMessage != null) await onMessage($"[등록실패] Ranked 또는 Loved 상태의 맵만 등록 가능합니다. (현재: {mapData.Status})");
                    return;
                }

                int difficultyFloor = (int)Math.Floor(mapData.DifficultyRating);
                string styleLower = style.ToLower();

                string dbUrl = $"{_firebaseUrl}/styles/{styleLower}/{difficultyFloor}/{mapId}.json?auth={_firebaseSecret}";

                var checkResponse = await _httpClient.GetAsync(dbUrl);
                if (checkResponse.IsSuccessStatusCode)
                {
                    string existingContent = await checkResponse.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(existingContent) && existingContent != "null")
                    {
                        Console.WriteLine($"[Info] 이미 등록된 맵입니다: {mapId} ({styleLower})");
                        if (onMessage != null) await onMessage($"[{mapId}] 이미 있는 곡입니다!");
                        return;
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

            // [수정] 일괄 등록 전 밴 체크
            var userData = await GetOsuUserAsync(senderUsername);
            if (userData == null || await IsUserBannedAsync(userData.Id))
            {
                Console.WriteLine($"[Ignore] Banned User or Not Found: {senderUsername}");
                return;
            }

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
                    if (saveResponse.IsSuccessStatusCode) successCount++;
                    else failCount++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error] 일괄 처리 중 예외 (ID: {mapId}): {ex.Message}");
                    failCount++;
                }
            }

            string resultMsg = $"[완료] 총 {mapIds.Count}개 중 성공: {successCount}, 중복: {duplicateCount}, 조건미달: {filterCount}, 오류: {failCount}";
            Console.WriteLine(resultMsg);
            if (onMessage != null) await onMessage(resultMsg);
        }

        public async Task RecommendMapAsync(string username, string style, Func<string, Task>? onMessage = null)
        {
            if (string.IsNullOrEmpty(_accessToken)) return;

            Console.WriteLine($"[Command] '{username}' 맵 추천 요청: Style={style}");

            try
            {
                var userData = await GetOsuUserAsync(username);
                if (userData == null)
                {
                    if (onMessage != null) await onMessage("유저 정보를 찾을 수 없습니다.");
                    return;
                }

                // [밴 확인]
                if (await IsUserBannedAsync(userData.Id))
                {
                    Console.WriteLine($"[Ignore] Banned User: {username} ({userData.Id})");
                    return;
                }

                string userDbUrl = $"{_firebaseUrl}/users/{userData.Id}.json?auth={_firebaseSecret}";
                var dbResponse = await _httpClient.GetAsync(userDbUrl);

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
                    if (onMessage != null) await onMessage("!m r start를 입력해서 유저 정보를 먼저 저장해 주세요.");
                    return;
                }

                double currentPp = (double)userBotData.CurrentPp;
                double baseStarRating = Math.Pow(currentPp, 0.4) * 0.195;

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

                var optionsMap = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var mapsDict = JsonSerializer.Deserialize<Dictionary<string, DbMapInfo>>(mapContent, optionsMap);

                if (mapsDict == null || mapsDict.Count == 0)
                {
                    if (onMessage != null) await onMessage($"[{styleLower}] 현재 {diffName} 난이도 ({difficultyFloor}성 구간)에 추천할 맵이 없습니다.");
                    return;
                }

                var randomEntry = mapsDict.ElementAt(_random.Next(mapsDict.Count));
                string mapId = randomEntry.Key;
                DbMapInfo mapInfo = randomEntry.Value;

                string link = $"https://osu.ppy.sh/b/{mapId}";
                string message = $"[추천] {mapInfo.Title} [{mapInfo.StarRating:F2}★] - {link}";

                if (onMessage != null) await onMessage(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] 맵 추천 중 오류: {ex.Message}");
                if (onMessage != null) await onMessage("오류가 발생했습니다.");
            }
        }

        public async Task RequestMapDeletionAsync(string requester, int mapId, Func<string, Task>? onMessage = null)
        {
            if (string.IsNullOrEmpty(_accessToken)) return;

            Console.WriteLine($"[Command] '{requester}' 맵 삭제 요청: {mapId}");

            try
            {
                // [밴 확인]
                var userData = await GetOsuUserAsync(requester);
                if (userData == null || await IsUserBannedAsync(userData.Id))
                {
                    Console.WriteLine($"[Ignore] Banned User or Not Found: {requester}");
                    return;
                }

                string mapUrl = $"https://osu.ppy.sh/api/v2/beatmaps/{mapId}";
                var response = await _httpClient.GetAsync(mapUrl);

                string mapTitle = "Unknown Map";
                string mapArtist = "Unknown Artist";

                if (response.IsSuccessStatusCode)
                {
                    var mapData = await response.Content.ReadFromJsonAsync<OsuBeatmap>();
                    if (mapData != null)
                    {
                        mapTitle = mapData.BeatmapSet.Title;
                        mapArtist = mapData.BeatmapSet.Artist;
                    }
                }
                else
                {
                    Console.WriteLine($"[Warning] 삭제 요청된 맵({mapId}) 정보를 가져올 수 없습니다.");
                }

                string dbUrl = $"{_firebaseUrl}/delete_requests/{mapId}.json?auth={_firebaseSecret}";

                var requestData = new
                {
                    MapId = mapId,
                    Title = mapTitle,
                    Artist = mapArtist,
                    RequestedBy = requester,
                    RequestedAt = DateTime.UtcNow
                };

                var saveResponse = await _httpClient.PutAsJsonAsync(dbUrl, requestData);

                if (saveResponse.IsSuccessStatusCode)
                {
                    string msg = $"[요청완료] {mapId}번 맵의 삭제 요청이 접수되었습니다. 관리자 검토 후 처리됩니다.";
                    Console.WriteLine($"[Delete Request] {requester} -> {mapId} ({mapTitle})");
                    if (onMessage != null) await onMessage(msg);
                }
                else
                {
                    if (onMessage != null) await onMessage("요청 저장에 실패했습니다.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] 삭제 요청 처리 중 오류: {ex.Message}");
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